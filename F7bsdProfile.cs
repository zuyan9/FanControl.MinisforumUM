namespace FanControl.MinisforumUMSeries;

internal enum CpuStartupState
{
    Firmware,
    Recoverable,
}

internal readonly record struct CpuStartupClassification(
    CpuStartupState State,
    byte Selector,
    byte[] Baseline);

internal enum SystemStartupState
{
    Firmware,
    Recoverable,
    Releasing,
    Unsupported,
}

internal static class F7bsdProfile
{
    internal const string IsaMutexName = "Global\\Access_ISABUS.HTP.Method";
    internal const string LpcResourceName =
        "LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin";
    internal const uint PawnIoApiVersion = 0x00020000;
    internal const byte MaximumCode = 51;
    internal const byte SystemSentinel = 0xff;
    internal const ushort SystemTargetAddress = 0x0885;
    internal const ushort SystemEffectiveTemperatureAddress = 0x0889;
    internal const ushort SystemTemperatureOverrideAddress = 0x088b;

    internal static readonly ushort[] ControllerProfileAddresses =
        [0x2000, 0x2001, 0x2002, 0x200d, 0x180c, 0x1841];

    // Stable low/high/low tachometer reads followed by raw temperatures.
    internal static readonly ushort[] TelemetryAddresses =
    [
        0x181e, 0x181f, 0x181e,
        0x1820, 0x1821, 0x1820,
        0x0309, 0x0305,
    ];
    internal static readonly ushort[] CpuTachAddresses = [0x181e, 0x181f, 0x181e];
    internal static readonly ushort[] SystemTachAddresses = [0x1820, 0x1821, 0x1820];

    internal static readonly ushort[] CpuBaseAddresses = Enumerable.Range(0, 7)
        .Select(row => (ushort)(0x0310 + (row * 3)))
        .ToArray();
    internal static readonly ushort[] CpuSlopeAddresses = Enumerable.Range(0x08b0, 7)
        .Select(address => (ushort)address)
        .ToArray();
    internal static readonly ushort[] CpuOwnedAddresses =
        [.. CpuBaseAddresses, .. CpuSlopeAddresses];

    private const ushort CpuProfileSelectorAddress = 0x032f;
    private const ushort CpuTemperatureOverrideAddress = 0x088a;
    internal static readonly ushort[] CpuBandAddresses = CpuBaseAddresses
        .SelectMany(address => new[] { (ushort)(address + 1), (ushort)(address + 2) })
        .ToArray();
    internal static readonly ushort[] CpuCriticalAddresses =
        [0x0325, 0x0326, 0x0327, 0x08b7];
    internal static readonly ushort[] SystemPolicyAddresses =
    [
        .. Enumerable.Range(0x0330, 9).Select(address => (ushort)address),
        .. Enumerable.Range(0x08c0, 3).Select(address => (ushort)address),
    ];

    // These bytes are configuration, not telemetry, so the backend can compare two
    // complete snapshots and use the selected one as a write precondition.
    internal static readonly ushort[] CpuSnapshotAddresses =
    [
        CpuProfileSelectorAddress,
        CpuTemperatureOverrideAddress,
        .. CpuBandAddresses,
        .. CpuCriticalAddresses,
        .. CpuOwnedAddresses,
    ];
    internal static readonly ushort[] SystemStateAddresses =
    [
        SystemEffectiveTemperatureAddress,
        SystemTemperatureOverrideAddress,
        SystemTargetAddress,
    ];
    internal static readonly ushort[] SystemEffectiveTemperaturePollAddresses =
        [SystemEffectiveTemperatureAddress];

    private static readonly byte[] ExpectedCpuCriticalRow = [51, 100, 93, 0];

    private static readonly HashSet<ushort> ReadAllowlist =
    [
        .. ControllerProfileAddresses,
        .. TelemetryAddresses,
        .. CpuSnapshotAddresses,
        .. SystemPolicyAddresses,
        .. SystemStateAddresses,
    ];

