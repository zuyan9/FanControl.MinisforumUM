using FanControl.MinisforumUMSeries;

namespace FanControl.MinisforumUMSeries.Tests;

internal static class Program
{
    private static readonly byte[] StandardBands =
    [
        25, 0,
        45, 25,
        54, 45,
        66, 54,
        76, 66,
        88, 76,
        93, 88,
    ];

    private static readonly byte[] PerformanceBands =
    [
        25, 0,
        45, 25,
        54, 45,
        66, 54,
        80, 66,
        88, 80,
        93, 88,
    ];

    private static readonly byte[] StandardSystemPolicy =
    [
        0, 25, 0,
        20, 83, 25,
        51, 100, 83,
        0, 0, 0,
    ];

    private static readonly byte[] ReducedSystemPolicy =
    [
        0, 25, 0,
        15, 83, 25,
        40, 100, 83,
        0, 0, 0,
    ];

    private static readonly SupportedHost[] SupportedHosts =
    [
        new("f7bsd", F7bsdHost()),
        new("f7bsh-f7bsd", F7bshHost()),
        new("f7bsc", F7bscHost("1.07")),
        new("f7bsc", F7bscHost("1.09")),
        new("f7bsi", F7bsiHost("F7BSI", "MGF7BSI", "1.08")),
        new("f7bsi", F7bsiHost("F7BSI", "MGF7BSI", "1.09")),
        new("f7bsi", F7bsiHost("F7BSW", "MGF7BSW", "1.01")),
        new("hpbsd", HpbsdHost(ecMinor: 1)),
        new("hpbsd", HpbsdHost(ecMinor: 2)),
    ];

    private static readonly ExpectedProfile[] ExpectedProfiles =
    [
        new(
            "F7BSD",
            "f7bsd",
            StandardSystemPolicy,
            51,
            [
                new(0x00, StandardBands,
                    [0, 16, 18, 21, 28, 34, 36, 0, 10, 33, 58, 60, 16, 200]),
                new(0xb1, StandardBands,
                    [0, 16, 18, 21, 28, 32, 33, 0, 10, 33, 58, 60, 16, 200]),
                new(0xb2, PerformanceBands,
                    [0, 18, 21, 28, 36, 42, 46, 0, 15, 77, 66, 40, 50, 100]),
            ]),
        new(
            "F7BSC",
            "f7bsc",
            StandardSystemPolicy,
            51,
            [
                new(0x00, StandardBands,
                    [0, 16, 18, 21, 28, 34, 36, 0, 10, 33, 58, 60, 16, 200]),
                new(0xb1, StandardBands,
                    [0, 16, 18, 21, 28, 34, 36, 0, 10, 33, 58, 60, 16, 200]),
                new(0xb2, StandardBands,
                    [0, 18, 21, 28, 36, 40, 46, 0, 15, 77, 66, 40, 50, 100]),
            ]),
        new(
            "F7BSI",
            "f7bsi",
            ReducedSystemPolicy,
            51,
            [
                new(0x00, StandardBands,
                    [0, 16, 18, 22, 25, 27, 29, 0, 10, 44, 25, 20, 16, 75]),
                new(0xb1, StandardBands,
                    [0, 16, 18, 22, 25, 27, 29, 0, 10, 44, 25, 20, 16, 75]),
                new(0xb2, PerformanceBands,
                    [0, 18, 22, 29, 31, 34, 37, 0, 20, 77, 16, 25, 37, 125]),
            ]),
        new(
            "HPBSD",
            "hpbsd",
            ReducedSystemPolicy,
            40,
            [
                new(0xb1, StandardBands,
                    [0, 18, 22, 24, 28, 32, 34, 0, 14, 16, 21, 28, 10, 154]),
                new(0xb2, PerformanceBands,
                    [0, 18, 21, 28, 30, 33, 36, 0, 15, 77, 16, 21, 37, 255]),
            ]),
    ];

    private static int Main()
    {
        List<(string Name, Action Body)> tests =
        [
            ("host profile resolution", HostProfileResolution),
            ("host mismatch rejection", HostMismatchRejection),
            ("host requirement overlap", HostRequirementOverlap),
        ];
        if (OperatingSystem.IsWindows())
        {
            tests.Insert(0, ("plugin identity", PluginIdentity));
        }
        tests.AddRange(ExpectedProfiles.Select(expected =>
            ($"{expected.Name} canonical tables",
                (Action)(() => AssertCanonicalProfile(expected)))));
        tests.AddRange(
        [
            ("controller masks", ControllerMaskBehavior),
            ("all compiled profiles enable normal control", AllProfilesEnableNormalControl),
            ("system diagnostics coalesce changes and clear on reset", SystemDiagnosticsCoalesce),
            ("system diagnostics omit failed control", SystemDiagnosticsFailure),
            ("system trace gates extra reads by build and validated profile", SystemTraceGatesReads),
            ("system trace reads live values without mutating control", SystemTraceReadOnly),
            ("system trace repeats unchanged samples and clears its interval", SystemTraceScheduling),
            ("system trace failure leaves control and recovery available", SystemTraceFailure),
            ("system trace rejects malformed samples without retrying", SystemTraceMalformedSample),
            ("system trace labels changing state and unstable tach", SystemTraceChangingSample),
            ("system trace formats stable raw tach and ownership", SystemTraceFormatting),
            ("system trace does not add writable addresses", SystemTraceWriteAllowlist),
            ("system debug sweeps supported profiles and restores firmware", SystemDebugSweep),
            ("system debug single target uses the profile maximum", SystemDebugSingleTarget),
            ("system debug final window excludes transients and unstable tach", SystemDebugFinalWindow),
            ("system debug short window includes zero RPM and averages the median", SystemDebugShortFinalWindow),
            ("system debug final window reports unknown for unstable tach", SystemDebugInvalidFinalWindow),
            ("system debug final window uses read timestamps and actual duration", SystemDebugFinalWindowTiming),
            ("system debug cancellation restores firmware", SystemDebugCancellation),
            ("system debug sample failure aborts later stages", SystemDebugReadFailure),
            ("system debug live target drift aborts later stages", SystemDebugTargetDrift),
            ("system debug exclusivity checks prevent and stop control", SystemDebugExclusivity),
            ("system debug rejects invalid options before hardware access", SystemDebugInvalidOptions),
            ("system debug exposes failed cleanup", SystemDebugCleanupFailure),
            ("system debug distinguishes log failure from cleanup failure", SystemDebugLoggingFailure),
            ("immutable policy before recovery", CorruptPolicyBlocksRecovery),
            ("HPBSD system maximum", HpbsdSystemMaximum),
            ("F7BSD set and reset transactions", F7bsdSetResetTransactions),
            ("F7BSC normal writes", F7bscNormalWrites),
            ("HPBSD system engage and release", HpbsdSystemEngageAndRelease),
            ("atomic precondition drift", AtomicPreconditionDrift),
            ("failed disposal closes public control", FailedDisposeClosesControl),
            ("failed startup recovery cleanup", FailedStartupRecoveryCleanup),
        ]);

        int failures = 0;
        foreach ((string name, Action body) in tests)
        {
            try
            {
                body();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception}");
            }
        }

        Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void PluginIdentity()
    {
        MinisforumUMSeriesPlugin plugin = new();
        Equal("Minisforum UM Series", plugin.Name);
        plugin.Close();
    }

    private static void HostProfileResolution()
    {
        foreach (SupportedHost supported in HostsWithBiosMetadata())
        {
            Equal(supported.ProfileId, F7ProfileCatalog.Resolve(supported.Host).Id);
        }
    }

    private static void HostMismatchRejection()
    {
        foreach (SupportedHost supported in HostsWithBiosMetadata())
        {
            HostIdentitySnapshot exact = supported.Host;
            List<HostIdentitySnapshot> mismatches =
            [
                exact with { Product = "Unknown product" },
                exact with { Board = "Unknown board" },
                exact with { BoardVersion = "Unknown revision" },
                exact with { EcMajor = 255 },
                exact with { EcMinor = 255 },
            ];
            if (exact.Board == "F7BSI")
            {
                mismatches.AddRange(
                [
                    exact with { Product = "EliteMini series" },
                    exact with { SystemVersion = "1.1" },
                    exact with { Sku = "MGF7BSW" },
                    exact with { Family = "Venus" },
                    exact with { BoardVersion = "1.1" },
                    exact with { EcMajor = 1 },
                    exact with { EcMinor = 4 },
                    exact with { EcMinor = 6 },
                ]);
            }

            foreach (HostIdentitySnapshot mismatch in mismatches)
            {
                bool transportCreated = false;
                PawnIoF7bsdBackend backend = new(
                    () => mismatch,
                    profile =>
                    {
                        transportCreated = true;
                        return new FakeTransport(profile, selector: 0xb1);
                    });
                Throws<PlatformNotSupportedException>(() => backend.Initialize());
                False(transportCreated);
                backend.Dispose();
            }
        }
    }

