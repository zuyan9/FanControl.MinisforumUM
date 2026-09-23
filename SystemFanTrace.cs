using System.Globalization;

namespace FanControl.MinisforumUMSeries;

// Opt-in diagnostic build only. These reads never participate in control,
// admission, or restoration decisions, and add no EC write permissions.
internal sealed class SystemFanTrace(TimeProvider? timeProvider = null)
{
    internal static bool Enabled
    {
        get
        {
#if SYSTEM_FAN_DIAGNOSTICS
            return true;
#else
            return false;
#endif
        }
    }

    internal const string Marker = "Minisforum system diagnostic";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    // Bracket the sample with live ownership/target and duty reads. The ISA
    // mutex serializes cooperating host clients, not EC execution: even matching
    // endpoints are observations, never a claim that this batch is atomic.
    internal static readonly ushort[] Addresses =
    [
        0x0889, 0x088b, 0x0885, 0x1804,
        0x1820, 0x1821, 0x1820,
        0x0309, 0x0305,
        0x0680, 0x06a1, 0x0891,
        0x1803, 0x180c, 0x1841, 0x1612,
        0x0889, 0x088b, 0x0885, 0x1804,
    ];

    // Shared IT5571 hardware registers plus already guarded policy tables.
    // These snapshots are observations, not a write surface or pinout inference.
    internal static ushort[] ConfigurationAddresses =>
    [
        .. F7bsdProfile.ControllerProfileAddresses,
        0x1600, 0x16f1, 0x16f5, 0x16e4, 0x16e7, 0x16e8,
        0x1611, 0x1612, 0x162e, 0x162f, 0x165a, 0x165b,
        0x1800, 0x1801, 0x1803, 0x1804, 0x180a, 0x180b, 0x180d, 0x180f,
        0x1823, 0x1827, 0x182b, 0x1840, 0x1848, 0x185a,
        .. F7bsdProfile.CpuSnapshotAddresses,
        .. F7bsdProfile.SystemPolicyAddresses,
    ];

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private long? sampledAt;
    private long? startedAt;
    private bool failed;

    internal string? Poll(PawnIoF7bsdBackend backend)
    {
        if (!backend.SupportsSystemFanTrace || failed)
        {
            return null;
        }
        long now = clock.GetTimestamp();
        if (sampledAt.HasValue &&
            clock.GetElapsedTime(sampledAt.Value, now) < Interval)
        {
            return null;
        }

        // Limit attempts too: a failed read must not become a busy retry loop.
        sampledAt = now;
        startedAt ??= now;
        try
        {
            SystemFanTraceSample sample = backend.ReadSystemFanTrace();
            return Format(sample, clock.GetElapsedTime(startedAt.Value, now));
        }
        catch (Exception exception)
        {
            failed = true;
            return $"{Marker}: diagnostic sampling stopped after a read failure: " +
                exception.Message;
        }
    }

    internal void Clear()
    {
        sampledAt = null;
        startedAt = null;
        failed = false;
    }

    internal static string Format(SystemFanTraceSample sample, TimeSpan elapsed)
    {
        byte[] values = sample.Values;
        if (values.Length != Addresses.Length)
        {
            throw new IOException("Unexpected system diagnostic sample length.");
        }

        string requested = sample.RequestedCode.HasValue
            ? sample.RequestedCode.Value.ToString(CultureInfo.InvariantCulture)
            : "none";
        bool stableTach = F7bsdTelemetryDecoder.TryDecodeCounter(
            values.AsSpan(4, 3), out int rpm);
        string speed = stableTach
            ? rpm.ToString(CultureInfo.InvariantCulture)
            : "unstable";
        bool sameState = values.AsSpan(0, 3).SequenceEqual(values.AsSpan(16, 3));
        string ownership = !sameState
            ? "changed-during-read"
            : values[0] == 0xff && values[1] == 0xff
                ? "fixed-target"
                : values[1] == 0 && F7bsdProfile.PlausibleTemperature(values[0])
                    ? "firmware"
                    : "other";

        string seconds = elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
        return $"{Marker}: t={seconds}s; requested={requested}/{sample.MaximumCode} (cached); " +
            $"live-target={values[2]}->{values[18]}; " +
            $"effective=0x{values[0]:X2}->0x{values[16]:X2}; " +
            $"override=0x{values[1]:X2}->0x{values[17]:X2}; ownership={ownership}; " +
            $"pwm2={values[3]}->{values[19]}; ctr1={values[14]}; " +
            $"rpm={speed}; tach={values[4]:X2}/{values[5]:X2}/{values[6]:X2}; " +
            $"cpu-temp-raw={values[7]}; system-temp-raw={values[8]}; " +
            $"ram0680=0x{values[9]:X2}; ram06A1=0x{values[10]:X2}; ram0891={values[11]}; " +
            $"pwm1={values[12]}; clock-select=0x{values[13]:X2}; gpa2=0x{values[15]:X2}; " +
            "sequential-read=true.";
    }
}

internal sealed record SystemFanTraceSample(
    byte? RequestedCode,
    byte MaximumCode,
    byte[] Values);

internal readonly record struct EcRegisterValue(ushort Address, byte Value);
