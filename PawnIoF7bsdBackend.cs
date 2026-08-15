namespace FanControl.MinisforumUMSeries;

internal sealed class PawnIoF7bsdBackend : IDisposable
{
    private const int StableSnapshotAttempts = 4;
    private const int SystemReleaseSteps = 4;
    private const int SystemHandoffPollAttempts = 16;
    private static readonly TimeSpan SystemHandoffPollDelay =
        TimeSpan.FromMilliseconds(100);

    private readonly Func<HostIdentitySnapshot> hostIdentityReader;
    private readonly Func<F7PlatformProfile, IF7Transport> transportFactory;
    private IF7Transport? transport;
    private F7PlatformProfile? profile;
    private byte[]? cpuBaseline;
    private byte? cpuCode;
    private byte? systemCode;
    private bool systemMayBeOwned;
    private bool cpuRestorePending;
    private bool systemRestorePending;
    private F7bsdStartupRecovery? startupRecovery;

    internal PawnIoF7bsdBackend()
        : this(
            HostIdentity.Read,
            static selected => new PawnIoTransport(selected))
    {
    }

    internal PawnIoF7bsdBackend(
        Func<HostIdentitySnapshot> hostIdentityReader,
        Func<F7PlatformProfile, IF7Transport> transportFactory)
    {
        this.hostIdentityReader = hostIdentityReader ??
            throw new ArgumentNullException(nameof(hostIdentityReader));
        this.transportFactory = transportFactory ??
            throw new ArgumentNullException(nameof(transportFactory));
    }

    internal bool IsInitialized => startupRecovery.HasValue;

    internal F7PlatformProfile ActiveProfile => profile ??
        throw new InvalidOperationException("The platform profile is unavailable.");

    internal F7bsdStartupRecovery Initialize()
    {
        if (transport is not null)
        {
            return startupRecovery ?? throw new InvalidOperationException(
                "Minisforum EC initialization did not complete.");
        }

        HostIdentitySnapshot host = hostIdentityReader();
        F7PlatformProfile selected = F7ProfileCatalog.Resolve(host);
        profile = selected;
        IF7Transport active = transportFactory(selected);
        transport = active;

        byte[] cpuSnapshot = ReadStable(
            active,
            F7bsdProfile.CpuSnapshotAddresses,
            "CPU fan table");
        CpuStartupClassification cpuState =
            F7bsdProfile.ClassifyCpuStartupSnapshot(selected, cpuSnapshot);
        byte[] systemPolicy = ReadStable(
            active,
            F7bsdProfile.SystemPolicyAddresses,
            "system-fan policy");
        F7bsdProfile.ValidateSystemPolicy(selected, systemPolicy);
        byte[] systemSnapshot = active.Read(F7bsdProfile.SystemStateAddresses);
        SystemStartupState systemState =
            F7bsdProfile.ClassifySystemStartupState(selected, systemSnapshot);
        if (systemState == SystemStartupState.Unsupported)
        {
            throw new PlatformNotSupportedException(
                "The system fan state is not firmware-owned or an exact " +
                "recoverable raw-control handoff.");
        }

        bool recoveredCpu = cpuState.State == CpuStartupState.Recoverable;
        bool recoveredSystem = systemState != SystemStartupState.Firmware;
        cpuBaseline = cpuState.Baseline;
        cpuRestorePending = recoveredCpu;
        byte? previousSystemTarget = recoveredSystem ? systemSnapshot[2] : null;
        if (recoveredSystem)
        {
            systemMayBeOwned = systemState == SystemStartupState.Recoverable;
            systemRestorePending = true;
            ReleaseSystemCore(active);
        }

        if (recoveredCpu)
        {
            RestoreCpuCore(active);
        }
        else
        {
            F7bsdProfile.ValidateFirmwareCpuSnapshot(
                selected,
                ReadStable(
                    active,
                    F7bsdProfile.CpuSnapshotAddresses,
                    "CPU fan table"),
                ActiveCpuBaseline());
        }
        F7bsdProfile.ValidateSystemPolicy(
            selected,
            ReadStable(
                active,
                F7bsdProfile.SystemPolicyAddresses,
                "system-fan policy"));

        F7bsdStartupRecovery result = new(
            selected.DisplayName,
            cpuState.Selector,
            recoveredCpu,
            recoveredSystem,
            previousSystemTarget);
        startupRecovery = result;
        return result;
    }

    internal F7bsdTelemetry ReadTelemetry()
    {
        EnsureInitialized();
        IF7Transport active = ActiveTransport();
        byte[] sample = active.Read(F7bsdProfile.TelemetryAddresses);
        int cpuRpm = ReadStableCounter(
            active,
            sample.AsSpan(0, 3),
            F7bsdProfile.CpuTachAddresses,
            "CPU");
        int systemRpm = ReadStableCounter(
            active,
            sample.AsSpan(3, 3),
            F7bsdProfile.SystemTachAddresses,
            "system");
        return new F7bsdTelemetry(cpuRpm, systemRpm, sample[6], sample[7]);
    }