    private static void HostRequirementOverlap()
    {
        static HostRequirement Requirement(
            string product = "EliteMini Series",
            string? systemVersion = "1.0",
            string? family = "EliteMini",
            string board = "F7BSI",
            string boardVersion = "1.0",
            EcVersion[]? ecVersions = null,
            string? sku = "MGF7BSI") => new(
                product,
                systemVersion,
                family,
                board,
                boardVersion,
                ecVersions ?? [new(0, 5)],
                sku);

        HostRequirement exact = Requirement();
        HostRequirement[] overlaps =
        [
            Requirement(),
            Requirement(systemVersion: null),
            Requirement(family: null),
            Requirement(sku: null),
            Requirement(ecVersions: [new(0, 4), new(0, 5)]),
        ];
        foreach (HostRequirement overlap in overlaps)
        {
            True(exact.Overlaps(overlap));
            True(overlap.Overlaps(exact));
            foreach (string biosVersion in new[] { "1.08", "1.09", "future BIOS", "" })
            {
                HostIdentitySnapshot host = F7bsiHost("F7BSI", "MGF7BSI", biosVersion);
                True(exact.Matches(host));
                True(overlap.Matches(host));
            }
        }

        HostRequirement[] disjoint =
        [
            Requirement(product: "Venus series"),
            Requirement(systemVersion: "1.1"),
            Requirement(family: "Venus"),
            Requirement(board: "F7BSW"),
            Requirement(boardVersion: "1.1"),
            Requirement(sku: "MGF7BSW"),
            Requirement(ecVersions: [new(1, 5), new(0, 6)]),
        ];
        foreach (HostRequirement other in disjoint)
        {
            False(exact.Overlaps(other));
            False(other.Overlaps(exact));
        }
    }

    private static void AssertCanonicalProfile(ExpectedProfile expected)
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get(expected.Id);
        Equal(expected.SystemMaximum, profile.SystemMaximumCode);
        Equal(expected.Cpus.Length, profile.CpuProfiles.Count);
        SequenceEqual(expected.SystemPolicy, profile.ExpectedSystemTable.ToArray());
        F7bsdProfile.ValidateSystemPolicy(profile, expected.SystemPolicy);

        byte[] corruptPolicy = (byte[])expected.SystemPolicy.Clone();
        corruptPolicy[0] ^= 1;
        Throws<PlatformNotSupportedException>(
            () => F7bsdProfile.ValidateSystemPolicy(profile, corruptPolicy));