    internal static byte ToCode(float percentage, byte maximumCode)
    {
        AssertCode(0, maximumCode, nameof(maximumCode));
        if (!float.IsFinite(percentage) || percentage < 0 || percentage > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentage),
                "Fan Control output must be a finite value from 0 through 100.");
        }

        return (byte)Math.Round(
            percentage * maximumCode / 100d,
            MidpointRounding.AwayFromZero);
    }

    internal static float ToPercentage(byte code, byte maximumCode)
    {
        AssertCode(code, maximumCode, nameof(code));
        return code * 100f / maximumCode;
    }

    internal static CpuStartupClassification ClassifyCpuStartupSnapshot(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> snapshot)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ValidateCpuSnapshotLength(snapshot);

        CpuProfileDefinition profile = ProfileFor(platform, snapshot[0]);
        int bandsOffset = 2;
        int criticalOffset = bandsOffset + CpuBandAddresses.Length;
        int mutableOffset = criticalOffset + CpuCriticalAddresses.Length;

        if (snapshot[1] != 0)
        {
            throw new PlatformNotSupportedException(
                $"CPU firmware-temperature override is 0x{snapshot[1]:X2}, not zero.");
        }
        if (!snapshot.Slice(bandsOffset, CpuBandAddresses.Length)
            .SequenceEqual(profile.Bands))
        {
            throw new PlatformNotSupportedException(
                $"CPU temperature bands do not match profile 0x{profile.Selector:X2}.");
        }
        if (!snapshot.Slice(criticalOffset, CpuCriticalAddresses.Length)
            .SequenceEqual(ExpectedCpuCriticalRow))
        {
            throw new PlatformNotSupportedException(
                "The CPU critical row is not (51,100,93,0).");
        }

        ReadOnlySpan<byte> mutable = snapshot[mutableOffset..];
        CpuStartupState state = mutable.SequenceEqual(profile.Baseline)
            ? CpuStartupState.Firmware
            : IsRecoverableCpuMutable(mutable, profile.Baseline)
                ? CpuStartupState.Recoverable
                : throw new PlatformNotSupportedException(
                    "The CPU table is neither canonical firmware state nor an exact " +
                    "reachable prefix of this plugin's raw-control writes.");
        return new CpuStartupClassification(
            state,
            profile.Selector,
            profile.Baseline.ToArray());
    }

    internal static void ValidateFirmwareCpuSnapshot(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> snapshot,
        ReadOnlySpan<byte> baseline)
    {
        ValidateCanonicalCpuBaseline(platform, baseline);
        CpuStartupClassification classification =
            ClassifyCpuStartupSnapshot(platform, snapshot);
        if (classification.State != CpuStartupState.Firmware ||
            !classification.Baseline.AsSpan().SequenceEqual(baseline))
        {
            throw new IOException(
                "The canonical CPU firmware table was not restored and verified.");
        }
    }

    internal static EcExpectation[] CpuSnapshotExpectations(
        ReadOnlySpan<byte> snapshot) => BuildExpectations(
            CpuSnapshotAddresses,
            snapshot,
            "Unexpected CPU snapshot length.",
            nameof(snapshot));

    internal static void ValidateCpuWritePrecondition(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> snapshot,
        ReadOnlySpan<byte> baseline,
        byte? activeCode)
    {
        CpuStartupClassification classification =
            ClassifyCpuStartupSnapshot(platform, snapshot);
        if (!classification.Baseline.AsSpan().SequenceEqual(baseline))
        {
            throw new IOException(
                "The BIOS-selected CPU fan profile changed while control was active.");
        }

        if (!activeCode.HasValue)
        {
            if (classification.State != CpuStartupState.Firmware)
            {
                throw new IOException(
                    "CPU control can start only from the canonical firmware table.");
            }
            return;
        }

        AssertCode(activeCode.Value, MaximumCode, nameof(activeCode));
        int mutableOffset = 2 + CpuBandAddresses.Length + CpuCriticalAddresses.Length;
        ReadOnlySpan<byte> mutable = snapshot[mutableOffset..];
        ReadOnlySpan<byte> bases = mutable[..CpuBaseAddresses.Length];
        ReadOnlySpan<byte> slopes = mutable[CpuBaseAddresses.Length..];
        if (bases.ContainsAnyExcept(activeCode.Value) || slopes.ContainsAnyExcept((byte)0))
        {
            throw new IOException(
                "The live CPU table is not the exact state last written by this plugin.");
        }
    }

    internal static SystemStartupState ClassifySystemStartupState(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> state)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ValidateSystemStateLength(state);
        byte effective = state[0];
        byte temperatureOverride = state[1];
        byte target = state[2];
        if (target > platform.SystemMaximumCode)
        {
            return SystemStartupState.Unsupported;
        }
        if (temperatureOverride == 0)
        {
            return PlausibleTemperature(effective)
                ? SystemStartupState.Firmware
                : effective == SystemSentinel
                    ? SystemStartupState.Releasing
                    : SystemStartupState.Unsupported;
        }
        if (temperatureOverride == SystemSentinel &&
            (effective == SystemSentinel || PlausibleTemperature(effective)))
        {
            return SystemStartupState.Recoverable;
        }
        return SystemStartupState.Unsupported;
    }

    internal static void ValidateFirmwareSystemState(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> state)
    {
        SystemStartupState classification = ClassifySystemStartupState(platform, state);
        if (classification != SystemStartupState.Firmware)
        {
            throw new PlatformNotSupportedException(
                $"System fan startup state is {classification}, not firmware-owned.");
        }
    }

    internal static void ValidateOwnedSystemState(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> state,
        byte? expectedTarget = null)
    {
        ValidateSystemStateLength(state);
        if (state[0] != SystemSentinel || state[1] != SystemSentinel)
        {
            throw new IOException("System fixed-target ownership is not active.");
        }
        if (state[2] > platform.SystemMaximumCode ||
            (expectedTarget.HasValue && state[2] != expectedTarget.Value))
        {
            throw new IOException("The system fan target did not match its request.");
        }
    }

    internal static void ValidateSystemPolicy(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> policy)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (policy.Length != SystemPolicyAddresses.Length)
        {
            throw new ArgumentException("Unexpected system-policy length.", nameof(policy));
        }
        if (!policy.SequenceEqual(platform.ExpectedSystemTable))
        {
            throw new PlatformNotSupportedException(
                $"The live system-fan policy does not match {platform.DisplayName}.");
        }
    }

    internal static EcExpectation[] SystemPolicyExpectations(
        ReadOnlySpan<byte> policy) => BuildExpectations(
            SystemPolicyAddresses,
            policy,
            "Unexpected system-policy length.",
            nameof(policy));

    internal static bool PlausibleTemperature(byte value) => value is >= 1 and <= 120;

    internal static EcWrite[] CpuTargetWrites(byte code, bool includeSlopes)
    {
        AssertCode(code, MaximumCode, nameof(code));
        IEnumerable<EcWrite> writes = CpuBaseAddresses.Select(
            address => new EcWrite(address, code));
        if (includeSlopes)
        {
            writes = CpuSlopeAddresses.Select(address => new EcWrite(address, 0))
                .Concat(writes);
        }
        return writes.ToArray();
    }

    internal static EcWrite[] CpuRestoreWrites(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> baseline)
    {
        ValidateCanonicalCpuBaseline(platform, baseline);

        byte[] canonical = baseline.ToArray();
        return CpuSlopeAddresses.Select(address => new EcWrite(address, 0))
            .Concat(CpuBaseAddresses.Select((address, index) =>
                new EcWrite(address, canonical[index])))
            .Concat(CpuSlopeAddresses.Select((address, index) =>
                new EcWrite(address, canonical[CpuBaseAddresses.Length + index])))
            .ToArray();
    }

    internal static void AssertReadsAllowed(IEnumerable<ushort> addresses)
    {
        foreach (ushort address in addresses)
        {
            if (!ReadAllowlist.Contains(address))
            {
                throw new InvalidOperationException(
                    $"EC read address 0x{address:X4} is not allowed.");
            }
        }
    }

    internal static void AssertWritesAllowed(
        F7PlatformProfile platform,
        IEnumerable<EcWrite> writes)
    {
        foreach (EcWrite write in writes)
        {
            bool allowed =
                (write.Address == SystemTargetAddress &&
                    write.Value <= platform.SystemMaximumCode) ||
                (write.Address == SystemTemperatureOverrideAddress &&
                    write.Value is 0 or SystemSentinel);
            if (!allowed)
            {
                throw new InvalidOperationException(
                    $"EC write 0x{write.Address:X4}=0x{write.Value:X2} is not allowed.");
            }
        }
    }

    internal static void AssertCpuWritesAllowed(
        F7PlatformProfile platform,
        IEnumerable<EcWrite> writes,
        ReadOnlySpan<byte> baseline)
    {
        ValidateCanonicalCpuBaseline(platform, baseline);
        foreach (EcWrite write in writes)
        {
            int slopeIndex = Array.IndexOf(CpuSlopeAddresses, write.Address);
            bool allowed =
                (Array.IndexOf(CpuBaseAddresses, write.Address) >= 0 &&
                    write.Value <= MaximumCode) ||
                (slopeIndex >= 0 &&
                    (write.Value == 0 ||
                        write.Value == baseline[CpuBaseAddresses.Length + slopeIndex]));
            if (!allowed)
            {
                throw new InvalidOperationException(
                    $"EC CPU write 0x{write.Address:X4}=0x{write.Value:X2} is not allowed.");
            }
        }
    }

    private static bool IsRecoverableCpuMutable(
        ReadOnlySpan<byte> mutable,
        ReadOnlySpan<byte> baseline)
    {
        if (mutable.Length != CpuOwnedAddresses.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> bases = mutable[..CpuBaseAddresses.Length];
        ReadOnlySpan<byte> slopes = mutable[CpuBaseAddresses.Length..];
        ReadOnlySpan<byte> canonicalBases = baseline[..CpuBaseAddresses.Length];
        ReadOnlySpan<byte> canonicalSlopes = baseline[CpuBaseAddresses.Length..];

        // Restore retries can alternately zero and restore prefixes. Once the bases
        // are canonical, the remaining canonical slopes form at most one contiguous
        // block surrounded by zeroes.
        if (bases.SequenceEqual(canonicalBases))
        {
            return MatchesSlopeRestorePrefix(slopes, canonicalSlopes);
        }

        // Every operation which can leave noncanonical bases has first completed
        // the seven slope-zero writes.
        if (slopes.ContainsAnyExcept((byte)0) || bases.ContainsAnyExceptInRange(
                (byte)0,
                MaximumCode))
        {
            return false;
        }

        return MatchesEnterOrRestorePrefix(bases, canonicalBases) ||
            MatchesUpdateOrRestorePrefix(bases, canonicalBases);
    }

    private static bool MatchesSlopeRestorePrefix(
        ReadOnlySpan<byte> observed,
        ReadOnlySpan<byte> canonical)
    {
        // A restore first zeroes a prefix (eventually all slopes), then restores a
        // canonical prefix. Repeating an interrupted restore can trim either end of
        // the previous canonical block, but cannot create two disjoint blocks.
        for (int start = 0; start <= observed.Length; start++)
        {
            for (int end = start; end <= observed.Length; end++)
            {
                bool match = true;
                for (int index = 0; index < observed.Length; index++)
                {
                    byte expected = index >= start && index < end
                        ? canonical[index]
                        : (byte)0;
                    if (observed[index] != expected)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool MatchesEnterOrRestorePrefix(
        ReadOnlySpan<byte> observed,
        ReadOnlySpan<byte> canonical)
    {
        // Initial control: a prefix becomes one target code, followed by the
        // canonical suffix. Recovery may already have restored a canonical prefix.
        for (int restored = 0; restored < observed.Length; restored++)
        {
            if (!observed[..restored].SequenceEqual(canonical[..restored]))
            {
                continue;
            }
            for (int written = restored + 1; written <= observed.Length; written++)
            {
                byte code = observed[restored];
                if (observed[restored..written].IndexOfAnyExcept(code) < 0 &&
                    observed[written..].SequenceEqual(canonical[written..]))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool MatchesUpdateOrRestorePrefix(
        ReadOnlySpan<byte> observed,
        ReadOnlySpan<byte> canonical)
    {
        // A later target change has at most two constant runs. A failed cleanup or
        // startup recovery can replace any prefix with the canonical profile.
        for (int restored = 0; restored < observed.Length; restored++)
        {
            if (!observed[..restored].SequenceEqual(canonical[..restored]))
            {
                continue;
            }
            ReadOnlySpan<byte> suffix = observed[restored..];
            byte first = suffix[0];
            int transition = suffix.IndexOfAnyExcept(first);
            if (transition < 0)
            {
                return true;
            }
            byte second = suffix[transition];
            if (suffix[transition..].IndexOfAnyExcept(second) < 0)
            {
                return true;
            }
        }
        return false;
    }

    private static CpuProfileDefinition ProfileFor(
        F7PlatformProfile platform,
        byte selector) =>
        platform.CpuProfiles.FirstOrDefault(profile => profile.Selector == selector) ??
        throw new PlatformNotSupportedException(
            $"Unknown {platform.DisplayName} CPU profile selector 0x{selector:X2}.");

    private static void ValidateCanonicalCpuBaseline(
        F7PlatformProfile platform,
        ReadOnlySpan<byte> baseline)
    {
        if (baseline.Length == CpuOwnedAddresses.Length)
        {
            foreach (CpuProfileDefinition profile in platform.CpuProfiles)
            {
                if (baseline.SequenceEqual(profile.Baseline))
                {
                    return;
                }
            }
        }
        throw new ArgumentException(
            $"CPU baseline is not a canonical {platform.DisplayName} firmware table.",
            nameof(baseline));
    }

    private static void AssertCode(
        byte code,
        byte maximumCode,
        string parameterName)
    {
        if (maximumCode is 0 or > MaximumCode || code > maximumCode)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static EcExpectation[] BuildExpectations(
        ReadOnlySpan<ushort> addresses,
        ReadOnlySpan<byte> values,
        string lengthMessage,
        string parameterName)
    {
        if (values.Length != addresses.Length)
        {
            throw new ArgumentException(lengthMessage, parameterName);
        }

        EcExpectation[] expectations = new EcExpectation[values.Length];
        for (int index = 0; index < expectations.Length; index++)
        {
            expectations[index] = new EcExpectation(addresses[index], values[index]);
        }
        return expectations;
    }

    private static void ValidateCpuSnapshotLength(ReadOnlySpan<byte> snapshot)
    {
        if (snapshot.Length != CpuSnapshotAddresses.Length)
        {
            throw new ArgumentException("Unexpected CPU snapshot length.", nameof(snapshot));
        }
    }

    private static void ValidateSystemStateLength(ReadOnlySpan<byte> state)
    {
        if (state.Length != SystemStateAddresses.Length)
        {
            throw new ArgumentException("Unexpected system state length.", nameof(state));
        }
    }
}