    internal void ResetCpu()
    {
        EnsureInitialized();
        RestoreCpu(ActiveTransport());
    }

    internal void ResetSystem()
    {
        EnsureInitialized();
        ReleaseSystem(ActiveTransport());
    }

    public void Dispose()
    {
        IF7Transport? old = transport;
        if (old is null)
        {
            return;
        }

        // Stop public telemetry/control calls before restoration begins. Private
        // recovery below remains available for retries if cleanup fails.
        startupRecovery = null;
        List<Exception> failures = [];
        TryRestore(
            systemMayBeOwned || systemRestorePending,
            () => ReleaseSystem(old));
        TryRestore(
            cpuCode.HasValue || cpuRestorePending,
            () => RestoreCpu(old));
        if (failures.Count != 0)
        {
            throw new AggregateException("Minisforum EC restoration failed.", failures);
        }

        old.Dispose();
        transport = null;
        profile = null;
        cpuBaseline = null;

        void TryRestore(bool needed, Action restore)
        {
            if (!needed)
            {
                return;
            }
            try
            {
                restore();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    internal byte SetCpu(byte requestedCode)
    {
        EnsureInitialized();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            requestedCode,
            F7bsdProfile.MaximumCode);
        IF7Transport active = ActiveTransport();
        if (cpuRestorePending)
        {
            throw new InvalidOperationException(
                "CPU restoration is pending; reset the control or refresh the plugin.");
        }
        try
        {
            byte[] snapshot = ReadStable(
                active,
                F7bsdProfile.CpuSnapshotAddresses,
                "CPU fan table");
            byte[] baseline = ActiveCpuBaseline();
            F7bsdProfile.ValidateCpuWritePrecondition(
                ActiveProfile,
                snapshot,
                baseline,
                cpuCode);
            active.WriteCpuVerified(
                F7bsdProfile.CpuSnapshotExpectations(snapshot),
                F7bsdProfile.CpuTargetWrites(
                    requestedCode,
                    includeSlopes: !cpuCode.HasValue),
                baseline);
            cpuCode = requestedCode;
            return requestedCode;
        }
        catch (Exception failure)
        {
            cpuRestorePending = true;
            try
            {
                RestoreCpuCore(active);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(
                    "CPU control failed and canonical-table restoration is incomplete.",
                    failure,
                    cleanup);
            }
            throw;
        }
    }

    private void RestoreCpu(IF7Transport active)
    {
        if (!cpuCode.HasValue && !cpuRestorePending)
        {
            return;
        }
        cpuRestorePending = true;
        RestoreCpuCore(active);
    }

    private void RestoreCpuCore(IF7Transport active)
    {
        byte[] baseline = ActiveCpuBaseline();
        byte[] snapshot = ReadStable(
            active,
            F7bsdProfile.CpuSnapshotAddresses,
            "CPU fan table");
        CpuStartupClassification classification =
            F7bsdProfile.ClassifyCpuStartupSnapshot(ActiveProfile, snapshot);
        EnsureCpuProfile(classification, baseline);

        Exception? writeFailure = null;
        if (classification.State == CpuStartupState.Recoverable)
        {
            try
            {
                active.WriteCpuVerified(
                    F7bsdProfile.CpuSnapshotExpectations(snapshot),
                    F7bsdProfile.CpuRestoreWrites(ActiveProfile, baseline),
                    baseline);
            }
            catch (Exception exception)
            {
                writeFailure = exception;
            }
        }

        try
        {
            byte[] verified = ReadStable(
                active,
                F7bsdProfile.CpuSnapshotAddresses,
                "CPU fan table");
            F7bsdProfile.ValidateFirmwareCpuSnapshot(
                ActiveProfile,
                verified,
                baseline);
        }
        catch (Exception verificationFailure)
        {
            cpuRestorePending = true;
            if (writeFailure is not null)
            {
                throw new AggregateException(writeFailure, verificationFailure);
            }
            throw;
        }

        cpuCode = null;
        cpuRestorePending = false;
    }

    internal byte SetSystem(byte requestedCode)
    {
        EnsureInitialized();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            requestedCode,
            ActiveProfile.SystemMaximumCode);
        IF7Transport active = ActiveTransport();
        if (systemRestorePending)
        {
            throw new InvalidOperationException(
                "System-fan ownership release is pending; reset the control or " +
                "refresh the plugin.");
        }
        try
        {
            byte[] policy = ReadStable(
                active,
                F7bsdProfile.SystemPolicyAddresses,
                "system-fan policy");
            F7bsdProfile.ValidateSystemPolicy(ActiveProfile, policy);
            EcExpectation[] policyExpectations =
                F7bsdProfile.SystemPolicyExpectations(policy);
            byte? expectedTarget = systemCode;
            if (!systemMayBeOwned)
            {
                expectedTarget = EngageSystem(active, policyExpectations);
            }

            List<EcExpectation> before =
            [
                .. policyExpectations,
                new EcExpectation(
                    F7bsdProfile.SystemEffectiveTemperatureAddress,
                    F7bsdProfile.SystemSentinel),
                new EcExpectation(
                    F7bsdProfile.SystemTemperatureOverrideAddress,
                    F7bsdProfile.SystemSentinel),
            ];
            if (expectedTarget.HasValue)
            {
                before.Add(new EcExpectation(
                    F7bsdProfile.SystemTargetAddress,
                    expectedTarget.Value));
            }
            active.WriteVerified(
                before.ToArray(),
                [new EcWrite(F7bsdProfile.SystemTargetAddress, requestedCode)]);
            F7bsdProfile.ValidateOwnedSystemState(
                ActiveProfile,
                active.Read(F7bsdProfile.SystemStateAddresses),
                requestedCode);
            systemCode = requestedCode;
            return requestedCode;
        }
        catch (Exception failure)
        {
            if (!systemMayBeOwned)
            {
                throw;
            }
            systemRestorePending = true;
            try
            {
                ReleaseSystemCore(active);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(
                    "System control failed and firmware ownership release is incomplete.",
                    failure,
                    cleanup);
            }
            throw;
        }
    }

    private byte EngageSystem(
        IF7Transport active,
        EcExpectation[] policyExpectations)
    {
        byte[] state = active.Read(F7bsdProfile.SystemStateAddresses);
        F7bsdProfile.ValidateFirmwareSystemState(ActiveProfile, state);
        active.WriteVerified(
            [
                .. policyExpectations,
                .. F7bsdProfile.SystemStateAddresses.Select((address, index) =>
                    new EcExpectation(address, state[index])),
            ],
            [new EcWrite(
                F7bsdProfile.SystemTemperatureOverrideAddress,
                F7bsdProfile.SystemSentinel)],
            () =>
            {
                systemMayBeOwned = true;
                systemRestorePending = true;
            });
        WaitForSystemEffective(active, owned: true);
        systemRestorePending = false;
        return state[2];
    }

    private void ReleaseSystem(IF7Transport active)
    {
        if (!systemMayBeOwned && !systemRestorePending)
        {
            return;
        }
        systemRestorePending = true;
        ReleaseSystemCore(active);
    }

    private void ReleaseSystemCore(IF7Transport active)
    {
        Exception? transitionFailure = null;
        (byte Override, byte Target)? unchangedAfterFailure = null;
        for (int step = 0; step < SystemReleaseSteps; step++)
        {
            byte[] state;
            try
            {
                state = active.Read(F7bsdProfile.SystemStateAddresses);
            }
            catch (Exception readFailure)
            {
                throw Combine(transitionFailure, readFailure);
            }
            if (unchangedAfterFailure is not null &&
                state[1] == unchangedAfterFailure.Value.Override &&
                state[2] == unchangedAfterFailure.Value.Target)
            {
                throw transitionFailure!;
            }
            unchangedAfterFailure = null;

            SystemStartupState classification =
                F7bsdProfile.ClassifySystemStartupState(ActiveProfile, state);
            switch (classification)
            {
                case SystemStartupState.Firmware:
                    CompleteSystemRelease();
                    return;

                case SystemStartupState.Releasing:
                    try
                    {
                        VerifySystemReleased(active);
                        CompleteSystemRelease();
                        return;
                    }
                    catch (Exception verificationFailure)
                    {
                        throw Combine(transitionFailure, verificationFailure);
                    }

                case SystemStartupState.Recoverable:
                    systemMayBeOwned = true;
                    systemRestorePending = true;
                    EcExpectation[] before =
                    [
                        new EcExpectation(
                            F7bsdProfile.SystemTemperatureOverrideAddress,
                            F7bsdProfile.SystemSentinel),
                        new EcExpectation(F7bsdProfile.SystemTargetAddress, state[2]),
                    ];
                    EcWrite write = state[2] == ActiveProfile.SystemMaximumCode
                        ? new EcWrite(F7bsdProfile.SystemTemperatureOverrideAddress, 0)
                        : new EcWrite(
                            F7bsdProfile.SystemTargetAddress,
                            ActiveProfile.SystemMaximumCode);
                    try
                    {
                        active.WriteVerified(before, [write]);
                    }
                    catch (Exception writeFailure)
                    {
                        transitionFailure = Combine(transitionFailure, writeFailure);
                        unchangedAfterFailure = (state[1], state[2]);
                    }
                    break;

                default:
                    IOException unsupported = new(
                        "The system fan state changed to an unsupported handoff state; " +
                        "no recovery write was attempted.");
                    throw Combine(transitionFailure, unsupported);
            }
        }

        IOException exhausted = new(
            "System-fan ownership release did not reach firmware state.");
        throw Combine(transitionFailure, exhausted);
    }

    private void VerifySystemReleased(IF7Transport active)
    {
        WaitForSystemEffective(active, owned: false);
        byte[] state = active.Read(F7bsdProfile.SystemStateAddresses);
        if (F7bsdProfile.ClassifySystemStartupState(ActiveProfile, state) !=
            SystemStartupState.Firmware)
        {
            throw new IOException("Firmware did not resume system-fan ownership.");
        }
    }

    private static void WaitForSystemEffective(
        IF7Transport active,
        bool owned)
    {
        byte last = 0;
        for (int attempt = 0; attempt < SystemHandoffPollAttempts; attempt++)
        {
            last = active.Read(
                F7bsdProfile.SystemEffectiveTemperaturePollAddresses)[0];
            if (owned
                    ? last == F7bsdProfile.SystemSentinel
                    : F7bsdProfile.PlausibleTemperature(last))
            {
                return;
            }
            if (attempt + 1 < SystemHandoffPollAttempts)
            {
                Thread.Sleep(SystemHandoffPollDelay);
            }
        }

        string direction = owned ? "enter raw mode" : "return to firmware mode";
        throw new IOException(
            $"System fan did not {direction}; effective byte ended at 0x{last:X2}.");
    }

    private static byte[] ReadStable(
        IF7Transport active,
        ushort[] addresses,
        string description)
    {
        byte[] previous = active.Read(addresses);
        for (int attempt = 1; attempt < StableSnapshotAttempts; attempt++)
        {
            byte[] current = active.Read(addresses);
            if (current.AsSpan().SequenceEqual(previous))
            {
                return current;
            }
            previous = current;
        }
        throw new IOException($"The {description} did not produce a stable snapshot.");
    }

    private static void EnsureCpuProfile(
        CpuStartupClassification classification,
        ReadOnlySpan<byte> baseline)
    {
        if (!classification.Baseline.AsSpan().SequenceEqual(baseline))
        {
            throw new IOException(
                "The BIOS-selected CPU fan profile changed while the plugin was loaded.");
        }
    }

    private void CompleteSystemRelease()
    {
        systemMayBeOwned = false;
        systemCode = null;
        systemRestorePending = false;
    }

    private IF7Transport ActiveTransport() => transport ??
        throw new InvalidOperationException("The Minisforum EC backend is not initialized.");

    private byte[] ActiveCpuBaseline() => cpuBaseline ??
        throw new InvalidOperationException("The canonical CPU baseline is unavailable.");

    private void EnsureInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException(
                "The Minisforum EC backend did not complete initialization.");
        }
    }

    private static int ReadStableCounter(
        IF7Transport active,
        ReadOnlySpan<byte> initial,
        ushort[] addresses,
        string name)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ReadOnlySpan<byte> sample = attempt == 0
                ? initial
                : active.Read(addresses);
            if (F7bsdTelemetryDecoder.TryDecodeCounter(sample, out int rpm))
            {
                return rpm;
            }
        }
        throw new IOException(
            $"The EC {name} tachometer did not produce a stable sample.");
    }

    private static Exception Combine(Exception? first, Exception second) =>
        first is null ? second : new AggregateException(first, second);

}

internal readonly record struct F7bsdStartupRecovery(
    string ProfileName,
    byte CpuSelector,
    bool CpuRecovered,
    bool SystemRecovered,
    byte? PreviousSystemTarget);

internal sealed record F7bsdTelemetry(
    int CpuFanRpm,
    int SystemFanRpm,
    int CpuTemperatureC,
    int SystemTemperatureC);

internal static class F7bsdTelemetryDecoder
{
    internal static bool TryDecodeCounter(
        ReadOnlySpan<byte> lowHighLow,
        out int rpm)
    {
        if (lowHighLow.Length != 3)
        {
            throw new ArgumentException(
                "A tachometer sample must contain low/high/low bytes.",
                nameof(lowHighLow));
        }
        if (lowHighLow[0] != lowHighLow[2])
        {
            rpm = 0;
            return false;
        }

        ushort counter = (ushort)(lowHighLow[0] | (lowHighLow[1] << 8));
        rpm = counter == 0 ? 0 : 2_156_250 / counter;
        return true;
    }
}
