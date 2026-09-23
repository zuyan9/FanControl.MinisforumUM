using FanControl.Plugins;
using System.Runtime.ExceptionServices;

namespace FanControl.MinisforumUMSeries;

public sealed class MinisforumUMSeriesPlugin : IPlugin2
{
    private readonly object lifecycleSync = new();
    private readonly IPluginLogger? logger;
    private readonly Sensor cpuFan = new("cpu-rpm", "CPU Fan");
    private readonly Sensor systemFan = new("system-rpm", "System Fan");
    private readonly Sensor cpuTemperature = new("cpu-temperature", "CPU Temperature");
    private readonly Sensor systemTemperature = new(
        "system-temperature",
        "System Temperature");
    private readonly ControlSensor cpuControl;
    private readonly ControlSensor systemControl;
    private readonly SystemControlDiagnostics systemDiagnostics = new();
    private readonly SystemFanTrace systemTrace = new();
    private PawnIoF7bsdBackend? backend;

    public MinisforumUMSeriesPlugin()
        : this(null)
    {
    }

    public MinisforumUMSeriesPlugin(IPluginLogger? logger)
    {
        this.logger = logger;
        cpuControl = new ControlSensor(
            "cpu-control",
            "CPU Fan Control",
            $"{Name}/{cpuFan.Id}",
            SetCpu,
            ResetCpu);
        systemControl = new ControlSensor(
            "system-control",
            "System Fan Control",
            $"{Name}/{systemFan.Id}",
            SetSystem,
            ResetSystem);
    }

    public string Name => "Minisforum UM Series";

