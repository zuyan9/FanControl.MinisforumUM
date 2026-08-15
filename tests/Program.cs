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
        foreach (SupportedHost supported in SupportedHosts)
        {
            Equal(supported.ProfileId, F7ProfileCatalog.Resolve(supported.Host).Id);
        }
    }

    private static void HostMismatchRejection()
    {
        HostIdentitySnapshot exact = F7bsiHost("F7BSI", "MGF7BSI", "1.08");
        HostIdentitySnapshot[] mismatches =
        [
            exact with { Product = "EliteMini series" },
            exact with { SystemVersion = "1.1" },
            exact with { Sku = "MGF7BSW" },
            exact with { Family = "Venus" },
            exact with { Board = "F7BSW" },
            exact with { BoardVersion = "1.1" },
            exact with { BiosVersion = "1.09" },
            exact with { EcMajor = 1 },
            exact with { EcMinor = 4 },
        ];

        foreach (HostIdentitySnapshot mismatch in mismatches)
        {
            Throws<PlatformNotSupportedException>(
                () => F7ProfileCatalog.Resolve(mismatch));
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
        foreach (SupportedHost supported in SupportedHosts)
        {
            F7PlatformProfile profile = F7ProfileCatalog.Get(supported.ProfileId);
            FakeTransport transport = new(profile, selector: 0xb1);
            PawnIoF7bsdBackend backend = CreateBackend(supported.Host, transport);

            backend.Initialize();
            True(backend.IsInitialized);
            Equal(0, transport.WriteAttempts);

            Equal((byte)19, backend.SetCpu(19));
            Equal((byte)19, backend.SetSystem(19));
            backend.ResetCpu();
            backend.ResetSystem();
            True(transport.WriteAttempts > 0);

            backend.Dispose();
            False(backend.IsInitialized);
            True(transport.Disposed);
        }
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

        internal Action<FakeTransport>? MutateBeforeNextWriteValidation { get; set; }

        internal bool FailNextWrite { get; set; }

        internal bool Disposed { get; private set; }

        public byte[] Read(ushort[] addresses)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            F7bsdProfile.AssertReadsAllowed(addresses);
            return addresses.Select(ByteAt).ToArray();
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
