using System.Runtime.ExceptionServices;

namespace FanControl.MinisforumUMSeries;

internal sealed record SystemFanDebugOptions(int HoldSeconds = 60, int? TargetPercent = null)
{
    internal void Validate()
    {
        if (HoldSeconds is < 5 or > 300)
            throw new ArgumentOutOfRangeException(nameof(HoldSeconds), "Hold time must be 5–300 seconds.");
        if (TargetPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(TargetPercent), "Target must be 0–100 percent.");
    }
}

// A single-threaded active test. This shares the plugin's guarded target writes
// and verified recovery; it never writes duty, pin modes, or clock registers.
internal sealed class SystemFanDebugSession(
    PawnIoF7bsdBackend backend,
    Action<string> log,
    Action? assertExclusive = null,
    TimeProvider? clock = null,
    Action<TimeSpan, CancellationToken>? wait = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Action<TimeSpan, CancellationToken> delay = wait ?? Wait;

    internal void Run(SystemFanDebugOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        if (!SystemFanTrace.Enabled)
            throw new InvalidOperationException("Active diagnostics require a diagnostic build.");

        long started = time.GetTimestamp();
        Exception? failure = null;
        bool initialized = false;
        try
        {
            Check();
            F7bsdStartupRecovery recovery = backend.Initialize();
            initialized = true;
            log($"Host: {recovery.Host}; profile={backend.ActiveProfile.Id}; " +
                $"CPU selector=0x{recovery.CpuSelector:X2}; " +
                $"startup CPU recovery={recovery.CpuRecovered}; startup system recovery={recovery.SystemRecovered}.");
            Configuration("before");
            Observe("firmware-baseline", 10, null);

            int[] targets = options.TargetPercent is int target ? [target] : [100, 70, 40];
            foreach (int percentage in targets)
            {
                Check();
                byte code = F7bsdProfile.ToCode(percentage, backend.ActiveProfile.SystemMaximumCode);
                log($"Request system target {percentage}%: code={code}/{backend.ActiveProfile.SystemMaximumCode}, " +
                    $"nominal={code * 100} RPM; holding {options.HoldSeconds}s. This is not PWM duty percent.");
                backend.SetSystem(code);
                Observe($"target-{percentage}", options.HoldSeconds, code);
            }

            log("Returning system control to firmware.");
            backend.ResetSystem();
            Observe("firmware-recovery", 15, null);
            Configuration("after");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            // Cancellation and logging/read failures never skip recovery. The
            // existing backend also handles partially completed initialization.
            bool restored = false;
            int attempts = 0;
            for (; attempts < 2; attempts++)
            {
                try
                {
                    backend.Dispose();
                    restored = true;
                    break;
                }
                catch (Exception cleanup)
                {
                    // One bounded retry uses existing recovery state, not guessed
                    // values or a new transport. Retain the initial error.
                    failure = Combine(failure, cleanup);
                }
            }
            try
            {
                log(!restored
                    ? "RESTORATION NOT VERIFIED. Fully power off before another attempt."
                    : attempts > 0
                        ? "Cleanup retry completed; the initial cleanup error is retained in this report."
                        : initialized
                            ? "Cleanup complete: firmware control restored and transport closed."
                            : "Cleanup complete; initialization did not complete.");
            }
            catch (Exception logging)
            {
                // A full disk is not a restoration failure and must not trigger
                // another cleanup attempt or a false hardware-state warning.
                failure = Combine(failure, logging);
            }
        }
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        void Check()
        {
            cancellationToken.ThrowIfCancellationRequested();
            assertExclusive?.Invoke();
        }

        void Configuration(string phase)
        {
            Check();
            EcRegisterValue[] values = backend.ReadSystemFanConfiguration();
            log($"Configuration {phase} (raw sequential reads): " + string.Join(" ",
                values.Select(value => $"{value.Address:X4}={value.Value:X2}")));
        }

        void Observe(string stage, int seconds, byte? expectedTarget)
        {
            long stageStarted = time.GetTimestamp();
            int samples = 0;
            int? minimumRpm = null;
            int? maximumRpm = null;
            byte minimumDuty = byte.MaxValue;
            byte maximumDuty = 0;
            while (true)
            {
                Check();
                SystemFanTraceSample sample = backend.ReadSystemFanTrace();
                log($"stage={stage}; " + SystemFanTrace.Format(sample, time.GetElapsedTime(started)));
                // Log before checking so an ownership loss or target overwrite
                // is recorded. Do not repeatedly reassert a stage target.
                ReadOnlySpan<byte> before = sample.Values.AsSpan(0, 3);
                ReadOnlySpan<byte> after = sample.Values.AsSpan(16, 3);
                if (expectedTarget.HasValue)
                {
                    F7bsdProfile.ValidateOwnedSystemState(backend.ActiveProfile, before, expectedTarget);
                    F7bsdProfile.ValidateOwnedSystemState(backend.ActiveProfile, after, expectedTarget);
                }
                else
                {
                    F7bsdProfile.ValidateFirmwareSystemState(backend.ActiveProfile, before);
                    F7bsdProfile.ValidateFirmwareSystemState(backend.ActiveProfile, after);
                }
                samples++;
                minimumDuty = Math.Min(minimumDuty, Math.Min(sample.Values[3], sample.Values[19]));
                maximumDuty = Math.Max(maximumDuty, Math.Max(sample.Values[3], sample.Values[19]));
                if (F7bsdTelemetryDecoder.TryDecodeCounter(sample.Values.AsSpan(4, 3), out int rpm))
                {
                    minimumRpm = minimumRpm.HasValue ? Math.Min(minimumRpm.Value, rpm) : rpm;
                    maximumRpm = maximumRpm.HasValue ? Math.Max(maximumRpm.Value, rpm) : rpm;
                }
                TimeSpan remaining = TimeSpan.FromSeconds(seconds) - time.GetElapsedTime(stageStarted);
                if (remaining <= TimeSpan.Zero)
                    break;
                delay(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2), cancellationToken);
            }
            log($"Summary stage={stage}: samples={samples}; pwm2-range={minimumDuty}..{maximumDuty}; " +
                $"rpm-range={minimumRpm?.ToString() ?? "unknown"}..{maximumRpm?.ToString() ?? "unknown"}. " +
                "Ranges are observations, not a diagnosis or proof of settled speed.");
        }
    }

    private static void Wait(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (cancellationToken.WaitHandle.WaitOne(duration))
            cancellationToken.ThrowIfCancellationRequested();
    }

    private static Exception Combine(Exception? first, Exception second) =>
        first is null ? second : new AggregateException(first, second);
}