    public void Initialize()
    {
        lock (lifecycleSync)
        {
            Close();
            if (backend is not null)
            {
                throw new InvalidOperationException(
                    "The previous backend still requires verified restoration. " +
                    "Restart Windows before reinitializing the plugin.");
            }

            PawnIoF7bsdBackend candidate = new();
            backend = candidate;
            try
            {
                F7bsdStartupRecovery recovery = candidate.Initialize();
                Apply(candidate.ReadTelemetry());
                Log(StartupMessage(recovery));
                if (candidate.SupportsSystemFanTrace)
                {
                    Log($"{SystemFanTrace.Marker} enabled: sequential read-only samples " +
                        "every 5 seconds; requested is cached, target/ownership/PWM/tach are live.");
                }
            }
            catch (Exception failure)
            {
                ClearTelemetry();
                try
                {
                    candidate.Dispose();
                    backend = null;
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(
                        "Minisforum EC initialization failed and verified recovery " +
                        "remains pending.",
                        failure,
                        cleanup);
                }
                ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
        }
    }

    public void Load(IPluginSensorsContainer container)
    {
        container.FanSensors.AddRange([cpuFan, systemFan]);
        container.TempSensors.AddRange([cpuTemperature, systemTemperature]);
        if (backend?.IsInitialized == true)
        {
            container.ControlSensors.AddRange([cpuControl, systemControl]);
        }
    }

    public void Update()
    {
        lock (lifecycleSync)
        {
            try
            {
                if (backend is not null)
                {
                    F7bsdTelemetry telemetry = backend.ReadTelemetry();
                    Apply(telemetry);
                    string? diagnostic = systemDiagnostics.Observe(
                        backend.ActiveSystemCode,
                        backend.ActiveProfile.SystemMaximumCode,
                        telemetry.SystemFanRpm);
                    if (diagnostic is not null)
                    {
                        Log(diagnostic);
                    }
                }
            }
            catch (Exception exception)
            {
                ClearTelemetry();
                Log($"Minisforum EC telemetry read failed: {exception.Message}");
            }
            // Capture raw diagnostic bytes even when normal telemetry could not
            // decode a stable tach sample. Poll isolates and stops failed reads.
            if (backend is not null)
            {
                string? trace = systemTrace.Poll(backend);
                if (trace is not null)
                {
                    Log(trace);
                }
            }
        }
    }

    public void Close()
    {
        lock (lifecycleSync)
        {
            if (backend is not null)
            {
                try
                {
                    backend.Dispose();
                    backend = null;
                }
                catch (Exception exception)
                {
                    Log($"Minisforum EC restoration remains pending: {exception.Message}");
                    return;
                }
            }

            cpuControl.Clear();
            systemControl.Clear();
            systemDiagnostics.Clear();
            systemTrace.Clear();
            ClearTelemetry();
        }
    }

    private float SetCpu(float percentage) => Set(
        percentage,
        static _ => F7bsdProfile.MaximumCode,
        static (active, code) => active.SetCpu(code));

    private float SetSystem(float percentage) => Set(
        percentage,
        static active => active.ActiveProfile.SystemMaximumCode,
        static (active, code) => active.SetSystem(code));

    private float Set(
        float percentage,
        Func<PawnIoF7bsdBackend, byte> maximumCode,
        Func<PawnIoF7bsdBackend, byte, byte> set)
    {
        lock (lifecycleSync)
        {
            PawnIoF7bsdBackend active = ActiveBackend();
            byte maximum = maximumCode(active);
            byte code = F7bsdProfile.ToCode(percentage, maximum);
            return F7bsdProfile.ToPercentage(set(active, code), maximum);
        }
    }

    private void ResetCpu() => Reset(static active => active.ResetCpu());

    private void ResetSystem()
    {
        lock (lifecycleSync)
        {
            backend?.ResetSystem();
            systemDiagnostics.Clear();
        }
    }

    private void Reset(Action<PawnIoF7bsdBackend> reset)
    {
        lock (lifecycleSync)
        {
            if (backend is not null)
            {
                reset(backend);
            }
        }
    }

    private PawnIoF7bsdBackend ActiveBackend()
    {
        PawnIoF7bsdBackend active = backend ??
            throw new InvalidOperationException("The Minisforum EC backend is unavailable.");
        if (!active.IsInitialized)
        {
            throw new InvalidOperationException(
                "The Minisforum EC backend did not complete initialization.");
        }
        return active;
    }

    private void Apply(F7bsdTelemetry telemetry)
    {
        cpuFan.Value = telemetry.CpuFanRpm;
        systemFan.Value = telemetry.SystemFanRpm;
        cpuTemperature.Value = telemetry.CpuTemperatureC;
        systemTemperature.Value = telemetry.SystemTemperatureC;
    }

    private void ClearTelemetry()
    {
        cpuFan.Value = null;
        systemFan.Value = null;
        cpuTemperature.Value = null;
        systemTemperature.Value = null;
    }

    private void Log(string message)
    {
        try
        {
            logger?.Log(message);
        }
        catch
        {
        }
    }

    private static string StartupMessage(F7bsdStartupRecovery recovery)
    {
        string profile = recovery.CpuSelector switch
        {
            0x00 => "Default",
            0xb1 => "Balance",
            0xb2 => "Performance",
            _ => $"0x{recovery.CpuSelector:X2}",
        };
        List<string> recovered = [];
        if (recovery.SystemRecovered)
        {
            recovered.Add(
                $"system target {recovery.PreviousSystemTarget} released");
        }
        if (recovery.CpuRecovered)
        {
            recovered.Add("CPU table restored");
        }
        string detail = recovered.Count == 0
            ? "no startup recovery needed"
            : "startup recovery: " + string.Join(", ", recovered);
        return $"Minisforum {recovery.ProfileName} initialized " +
            $"({recovery.Host}; {profile}; controls enabled; {detail}).";
    }

    private sealed class Sensor(string id, string name) : IPluginSensor
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public float? Value { get; internal set; }

        public void Update() { }
    }

    private sealed class ControlSensor(
        string id,
        string name,
        string pairedFanSensorId,
        Func<float, float> set,
        Action reset) : IPluginControlSensor2
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public string PairedFanSensorId { get; } = pairedFanSensorId;

        public float? Value { get; private set; }

        public void Set(float value)
        {
            Value = set(value);
        }

        public void Reset()
        {
            reset();
            Value = null;
        }

        public void Update() { }

        internal void Clear() => Value = null;
    }
}

internal sealed class SystemControlDiagnostics(TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan StableDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private byte? observedCode;
    private byte? reportedCode;
    private long changedAt;
    private long? reportedAt;

    internal string? Observe(byte? code, byte maximumCode, int sampledRpm)
    {
        if (!code.HasValue)
        {
            Clear();
            return null;
        }

        long now = clock.GetTimestamp();
        if (observedCode != code)
        {
            observedCode = code;
            changedAt = now;
        }
        if (reportedCode == code ||
            clock.GetElapsedTime(changedAt, now) < StableDelay ||
            (reportedAt.HasValue &&
                clock.GetElapsedTime(reportedAt.Value, now) < MinimumInterval))
        {
            return null;
        }

        reportedCode = code;
        reportedAt = now;
        return $"Minisforum system target code {code.Value}/{maximumCode} " +
            "was accepted and read back with the raw handoff verified; " +
            $"latest sampled speed is {sampledRpm} RPM.";
    }

    internal void Clear()
    {
        observedCode = null;
        reportedCode = null;
        reportedAt = null;
    }
}