        foreach (ExpectedCpu cpu in expected.Cpus)
        {
            CpuProfileDefinition actual = profile.CpuProfiles.Single(
                item => item.Selector == cpu.Selector);
            SequenceEqual(cpu.Bands, actual.Bands.ToArray());
            SequenceEqual(cpu.Baseline, actual.Baseline.ToArray());

            byte[] snapshot = BuildCpuSnapshot(cpu);
            CpuStartupClassification classification =
                F7bsdProfile.ClassifyCpuStartupSnapshot(profile, snapshot);
            Equal(CpuStartupState.Firmware, classification.State);
            Equal(cpu.Selector, classification.Selector);
            SequenceEqual(cpu.Baseline, classification.Baseline);
            F7bsdProfile.ValidateFirmwareCpuSnapshot(
                profile,
                snapshot,
                cpu.Baseline);

            byte[] corruptSnapshot = (byte[])snapshot.Clone();
            corruptSnapshot[2] ^= 1;
            Throws<PlatformNotSupportedException>(() =>
                F7bsdProfile.ClassifyCpuStartupSnapshot(profile, corruptSnapshot));
        }
    }

    private static void ControllerMaskBehavior()
    {
        F7PlatformProfile exact = F7ProfileCatalog.Get("f7bsd");
        True(exact.MatchesController(
            [0x55, 0x71, 0x02],
            [0x55, 0x71, 0x02, 0x43, 0x14, 0x7f]));
        False(exact.MatchesController(
            [0x55, 0x71, 0x03],
            [0x55, 0x71, 0x03, 0x43, 0x14, 0x7f]));

        F7PlatformProfile revisionMasked = F7ProfileCatalog.Get("f7bsc");
        True(revisionMasked.MatchesController(
            [0x55, 0x71, 0x03],
            [0x55, 0x71, 0x03, 0x43, 0x14, 0x7f]));
        False(revisionMasked.MatchesController(
            [0x55, 0x71, 0x02],
            [0x55, 0x71, 0x03, 0x43, 0x14, 0x7f]));
        False(revisionMasked.MatchesController(
            [0x54, 0x71, 0x03],
            [0x54, 0x71, 0x03, 0x43, 0x14, 0x7f]));
        False(revisionMasked.MatchesController(
            [0x55, 0x71, 0x03],
            [0x55, 0x71, 0x03, 0x42, 0x14, 0x7f]));
        False(revisionMasked.MatchesController(
            [0x55, 0x71, 0x03],
            [0x55, 0x71, 0x03, 0x43, 0x15, 0x7f]));
        False(revisionMasked.MatchesController(
            [0x55, 0x71, 0x03],
            [0x55, 0x71, 0x03, 0x43, 0x14, 0x7e]));
    }

    private static void AllProfilesEnableNormalControl()
    {
        foreach (SupportedHost supported in HostsWithBiosMetadata())
        {
            F7PlatformProfile profile = F7ProfileCatalog.Get(supported.ProfileId);
            FakeTransport transport = new(profile, selector: 0xb1);
            PawnIoF7bsdBackend backend = CreateBackend(supported.Host, transport);

            F7bsdStartupRecovery recovery = backend.Initialize();
            Equal(supported.Host, recovery.Host);
            True(backend.IsInitialized);
            Equal<byte?>(null, backend.ActiveSystemCode);
            Equal(0, transport.WriteAttempts);

            Equal((byte)19, backend.SetCpu(19));
            Equal((byte)19, backend.SetSystem(19));
            Equal<byte?>(19, backend.ActiveSystemCode);
            backend.ResetCpu();
            backend.ResetSystem();
            Equal<byte?>(null, backend.ActiveSystemCode);
            True(transport.WriteAttempts > 0);

            backend.Dispose();
            False(backend.IsInitialized);
            Equal<byte?>(null, backend.ActiveSystemCode);
            True(transport.Disposed);
        }
    }

    private static void SystemDiagnosticsCoalesce()
    {
        ManualTimeProvider clock = new();
        SystemControlDiagnostics diagnostics = new(clock);
        Equal<string?>(null, diagnostics.Observe(20, 51, 3500));
        clock.Advance(1);
        Equal<string?>(null, diagnostics.Observe(25, 51, 3500));
        clock.Advance(1);
        Equal<string?>(null, diagnostics.Observe(30, 51, 3500));
        clock.Advance(2);
        string message = diagnostics.Observe(30, 51, 3500) ??
            throw new InvalidOperationException("Expected stable target diagnostic.");
        True(message.Contains("30/51", StringComparison.Ordinal));
        True(message.Contains("3500 RPM", StringComparison.Ordinal));
        True(message.Contains("accepted and read back", StringComparison.Ordinal));

        Equal<string?>(null, diagnostics.Observe(25, 51, 3480));
        clock.Advance(2);
        Equal<string?>(null, diagnostics.Observe(25, 51, 3480));
        clock.Advance(3);
        True(diagnostics.Observe(25, 51, 3480) is not null);
        clock.Advance(60);
        Equal<string?>(null, diagnostics.Observe(25, 51, 3500));

        Equal<string?>(null, diagnostics.Observe(null, 51, 3500));
        Equal<string?>(null, diagnostics.Observe(25, 51, 3500));
        clock.Advance(2);
        True(diagnostics.Observe(25, 51, 3500) is not null);

        diagnostics.Clear();
        Equal<string?>(null, diagnostics.Observe(25, 51, 3500));
        clock.Advance(2);
        True(diagnostics.Observe(25, 51, 3500) is not null);
    }

    private static void SystemDiagnosticsFailure()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsd");
        FakeTransport transport = new(profile, selector: 0xb1);
        PawnIoF7bsdBackend backend = CreateBackend(F7bsdHost(), transport);
        backend.Initialize();
        Equal((byte)20, backend.SetSystem(20));
        Equal<byte?>(20, backend.ActiveSystemCode);
        transport.FailNextWrite = true;
        Throws<IOException>(() => backend.SetSystem(25));
        Equal<byte?>(null, backend.ActiveSystemCode);

        Equal((byte)20, backend.SetSystem(20));
        transport.FailNextWrite = true;
        Throws<AggregateException>(backend.Dispose);
        Equal<byte?>(null, backend.ActiveSystemCode);
        backend.Dispose();
        True(transport.Disposed);
    }

    private static void SystemTraceGatesReads()
    {
        foreach (SupportedHost supported in SupportedHosts)
        {
            F7PlatformProfile profile = F7ProfileCatalog.Get(supported.ProfileId);
            FakeTransport transport = new(profile, selector: 0xb1);
            PawnIoF7bsdBackend backend = CreateBackend(supported.Host, transport);
            SystemFanTrace trace = new(new ManualTimeProvider());
            False(backend.SupportsSystemFanTrace);
            Equal<string?>(null, trace.Poll(backend));
            Throws<InvalidOperationException>(() => backend.ReadSystemFanTrace());
            Throws<InvalidOperationException>(() => backend.ReadSystemFanConfiguration());
            Equal(0, transport.ReadBatches.Count);

            backend.Initialize();
            bool expectedSupport = SystemFanTrace.Enabled;
            Equal(expectedSupport, backend.SupportsSystemFanTrace);
            int beforePoll = transport.ReadBatches.Count;
            Equal(expectedSupport, trace.Poll(backend) is not null);
            Equal(beforePoll + (expectedSupport ? 1 : 0), transport.ReadBatches.Count);
            if (expectedSupport)
            {
                SequenceEqual(SystemFanTrace.Addresses, transport.ReadBatches[^1]);
                EcRegisterValue[] configuration = backend.ReadSystemFanConfiguration();
                SequenceEqual(SystemFanTrace.ConfigurationAddresses,
                    configuration.Select(value => value.Address));
                SequenceEqual(SystemFanTrace.ConfigurationAddresses, transport.ReadBatches[^1]);
                Equal(beforePoll + 2, transport.ReadBatches.Count);
            }
            else
            {
                Throws<InvalidOperationException>(() => backend.ReadSystemFanTrace());
                Throws<InvalidOperationException>(() => backend.ReadSystemFanConfiguration());
                Equal(beforePoll, transport.ReadBatches.Count);
            }

            backend.Dispose();
            False(backend.SupportsSystemFanTrace);
            int afterDispose = transport.ReadBatches.Count;
            trace.Clear();
            Equal<string?>(null, trace.Poll(backend));
            Throws<InvalidOperationException>(() => backend.ReadSystemFanTrace());
            Throws<InvalidOperationException>(() => backend.ReadSystemFanConfiguration());
            Equal(afterDispose, transport.ReadBatches.Count);
        }

        F7PlatformProfile f7bsi = F7ProfileCatalog.Get("f7bsi");
        FakeTransport failedTransport = new(f7bsi, selector: 0xb1);
        failedTransport.CorruptSystemPolicy();
        PawnIoF7bsdBackend failedBackend = CreateBackend(
            F7bsiHost("F7BSI", "MGF7BSI", "1.09"),
            failedTransport);
        Throws<PlatformNotSupportedException>(() => failedBackend.Initialize());
        False(failedBackend.SupportsSystemFanTrace);
        int afterFailure = failedTransport.ReadBatches.Count;
        Equal<string?>(null, new SystemFanTrace().Poll(failedBackend));
        Throws<InvalidOperationException>(() => failedBackend.ReadSystemFanTrace());
        Throws<InvalidOperationException>(() => failedBackend.ReadSystemFanConfiguration());
        Equal(afterFailure, failedTransport.ReadBatches.Count);
        failedBackend.Dispose();
    }

    private static void SystemTraceReadOnly()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsi");
        FakeTransport transport = new(profile, selector: 0xb1);
        PawnIoF7bsdBackend backend = CreateBackend(
            F7bsiHost("F7BSI", "MGF7BSI", "1.09"),
            transport);
        backend.Initialize();
        backend.SetSystem(20);
        transport.SetByte(F7bsdProfile.SystemTargetAddress, 15);
        transport.SetByte(F7bsdProfile.SystemTemperatureOverrideAddress, 0);
        transport.SetByte(F7bsdProfile.SystemEffectiveTemperatureAddress, 45);
        transport.SetByte(0x1804, 42);
        int beforeReads = transport.ReadBatches.Count;
        int beforeWrites = transport.WriteAttempts;
        KeyValuePair<ushort, byte>[] beforeMemory = transport.SnapshotMemory();

        SystemFanTraceSample sample = backend.ReadSystemFanTrace();
        Equal<byte?>(20, sample.RequestedCode);
        Equal((byte)51, sample.MaximumCode);
        Equal((byte)15, sample.Values[Array.IndexOf(
            SystemFanTrace.Addresses, F7bsdProfile.SystemTargetAddress)]);
        Equal((byte)0, sample.Values[Array.IndexOf(
            SystemFanTrace.Addresses, F7bsdProfile.SystemTemperatureOverrideAddress)]);
        Equal((byte)42, sample.Values[Array.IndexOf(SystemFanTrace.Addresses, (ushort)0x1804)]);
        string message = SystemFanTrace.Format(sample, TimeSpan.Zero);
        True(message.Contains("requested=20/51 (cached)", StringComparison.Ordinal));
        True(message.Contains("live-target=15->15", StringComparison.Ordinal));
        True(message.Contains("ownership=firmware", StringComparison.Ordinal));
        True(message.Contains("pwm2=42->42", StringComparison.Ordinal));
        Equal(beforeReads + 1, transport.ReadBatches.Count);
        SequenceEqual(SystemFanTrace.Addresses, transport.ReadBatches[^1]);
        EcRegisterValue[] configuration = backend.ReadSystemFanConfiguration();
        SequenceEqual(SystemFanTrace.ConfigurationAddresses,
            configuration.Select(value => value.Address));
        SequenceEqual(transport.ValuesAt(SystemFanTrace.ConfigurationAddresses),
            configuration.Select(value => value.Value));
        Equal(beforeReads + 2, transport.ReadBatches.Count);
        Equal(beforeWrites, transport.WriteAttempts);
        Equal<byte?>(20, backend.ActiveSystemCode);
        True(backend.IsInitialized);
        SequenceEqual(beforeMemory, transport.SnapshotMemory());

        transport.MakeSystemRecoverable(20);
        backend.ResetSystem();
        backend.Dispose();
        True(transport.Disposed);
    }

    private static void SystemTraceScheduling()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsi");
        FakeTransport transport = new(profile, selector: 0xb1);
        PawnIoF7bsdBackend backend = CreateBackend(
            F7bsiHost("F7BSI", "MGF7BSI", "1.09"),
            transport);
        backend.Initialize();
        ManualTimeProvider clock = new();
        SystemFanTrace trace = new(clock);
        int beforeReads = transport.ReadBatches.Count;
        KeyValuePair<ushort, byte>[] beforeMemory = transport.SnapshotMemory();
        True(trace.Poll(backend) is not null);
        Equal<byte?>(null, backend.ActiveSystemCode);
        Equal(beforeReads + 1, transport.ReadBatches.Count);
        Equal<string?>(null, trace.Poll(backend));
        clock.Advance(4);
        Equal<string?>(null, trace.Poll(backend));
        Equal(beforeReads + 1, transport.ReadBatches.Count);
        clock.Advance(1);
        True(trace.Poll(backend) is not null);
        Equal(beforeReads + 2, transport.ReadBatches.Count);
        clock.Advance(5);
        True(trace.Poll(backend) is not null);
        Equal(beforeReads + 3, transport.ReadBatches.Count);

        trace.Clear();
        True(trace.Poll(backend) is not null);
        Equal(beforeReads + 4, transport.ReadBatches.Count);
        Equal(0, transport.WriteAttempts);
        SequenceEqual(beforeMemory, transport.SnapshotMemory());
        backend.Dispose();
    }

    private static void SystemTraceFailure()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsi");
        FakeTransport transport = new(profile, selector: 0xb1);
        PawnIoF7bsdBackend backend = CreateBackend(
            F7bsiHost("F7BSI", "MGF7BSI", "1.09"),
            transport);
        backend.Initialize();
        backend.SetSystem(20);
        ManualTimeProvider clock = new();
        SystemFanTrace trace = new(clock);
        int beforeReads = transport.ReadBatches.Count;
        int beforeWrites = transport.WriteAttempts;
        KeyValuePair<ushort, byte>[] beforeMemory = transport.SnapshotMemory();
        transport.FailNextRead = true;
        string failure = trace.Poll(backend) ??
            throw new InvalidOperationException("Expected one trace failure message.");
        True(failure.Contains("sampling stopped", StringComparison.Ordinal));
        True(failure.Contains("Expected fake read failure", StringComparison.Ordinal));
        False(failure.Contains("rpm=", StringComparison.Ordinal));
        Equal(beforeReads + 1, transport.ReadBatches.Count);
        Equal(beforeWrites, transport.WriteAttempts);
        Equal<byte?>(20, backend.ActiveSystemCode);
        True(backend.IsInitialized);
        SequenceEqual(beforeMemory, transport.SnapshotMemory());

        Equal<string?>(null, trace.Poll(backend));
        clock.Advance(60);
        Equal<string?>(null, trace.Poll(backend));
        Equal(beforeReads + 1, transport.ReadBatches.Count);
        trace.Clear();
        True(trace.Poll(backend) is not null);
        Equal(beforeReads + 2, transport.ReadBatches.Count);
        Equal(beforeWrites, transport.WriteAttempts);
        Equal((byte)25, backend.SetSystem(25));
        backend.ResetSystem();
        Equal<byte?>(null, backend.ActiveSystemCode);
        backend.SetSystem(20);
        transport.FailNextWrite = true;
        Throws<AggregateException>(backend.Dispose);
        False(backend.SupportsSystemFanTrace);
        trace.Clear();
        int afterFailedDispose = transport.ReadBatches.Count;
        Equal<string?>(null, trace.Poll(backend));
        Throws<InvalidOperationException>(() => backend.ReadSystemFanTrace());
        Equal(afterFailedDispose, transport.ReadBatches.Count);
        backend.Dispose();
        True(transport.Disposed);
    }

    private static void SystemTraceMalformedSample()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsi");
        FakeTransport transport = new(profile, selector: 0xb1);
        PawnIoF7bsdBackend backend = CreateBackend(
            F7bsiHost("F7BSI", "MGF7BSI", "1.09"),
            transport);
        backend.Initialize();
        int beforeReads = transport.ReadBatches.Count;
        transport.TransformNextRead = static (_, values) => values[..^1];
        SystemFanTrace trace = new(new ManualTimeProvider());
        string failure = trace.Poll(backend) ??
            throw new InvalidOperationException("Expected malformed sample message.");
        True(failure.Contains("sample length", StringComparison.Ordinal));
        False(failure.Contains("rpm=", StringComparison.Ordinal));
        Equal(beforeReads + 1, transport.ReadBatches.Count);
        Equal<string?>(null, trace.Poll(backend));
        Equal(beforeReads + 1, transport.ReadBatches.Count);
        Equal(0, transport.WriteAttempts);
        True(backend.IsInitialized);
        backend.Dispose();
    }

    private static void SystemTraceChangingSample()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsi");
        FakeTransport transport = new(profile, selector: 0xb1);
        PawnIoF7bsdBackend backend = CreateBackend(
            F7bsiHost("F7BSI", "MGF7BSI", "1.09"),
            transport);
        backend.Initialize();
        backend.SetSystem(20);
        transport.SetByte(0x1804, 42);
        transport.SetByte(0x1820, 0x68);
        transport.SetByte(0x1821, 0x02);
        int beforeReads = transport.ReadBatches.Count;
        int beforeWrites = transport.WriteAttempts;
        KeyValuePair<ushort, byte>[] beforeMemory = transport.SnapshotMemory();
        transport.TransformNextRead = static (_, values) =>
        {
            values[6] = 0x69;
            values[18] = 15;
            values[19] = 41;
            return values;
        };
        ManualTimeProvider clock = new();
        SystemFanTrace trace = new(clock);
        string message = trace.Poll(backend) ??
            throw new InvalidOperationException("Expected changing sample message.");
        True(message.Contains("requested=20/51 (cached)", StringComparison.Ordinal));
        True(message.Contains("live-target=20->15", StringComparison.Ordinal));
        True(message.Contains("ownership=changed-during-read", StringComparison.Ordinal));
        True(message.Contains("pwm2=42->41", StringComparison.Ordinal));
        True(message.Contains("rpm=unstable;", StringComparison.Ordinal));
        True(message.Contains("tach=68/02/69", StringComparison.Ordinal));
        True(message.Contains("sequential-read=true", StringComparison.Ordinal));
        Equal(beforeReads + 1, transport.ReadBatches.Count);
        Equal(beforeWrites, transport.WriteAttempts);
        Equal<byte?>(20, backend.ActiveSystemCode);
        SequenceEqual(beforeMemory, transport.SnapshotMemory());

        clock.Advance(5);
        string next = trace.Poll(backend) ??
            throw new InvalidOperationException("Unstable tach must not disable future samples.");
        True(next.Contains("rpm=3500;", StringComparison.Ordinal));
        True(next.Contains("ownership=fixed-target", StringComparison.Ordinal));
        Equal(beforeReads + 2, transport.ReadBatches.Count);
        Equal(beforeWrites, transport.WriteAttempts);
        backend.Dispose();
    }

    private static void SystemTraceFormatting()
    {
        byte[] values = new byte[SystemFanTrace.Addresses.Length];
        values[0] = values[1] = values[16] = values[17] = 0xff;
        values[2] = values[18] = 20;
        values[3] = values[19] = 42;
        values[4] = values[6] = 0x68;
        values[5] = 0x02;
        string message = SystemFanTrace.Format(new(null, 51, values), TimeSpan.FromSeconds(5));
        True(message.Contains("t=5.0s", StringComparison.Ordinal));
        True(message.Contains("requested=none/51 (cached)", StringComparison.Ordinal));
        True(message.Contains("ownership=fixed-target", StringComparison.Ordinal));
        True(message.Contains("rpm=3500; tach=68/02/68", StringComparison.Ordinal));

        values[1] = values[17] = 0x20;
        message = SystemFanTrace.Format(new(null, 51, values), TimeSpan.Zero);
        True(message.Contains("ownership=other", StringComparison.Ordinal));
        Throws<IOException>(() => SystemFanTrace.Format(
            new(null, 51, values[..^1]), TimeSpan.Zero));
    }

    private static void SystemTraceWriteAllowlist()
    {
        ushort[] originalReadAddresses =
        [
            .. F7bsdProfile.ControllerProfileAddresses,
            .. F7bsdProfile.TelemetryAddresses,
            .. F7bsdProfile.CpuSnapshotAddresses,
            .. F7bsdProfile.SystemPolicyAddresses,
            .. F7bsdProfile.SystemStateAddresses,
        ];
        ushort[] addedAddresses = SystemFanTrace.Addresses
            .Union(SystemFanTrace.ConfigurationAddresses)
            .Except(originalReadAddresses)
            .ToArray();
        True(addedAddresses.Length > 0);
        foreach (ExpectedProfile expected in ExpectedProfiles)
        {
            F7PlatformProfile profile = F7ProfileCatalog.Get(expected.Id);
            byte[] baseline = profile.CpuProfiles.First().Baseline.ToArray();
            foreach (ushort address in addedAddresses)
            {
                Throws<InvalidOperationException>(() => F7bsdProfile.AssertWritesAllowed(
                    profile, [new EcWrite(address, 0)]));
                Throws<InvalidOperationException>(() => F7bsdProfile.AssertWritesAllowed(
                    profile, [new EcWrite(address, 0xff)]));
                Throws<InvalidOperationException>(() => F7bsdProfile.AssertCpuWritesAllowed(
                    profile, [new EcWrite(address, 0)], baseline));
                if (SystemFanTrace.Enabled)
                {
                    F7bsdProfile.AssertReadsAllowed([address]);
                }
                else
                {
                    Throws<InvalidOperationException>(() => F7bsdProfile.AssertReadsAllowed([address]));
                }
            }
        }
    }

    private static void SystemDebugSweep()
    {
        Equal(60, new SystemFanDebugOptions().HoldSeconds);
        foreach (SupportedHost supported in SupportedHosts)
        {
            DebugFixture fixture = new(supported);
            SystemFanDebugSession session = fixture.Session();
            if (!SystemFanTrace.Enabled)
            {
                Throws<InvalidOperationException>(() => session.Run(new(5), CancellationToken.None));
                Equal(0, fixture.Transport.ReadBatches.Count);
                Equal(0, fixture.Transport.WriteAttempts);
                False(fixture.Backend.IsInitialized);
                continue;
            }

            session.Run(new(5), CancellationToken.None);
            byte maximum = fixture.Transport.Profile.SystemMaximumCode;
            SequenceEqual(
                maximum == 40
                    ? new byte[] { 40, 32, 24, 20, 16, 12, 8, 4, 40 }
                    : [51, 41, 31, 26, 20, 15, 10, 5, 51],
                fixture.TargetWrites());
            SequenceEqual(
                new[] { 100, 80, 60, 50, 40, 30, 20, 10 }.Select(percent =>
                    $"target-{percent}"),
                fixture.Messages.Where(message => message.StartsWith(
                    "Summary stage=target-", StringComparison.Ordinal))
                    .Select(message => message["Summary stage=".Length..message.IndexOf(':')]));
            SequenceEqual(
                new byte[] { 0xff, 0 },
                fixture.Transport.WriteBatches.SelectMany(batch => batch)
                    .Where(write => write.Address == F7bsdProfile.SystemTemperatureOverrideAddress)
                    .Select(write => write.Value));
            Equal(11, fixture.Transport.WriteAttempts);
            Equal(47, fixture.Transport.ReadBatches.Count(batch =>
                batch.SequenceEqual(SystemFanTrace.Addresses)));
            Equal(10, fixture.Messages.Count(message =>
                message.StartsWith("Final window stage=", StringComparison.Ordinal)));
            fixture.AssertRestored();
        }
    }

    private static void SystemDebugSingleTarget()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        foreach (int percent in new[] { 0, 70, 100 })
        {
            DebugFixture fixture = new(SupportedHosts.First(host => host.ProfileId == "hpbsd"));
            fixture.Session().Run(new(5, percent), CancellationToken.None);
            byte code = F7bsdProfile.ToCode(percent, 40);
            SequenceEqual(code == 40 ? new byte[] { code } : [code, 40], fixture.TargetWrites());
            fixture.AssertRestored();
        }
    }

    private static void SystemDebugFinalWindow()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture fixture = new();
        int targetSamples = 0;
        fixture.Transport.AfterRead = (addresses, values) =>
        {
            if (!addresses.SequenceEqual(SystemFanTrace.Addresses) ||
                !fixture.Backend.ActiveSystemCode.HasValue)
            {
                return;
            }

            int index = targetSamples++;
            // The stage samples at 0, 2, ... 20 seconds. The sample at
            // exactly 10 seconds belongs to the final window.
            if (index < 5)
            {
                SetDebugObservation(values, counter: 50, stable: true, 200, 201);
            }
            else
            {
                ushort counter = index switch
                {
                    5 => 2_000,
                    6 => 500,
                    _ => 1_000,
                };
                SetDebugObservation(values, counter, stable: index < 8,
                    (byte)(index * 2), (byte)(index * 2 + 1));
            }
        };

        fixture.Session().Run(new(20, 100), CancellationToken.None);
        Equal(11, targetSamples);
        string summary = fixture.Messages.Single(message => message.StartsWith(
            "Summary stage=target-100:", StringComparison.Ordinal));
        True(summary.Contains("duration=20.0s", StringComparison.Ordinal));
        True(summary.Contains("samples=11;", StringComparison.Ordinal));
        True(summary.Contains("pwm2-range=10..201;", StringComparison.Ordinal));
        True(summary.Contains("rpm-range=1078..43125.", StringComparison.Ordinal));
        Equal(
            "Final window stage=target-100: window=10.0s; samples=6; " +
            "valid-rpm-samples=3; rpm-median=2156; rpm-range=1078..4312; pwm2-range=10..21.",
            fixture.Messages.Single(message => message.StartsWith(
                "Final window stage=target-100:", StringComparison.Ordinal)));
        fixture.AssertRestored();
    }

    private static void SystemDebugShortFinalWindow()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture fixture = new();
        int targetSamples = 0;
        fixture.Transport.AfterRead = (addresses, values) =>
        {
            if (addresses.SequenceEqual(SystemFanTrace.Addresses) &&
                fixture.Backend.ActiveSystemCode.HasValue)
            {
                int index = targetSamples++;
                SetDebugObservation(values, index == 0 ? (ushort)0 : (ushort)4_000,
                    stable: index < 2, (byte)(10 + index), (byte)(9 + index));
            }
        };

        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
            fixture.Session().Run(new(5, 100), CancellationToken.None);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        Equal(4, targetSamples);
        True(fixture.Messages.Single(message => message.StartsWith(
            "Summary stage=target-100:", StringComparison.Ordinal))
            .Contains("duration=5.0s", StringComparison.Ordinal));
        Equal(
            "Final window stage=target-100: window=5.0s; samples=4; " +
            "valid-rpm-samples=2; rpm-median=269.5; rpm-range=0..539; pwm2-range=9..13.",
            fixture.Messages.Single(message => message.StartsWith(
                "Final window stage=target-100:", StringComparison.Ordinal)));
        fixture.AssertRestored();
    }

    private static void SystemDebugInvalidFinalWindow()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture fixture = new();
        fixture.Transport.AfterRead = (addresses, values) =>
        {
            if (addresses.SequenceEqual(SystemFanTrace.Addresses))
            {
                SetDebugObservation(values, counter: 500, stable: false, 42, 45);
            }
        };

        fixture.Session().Run(new(5, 100), CancellationToken.None);
        Equal(
            "Final window stage=target-100: window=5.0s; samples=4; " +
            "valid-rpm-samples=0; rpm-median=unknown; rpm-range=unknown..unknown; pwm2-range=42..45.",
            fixture.Messages.Single(message => message.StartsWith(
                "Final window stage=target-100:", StringComparison.Ordinal)));
        fixture.AssertRestored();
    }

    private static void SystemDebugFinalWindowTiming()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        foreach (bool stallDuringRead in new[] { true, false })
        {
            DebugFixture fixture = new();
            int targetSamples = 0;
            fixture.Transport.AfterRead = (addresses, values) =>
            {
                if (addresses.SequenceEqual(SystemFanTrace.Addresses) &&
                    fixture.Backend.ActiveSystemCode.HasValue)
                {
                    targetSamples++;
                    SetDebugObservation(values, counter: 500, stable: true, 42, 45);
                    if (stallDuringRead)
                    {
                        fixture.Clock.Advance(30);
                    }
                }
            };
            fixture.Session(afterLog: message =>
            {
                if (!stallDuringRead && message.StartsWith(
                    "stage=target-100;", StringComparison.Ordinal))
                {
                    fixture.Clock.Advance(30);
                }
            }).Run(new(20, 100), CancellationToken.None);

            Equal(1, targetSamples);
            True(fixture.Messages.Single(message => message.StartsWith(
                "Summary stage=target-100:", StringComparison.Ordinal))
                .Contains("duration=30.0s", StringComparison.Ordinal));
            Equal(
                "Final window stage=target-100: window=10.0s; " +
                (stallDuringRead
                    ? "samples=1; valid-rpm-samples=1; rpm-median=4312; rpm-range=4312..4312; pwm2-range=42..45."
                    : "samples=0; valid-rpm-samples=0; rpm-median=unknown; rpm-range=unknown..unknown; pwm2-range=unknown..unknown."),
                fixture.Messages.Single(message => message.StartsWith(
                    "Final window stage=target-100:", StringComparison.Ordinal)));
            fixture.AssertRestored();
        }
    }

    private static void SetDebugObservation(
        byte[] values, ushort counter, bool stable, byte dutyBefore, byte dutyAfter)
    {
        values[3] = dutyBefore;
        values[19] = dutyAfter;
        values[4] = (byte)counter;
        values[5] = (byte)(counter >> 8);
        values[6] = stable ? values[4] : (byte)(values[4] ^ 1);
    }

    private static void SystemDebugCancellation()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture fixture = new();
        using CancellationTokenSource cancellation = new();
        SystemFanDebugSession session = fixture.Session(beforeWait: _ =>
        {
            if (fixture.Backend.ActiveSystemCode.HasValue)
            {
                cancellation.Cancel();
            }
        });
        Throws<OperationCanceledException>(() => session.Run(new(5), cancellation.Token));
        SequenceEqual(new byte[] { 51 }, fixture.TargetWrites());
        fixture.AssertRestored();
    }

    private static void SystemDebugReadFailure()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture fixture = new();
        bool injected = false;
        SystemFanDebugSession session = fixture.Session(beforeWait: _ =>
        {
            if (!injected && fixture.Backend.ActiveSystemCode.HasValue)
            {
                injected = true;
                fixture.Transport.FailNextRead = true;
            }
        });
        IOException failure = Throws<IOException>(() => session.Run(new(5), CancellationToken.None));
        True(failure.Message.Contains("Expected fake read failure", StringComparison.Ordinal));
        True(injected);
        SequenceEqual(new byte[] { 51 }, fixture.TargetWrites());
        fixture.AssertRestored();
    }

    private static void SystemDebugTargetDrift()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture fixture = new();
        bool injected = false;
        SystemFanDebugSession session = fixture.Session(beforeWait: _ =>
        {
            if (!injected && fixture.Backend.ActiveSystemCode.HasValue)
            {
                injected = true;
                fixture.Transport.SetByte(F7bsdProfile.SystemTargetAddress, 19);
            }
        });
        Throws<IOException>(() => session.Run(new(5), CancellationToken.None));
        True(injected);
        SequenceEqual(new byte[] { 51, 51 }, fixture.TargetWrites());
        fixture.AssertRestored();
    }

    private static void SystemDebugExclusivity()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture beforeStart = new();
        SystemFanDebugSession rejected = beforeStart.Session(
            assertExclusive: () => throw new IOException("Expected competing utility."));
        Throws<IOException>(() => rejected.Run(new(5), CancellationToken.None));
        Equal(0, beforeStart.Transport.ReadBatches.Count);
        Equal(0, beforeStart.Transport.WriteAttempts);
        False(beforeStart.Backend.IsInitialized);

        DebugFixture duringControl = new();
        bool injected = false;
        SystemFanDebugSession interrupted = duringControl.Session(assertExclusive: () =>
        {
            if (duringControl.Backend.ActiveSystemCode.HasValue)
            {
                injected = true;
                throw new IOException("Expected competing utility.");
            }
        });
        Throws<IOException>(() => interrupted.Run(new(5), CancellationToken.None));
        True(injected);
        SequenceEqual(new byte[] { 51 }, duringControl.TargetWrites());
        duringControl.AssertRestored();
    }

    private static void SystemDebugInvalidOptions()
    {
        foreach ((int hold, int? target) in new (int, int?)[]
        {
            (4, null), (301, null), (5, -1), (5, 101),
        })
        {
            DebugFixture fixture = new();
            SystemFanDebugSession session = fixture.Session();
            Throws<ArgumentOutOfRangeException>(() =>
                session.Run(new(hold, target), CancellationToken.None));
            Equal(0, fixture.Transport.ReadBatches.Count);
            Equal(0, fixture.Transport.WriteAttempts);
            False(fixture.Backend.IsInitialized);
        }
    }

    private static void SystemDebugCleanupFailure()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture fixture = new();
        using CancellationTokenSource cancellation = new();
        SystemFanDebugSession session = fixture.Session(beforeWait: _ =>
        {
            if (fixture.Backend.ActiveSystemCode.HasValue)
            {
                fixture.Transport.FailNextWrite = true;
                cancellation.Cancel();
            }
        });
        AggregateException failure = Throws<AggregateException>(() =>
            session.Run(new(5), cancellation.Token));
        True(failure.Flatten().InnerExceptions.Any(exception => exception is OperationCanceledException));
        True(failure.Flatten().InnerExceptions.Any(exception => exception is IOException));
        False(fixture.Backend.IsInitialized);
        fixture.Backend.Dispose();
        fixture.AssertRestored();
    }

    private static void SystemDebugLoggingFailure()
    {
        if (!SystemFanTrace.Enabled)
        {
            return;
        }

        DebugFixture fixture = new();
        ManualTimeProvider clock = new();
        SystemFanDebugSession session = new(
            fixture.Backend,
            message =>
            {
                fixture.Messages.Add(message);
                if (message.StartsWith("Cleanup complete", StringComparison.Ordinal))
                {
                    True(fixture.Transport.Disposed);
                    throw new IOException("Expected cleanup log failure.");
                }
            },
            clock: clock,
            wait: (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                clock.Advance(delay);
            });

        IOException failure = Throws<IOException>(() =>
            session.Run(new(5, 100), CancellationToken.None));
        Equal("Expected cleanup log failure.", failure.Message);
        fixture.AssertRestored();
        Equal(3, fixture.Transport.WriteAttempts);
        SequenceEqual(new byte[] { 51 }, fixture.TargetWrites());
        False(fixture.Messages.Any(message =>
            message.Contains("RESTORATION NOT VERIFIED", StringComparison.Ordinal)));
        False(fixture.Messages.Any(message =>
            message.Contains("Cleanup retry", StringComparison.Ordinal)));
    }

    private static void CorruptPolicyBlocksRecovery()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsd");
        FakeTransport transport = new(profile, selector: 0xb1);
        transport.MakeCpuRecoverable(20);
        transport.MakeSystemRecoverable(20);
        transport.CorruptSystemPolicy();
        PawnIoF7bsdBackend backend = CreateBackend(F7bsdHost(), transport);

        Throws<PlatformNotSupportedException>(() => backend.Initialize());
        False(backend.IsInitialized);
        Equal(0, transport.WriteAttempts);
        backend.Dispose();
        True(transport.Disposed);
        Equal(0, transport.WriteAttempts);
    }

    private static void HpbsdSystemMaximum()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("hpbsd");
        Equal((byte)40, profile.SystemMaximumCode);
        Equal((byte)0, F7bsdProfile.ToCode(0f, profile.SystemMaximumCode));
        Equal((byte)20, F7bsdProfile.ToCode(50f, profile.SystemMaximumCode));
        Equal((byte)40, F7bsdProfile.ToCode(100f, profile.SystemMaximumCode));
        Equal(0f, F7bsdProfile.ToPercentage(0, profile.SystemMaximumCode));
        Equal(50f, F7bsdProfile.ToPercentage(20, profile.SystemMaximumCode));
        Equal(100f, F7bsdProfile.ToPercentage(40, profile.SystemMaximumCode));
        Throws<ArgumentOutOfRangeException>(() =>
            F7bsdProfile.ToPercentage(41, profile.SystemMaximumCode));

        F7bsdProfile.AssertWritesAllowed(
            profile,
            [new EcWrite(F7bsdProfile.SystemTargetAddress, 40)]);
        Throws<InvalidOperationException>(() => F7bsdProfile.AssertWritesAllowed(
            profile,
            [new EcWrite(F7bsdProfile.SystemTargetAddress, 41)]));
        Equal(
            SystemStartupState.Firmware,
            F7bsdProfile.ClassifySystemStartupState(profile, [45, 0, 40]));
        Equal(
            SystemStartupState.Unsupported,
            F7bsdProfile.ClassifySystemStartupState(profile, [45, 0, 41]));
        F7bsdProfile.ValidateOwnedSystemState(profile, [0xff, 0xff, 40], 40);
        Throws<IOException>(() =>
            F7bsdProfile.ValidateOwnedSystemState(profile, [0xff, 0xff, 41]));
    }

    private static void F7bsdSetResetTransactions()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsd");
        CpuProfileDefinition cpu = profile.CpuProfiles.Single(
            item => item.Selector == 0xb1);
        byte[] baseline = cpu.Baseline.ToArray();
        FakeTransport transport = new(profile, selector: cpu.Selector);
        PawnIoF7bsdBackend backend = CreateBackend(F7bsdHost(), transport);

        backend.Initialize();
        True(backend.IsInitialized);
        Equal(0, transport.WriteAttempts);

        AssertCpuSetReset(backend, transport, profile, baseline, 20);

        Equal((byte)20, backend.SetSystem(20));
        SequenceEqual(
            new[]
            {
                new EcWrite(
                    F7bsdProfile.SystemTemperatureOverrideAddress,
                    F7bsdProfile.SystemSentinel),
            },
            transport.WriteBatches[2]);
        SequenceEqual(
            new[] { new EcWrite(F7bsdProfile.SystemTargetAddress, 20) },
            transport.WriteBatches[3]);
        backend.ResetSystem();
        SequenceEqual(
            new[]
            {
                new EcWrite(
                    F7bsdProfile.SystemTargetAddress,
                    profile.SystemMaximumCode),
            },
            transport.WriteBatches[4]);
        SequenceEqual(
            new[]
            {
                new EcWrite(F7bsdProfile.SystemTemperatureOverrideAddress, 0),
            },
            transport.WriteBatches[5]);
        SequenceEqual(
            new byte[] { 45, 0, profile.SystemMaximumCode },
            transport.ValuesAt(F7bsdProfile.SystemStateAddresses));
        Equal(6, transport.WriteAttempts);

        backend.Dispose();
        True(transport.Disposed);
        Equal(6, transport.WriteAttempts);
    }

    private static void F7bscNormalWrites()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsc");
        CpuProfileDefinition cpu = profile.CpuProfiles.Single(
            item => item.Selector == 0xb1);
        byte[] baseline = cpu.Baseline.ToArray();
        FakeTransport transport = new(profile, selector: cpu.Selector);
        PawnIoF7bsdBackend backend = CreateBackend(
            F7bscHost("1.09"),
            transport);

        backend.Initialize();
        True(backend.IsInitialized);
        AssertCpuSetReset(backend, transport, profile, baseline, 18);
        Equal(2, transport.WriteAttempts);

        backend.Dispose();
        True(transport.Disposed);
        Equal(2, transport.WriteAttempts);
    }

    private static void AssertCpuSetReset(
        PawnIoF7bsdBackend backend,
        FakeTransport transport,
        F7PlatformProfile profile,
        byte[] baseline,
        byte code)
    {
        int firstBatch = transport.WriteBatches.Count;
        Equal(code, backend.SetCpu(code));
        SequenceEqual(
            F7bsdProfile.CpuTargetWrites(code, includeSlopes: true),
            transport.WriteBatches[firstBatch]);
        backend.ResetCpu();
        SequenceEqual(
            F7bsdProfile.CpuRestoreWrites(profile, baseline),
            transport.WriteBatches[firstBatch + 1]);
        SequenceEqual(
            baseline,
            transport.ValuesAt(F7bsdProfile.CpuOwnedAddresses));
    }

    private static void HpbsdSystemEngageAndRelease()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("hpbsd");
        FakeTransport transport = new(profile, selector: 0xb1);
        PawnIoF7bsdBackend backend = CreateBackend(
            HpbsdHost(ecMinor: 2),
            transport);

        backend.Initialize();
        Throws<ArgumentOutOfRangeException>(() => backend.SetSystem(41));
        Equal(0, transport.WriteAttempts);

        Equal((byte)40, backend.SetSystem(40));
        SequenceEqual(
            new[]
            {
                new EcWrite(
                    F7bsdProfile.SystemTemperatureOverrideAddress,
                    F7bsdProfile.SystemSentinel),
            },
            transport.WriteBatches[0]);
        SequenceEqual(
            new[] { new EcWrite(F7bsdProfile.SystemTargetAddress, 40) },
            transport.WriteBatches[1]);
        backend.ResetSystem();
        SequenceEqual(
            new[]
            {
                new EcWrite(F7bsdProfile.SystemTemperatureOverrideAddress, 0),
            },
            transport.WriteBatches[2]);
        False(transport.WriteBatches
            .SelectMany(batch => batch)
            .Any(write =>
                write.Address == F7bsdProfile.SystemTargetAddress &&
                write.Value > profile.SystemMaximumCode));
        SequenceEqual(
            new byte[] { 45, 0, 40 },
            transport.ValuesAt(F7bsdProfile.SystemStateAddresses));
        Equal(3, transport.WriteAttempts);

        backend.Dispose();
        True(transport.Disposed);
        Equal(3, transport.WriteAttempts);
    }

    private static void AtomicPreconditionDrift()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsd");

        FakeTransport cpuTransport = new(profile, selector: 0xb1);
        CpuProfileDefinition cpu = profile.CpuProfiles.Single(
            item => item.Selector == 0xb1);
        byte[] baseline = cpu.Baseline.ToArray();
        PawnIoF7bsdBackend cpuBackend = CreateBackend(F7bsdHost(), cpuTransport);
        cpuBackend.Initialize();
        cpuTransport.MutateBeforeNextWriteValidation = static transport =>
            transport.MakeCpuRecoverable(7);

        Throws<IOException>(() => cpuBackend.SetCpu(20));
        Equal(2, cpuTransport.WriteAttempts);
        Equal(1, cpuTransport.WriteBatches.Count);
        SequenceEqual(
            F7bsdProfile.CpuRestoreWrites(profile, baseline),
            cpuTransport.WriteBatches[0]);
        SequenceEqual(
            baseline,
            cpuTransport.ValuesAt(F7bsdProfile.CpuOwnedAddresses));
        cpuBackend.Dispose();
        True(cpuTransport.Disposed);

        FakeTransport systemTransport = new(profile, selector: 0xb1);
        PawnIoF7bsdBackend systemBackend = CreateBackend(
            F7bsdHost(),
            systemTransport);
        systemBackend.Initialize();
        systemTransport.MutateBeforeNextWriteValidation = static transport =>
            transport.CorruptSystemPolicy();

        Throws<IOException>(() => systemBackend.SetSystem(20));
        Equal(1, systemTransport.WriteAttempts);
        Equal(0, systemTransport.WriteBatches.Count);
        SequenceEqual(
            new byte[] { 45, 0, 20 },
            systemTransport.ValuesAt(F7bsdProfile.SystemStateAddresses));
        systemBackend.Dispose();
        True(systemTransport.Disposed);
        Equal(1, systemTransport.WriteAttempts);
    }

    private static void FailedStartupRecoveryCleanup()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsd");
        FakeTransport transport = new(profile, selector: 0xb1);
        CpuProfileDefinition cpu = profile.CpuProfiles.Single(
            item => item.Selector == 0xb1);
        byte[] baseline = cpu.Baseline.ToArray();
        transport.MakeCpuRecoverable(20);
        transport.MakeSystemRecoverable(20);
        transport.FailNextWrite = true;
        PawnIoF7bsdBackend backend = CreateBackend(F7bsdHost(), transport);

        Throws<IOException>(() => backend.Initialize());
        Equal(1, transport.WriteAttempts);
        Equal(0, transport.WriteBatches.Count);
        False(transport.Disposed);

        backend.Dispose();
        True(transport.Disposed);
        Equal(4, transport.WriteAttempts);
        Equal(3, transport.WriteBatches.Count);
        SequenceEqual(
            new[]
            {
                new EcWrite(
                    F7bsdProfile.SystemTargetAddress,
                    profile.SystemMaximumCode),
            },
            transport.WriteBatches[0]);
        SequenceEqual(
            new[]
            {
                new EcWrite(F7bsdProfile.SystemTemperatureOverrideAddress, 0),
            },
            transport.WriteBatches[1]);
        SequenceEqual(
            F7bsdProfile.CpuRestoreWrites(profile, baseline),
            transport.WriteBatches[2]);
        SequenceEqual(
            new byte[] { 45, 0, profile.SystemMaximumCode },
            transport.ValuesAt(F7bsdProfile.SystemStateAddresses));
        SequenceEqual(
            baseline,
            transport.ValuesAt(F7bsdProfile.CpuOwnedAddresses));
    }

    private static void FailedDisposeClosesControl()
    {
        F7PlatformProfile profile = F7ProfileCatalog.Get("f7bsd");
        CpuProfileDefinition cpu = profile.CpuProfiles.Single(
            item => item.Selector == 0xb1);
        byte[] baseline = cpu.Baseline.ToArray();
        FakeTransport transport = new(profile, selector: cpu.Selector);
        PawnIoF7bsdBackend backend = CreateBackend(F7bsdHost(), transport);

        backend.Initialize();
        Equal((byte)20, backend.SetCpu(20));
        transport.FailNextWrite = true;

        Throws<AggregateException>(backend.Dispose);
        False(backend.IsInitialized);
        False(transport.Disposed);
        Equal(2, transport.WriteAttempts);

        Throws<InvalidOperationException>(() => backend.ReadTelemetry());
        Throws<InvalidOperationException>(() => backend.SetCpu(19));
        Throws<InvalidOperationException>(() => backend.SetSystem(19));
        Throws<InvalidOperationException>(backend.ResetCpu);
        Throws<InvalidOperationException>(backend.ResetSystem);
        Equal(2, transport.WriteAttempts);

        backend.Dispose();
        False(backend.IsInitialized);
        True(transport.Disposed);
        Equal(3, transport.WriteAttempts);
        SequenceEqual(
            baseline,
            transport.ValuesAt(F7bsdProfile.CpuOwnedAddresses));
    }

    private static PawnIoF7bsdBackend CreateBackend(
        HostIdentitySnapshot host,
        FakeTransport transport) => new(
            () => host,
            selected =>
            {
                Equal(transport.Profile.Id, selected.Id);
                return transport;
            });

    private static IEnumerable<SupportedHost> HostsWithBiosMetadata() =>
        SupportedHosts.SelectMany(supported => new[]
        {
            supported.Host.BiosVersion,
            "1.10",
            "1.9",
            "future BIOS",
            string.Empty,
        }.Select(biosVersion => supported with
        {
            Host = supported.Host with { BiosVersion = biosVersion },
        }));

    private static byte[] BuildCpuSnapshot(ExpectedCpu cpu) =>
    [
        cpu.Selector,
        0,
        .. cpu.Bands,
        51, 100, 93, 0,
        .. cpu.Baseline,
    ];

    private static HostIdentitySnapshot F7bsdHost() => new(
        "Venus series",
        "1.0",
        "Default string",
        "Default string",
        "F7BSD",
        "1.1",
        "1.06",
        0,
        8);

    private static HostIdentitySnapshot F7bshHost() => new(
        "Venus series",
        "1.0",
        "Default string",
        "Default string",
        "F7BSH",
        "1.1",
        "1.06",
        0,
        8);

    private static HostIdentitySnapshot F7bscHost(string biosVersion) => new(
        "Venus series",
        "Default string",
        "Default string",
        "Default string",
        "F7BSC",
        "Default string",
        biosVersion,
        2,
        6);

    private static HostIdentitySnapshot F7bsiHost(
        string board,
        string sku,
        string biosVersion) => new(
            "EliteMini Series",
            "1.0",
            sku,
            "EliteMini",
            board,
            "1.0",
            biosVersion,
            0,
            5);

    private static HostIdentitySnapshot HpbsdHost(int ecMinor) => new(
        "EliteMini Series",
        "1.0",
        string.Empty,
        string.Empty,
        "HPBSD",
        "1.0",
        "1.06",
        0,
        ecMinor);

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void False(bool condition) => True(!condition);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}; found {actual}.");
        }
    }

    private static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual)
    {
        T[] expectedArray = expected.ToArray();
        T[] actualArray = actual.ToArray();
        if (!expectedArray.SequenceEqual(actualArray))
        {
            throw new InvalidOperationException(
                "Expected [" + string.Join(", ", expectedArray) + "]; found [" +
                string.Join(", ", actualArray) + "].");
        }
    }

    private static T Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed record ExpectedCpu(
        byte Selector,
        byte[] Bands,
        byte[] Baseline);

    private sealed record ExpectedProfile(
        string Name,
        string Id,
        byte[] SystemPolicy,
        byte SystemMaximum,
        ExpectedCpu[] Cpus);

    private sealed record SupportedHost(
        string ProfileId,
        HostIdentitySnapshot Host);

    private sealed class DebugFixture
    {
        private readonly byte[] cpuBaseline;
        private readonly byte[] policyBaseline;

        internal DebugFixture(SupportedHost? supported = null)
        {
            supported ??= SupportedHosts.First(host => host.ProfileId == "f7bsi");
            Transport = new(F7ProfileCatalog.Get(supported.ProfileId), selector: 0xb1);
            Transport.SetByte(0x0309, 35);
            Transport.SetByte(0x0305, 25);
            Transport.SetByte(0x1820, 0x68);
            Transport.SetByte(0x1821, 0x02);
            Backend = CreateBackend(supported.Host, Transport);
            cpuBaseline = Transport.ValuesAt(F7bsdProfile.CpuSnapshotAddresses);
            policyBaseline = Transport.ValuesAt(F7bsdProfile.SystemPolicyAddresses);
        }

        internal FakeTransport Transport { get; }

        internal PawnIoF7bsdBackend Backend { get; }

        internal List<string> Messages { get; } = [];

        internal ManualTimeProvider Clock { get; } = new();

        internal SystemFanDebugSession Session(
            Action? assertExclusive = null,
            Action<CancellationToken>? beforeWait = null,
            Action<string>? afterLog = null) => new(
                Backend,
                message =>
                {
                    Messages.Add(message);
                    afterLog?.Invoke(message);
                },
                assertExclusive,
                Clock,
                (delay, cancellationToken) =>
                {
                    beforeWait?.Invoke(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    Clock.Advance(delay);
                });

        internal IEnumerable<byte> TargetWrites() =>
            Transport.WriteBatches.SelectMany(batch => batch)
                .Where(write => write.Address == F7bsdProfile.SystemTargetAddress)
                .Select(write => write.Value);

        internal void AssertRestored()
        {
            False(Backend.IsInitialized);
            True(Transport.Disposed);
            SequenceEqual(cpuBaseline, Transport.ValuesAt(F7bsdProfile.CpuSnapshotAddresses));
            SequenceEqual(policyBaseline, Transport.ValuesAt(F7bsdProfile.SystemPolicyAddresses));
            SequenceEqual(
                new byte[] { 45, 0, Transport.Profile.SystemMaximumCode },
                Transport.ValuesAt(F7bsdProfile.SystemStateAddresses));
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        internal void Advance(int seconds) =>
            timestamp += TimeSpan.FromSeconds(seconds).Ticks;

        internal void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }

    private sealed class FakeTransport : IF7Transport
    {
        private readonly Dictionary<ushort, byte> memory = [];

        internal FakeTransport(F7PlatformProfile profile, byte selector)
        {
            Profile = profile;
            CpuProfileDefinition cpu = profile.CpuProfiles.Single(
                item => item.Selector == selector);
            SetBytes(
                F7bsdProfile.CpuSnapshotAddresses,
                [
                    selector,
                    0,
                    .. cpu.Bands,
                    51, 100, 93, 0,
                    .. cpu.Baseline,
                ]);
            SetBytes(
                F7bsdProfile.SystemPolicyAddresses,
                profile.ExpectedSystemTable);
            SetBytes(F7bsdProfile.SystemStateAddresses, [45, 0, 20]);
        }

        internal F7PlatformProfile Profile { get; }

        internal int WriteAttempts { get; private set; }

        internal List<EcWrite[]> WriteBatches { get; } = [];

        internal List<ushort[]> ReadBatches { get; } = [];

        internal Action<FakeTransport>? MutateBeforeNextWriteValidation { get; set; }

        internal bool FailNextWrite { get; set; }

        internal bool FailNextRead { get; set; }

        internal Func<ushort[], byte[], byte[]>? TransformNextRead { get; set; }

        internal Action<ushort[], byte[]>? AfterRead { get; set; }

        internal bool Disposed { get; private set; }

        public byte[] Read(ushort[] addresses)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            F7bsdProfile.AssertReadsAllowed(addresses);
            ReadBatches.Add((ushort[])addresses.Clone());
            if (FailNextRead)
            {
                FailNextRead = false;
                throw new IOException("Expected fake read failure.");
            }
            byte[] values = addresses.Select(ByteAt).ToArray();
            Func<ushort[], byte[], byte[]>? transform = TransformNextRead;
            TransformNextRead = null;
            AfterRead?.Invoke(addresses, values);
            return transform is null ? values : transform(addresses, values);
        }

        public void WriteVerified(
            EcExpectation[] before,
            EcWrite[] writes,
            Action? beforeWrites = null)
        {
            F7bsdProfile.AssertWritesAllowed(Profile, writes);
            Apply(before, writes, beforeWrites);
        }

        public void WriteCpuVerified(
            EcExpectation[] before,
            EcWrite[] writes,
            ReadOnlySpan<byte> baseline)
        {
            F7bsdProfile.AssertCpuWritesAllowed(Profile, writes, baseline);
            Apply(before, writes, null);
        }

        public void Dispose() => Disposed = true;

        internal void MakeCpuRecoverable(byte code)
        {
            foreach (ushort address in F7bsdProfile.CpuBaseAddresses)
            {
                memory[address] = code;
            }
            foreach (ushort address in F7bsdProfile.CpuSlopeAddresses)
            {
                memory[address] = 0;
            }
        }

        internal void MakeSystemRecoverable(byte code) => SetBytes(
            F7bsdProfile.SystemStateAddresses,
            [F7bsdProfile.SystemSentinel, F7bsdProfile.SystemSentinel, code]);

        internal void CorruptSystemPolicy()
        {
            ushort address = F7bsdProfile.SystemPolicyAddresses[0];
            memory[address] ^= 1;
        }

        internal byte[] ValuesAt(ushort[] addresses) =>
            addresses.Select(ByteAt).ToArray();

        internal void SetByte(ushort address, byte value) => memory[address] = value;

        internal KeyValuePair<ushort, byte>[] SnapshotMemory() =>
            memory.OrderBy(item => item.Key).ToArray();

        private void Apply(
            EcExpectation[] before,
            EcWrite[] writes,
            Action? beforeWrites)
        {
            WriteAttempts++;
            ObjectDisposedException.ThrowIf(Disposed, this);
            F7bsdProfile.AssertReadsAllowed(before.Select(item => item.Address));
            Action<FakeTransport>? mutate = MutateBeforeNextWriteValidation;
            MutateBeforeNextWriteValidation = null;
            mutate?.Invoke(this);
            foreach (EcExpectation expectation in before)
            {
                byte actual = ByteAt(expectation.Address);
                if (actual != expectation.Value)
                {
                    throw new IOException(
                        $"Fake precondition failed at 0x{expectation.Address:X4}.");
                }
            }

            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new IOException($"Expected fake write failure {WriteAttempts}.");
            }
            beforeWrites?.Invoke();
            EcWrite[] copy = (EcWrite[])writes.Clone();
            WriteBatches.Add(copy);
            foreach (EcWrite write in copy)
            {
                memory[write.Address] = write.Value;
                if (write.Address == F7bsdProfile.SystemTemperatureOverrideAddress)
                {
                    memory[F7bsdProfile.SystemEffectiveTemperatureAddress] =
                        write.Value == F7bsdProfile.SystemSentinel
                            ? F7bsdProfile.SystemSentinel
                            : (byte)45;
                }
                if (ByteAt(write.Address) != write.Value)
                {
                    throw new IOException(
                        $"Fake verification failed at 0x{write.Address:X4}.");
                }
            }
        }

        private byte ByteAt(ushort address) =>
            memory.TryGetValue(address, out byte value) ? value : (byte)0;

        private void SetBytes(ushort[] addresses, ReadOnlySpan<byte> values)
        {
            if (addresses.Length != values.Length)
            {
                throw new ArgumentException("Fake address/value lengths differ.");
            }
            for (int index = 0; index < addresses.Length; index++)
            {
                memory[addresses[index]] = values[index];
            }
        }
    }
}
