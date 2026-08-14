namespace FanControl.MinisforumUM780XTX;

internal readonly record struct EcVersion(int Major, int Minor);

internal sealed record HostIdentitySnapshot(
    string Product,
    string SystemVersion,
    string Sku,
    string Family,
    string Board,
    string BoardVersion,
    string BiosVersion,
    int EcMajor,
    int EcMinor)
{
    internal EcVersion EcVersion => new(EcMajor, EcMinor);

    public override string ToString() =>
        $"{Product}/{Board} revision {BoardVersion}, SKU {Sku}, BIOS " +
        $"{BiosVersion}, EC {EcMajor}.{EcMinor}";
}

internal sealed class HostRequirement
{
    private readonly string[] biosVersions;
    private readonly EcVersion[] ecVersions;

    internal HostRequirement(
        string product,
        string? systemVersion,
        string? family,
        string board,
        string boardVersion,
        string[] biosVersions,
        EcVersion[] ecVersions,
        string? sku = null)
    {
        Product = product;
        SystemVersion = systemVersion;
        Family = family;
        Board = board;
        BoardVersion = boardVersion;
        this.biosVersions = (string[])biosVersions.Clone();
        this.ecVersions = (EcVersion[])ecVersions.Clone();
        Sku = sku;

        if (string.IsNullOrWhiteSpace(Product) ||
            (SystemVersion is not null && string.IsNullOrWhiteSpace(SystemVersion)) ||
            (Family is not null && string.IsNullOrWhiteSpace(Family)) ||
            string.IsNullOrWhiteSpace(Board) ||
            string.IsNullOrWhiteSpace(BoardVersion) ||
            this.biosVersions.Length == 0 ||
            this.biosVersions.Any(string.IsNullOrWhiteSpace) ||
            this.ecVersions.Length == 0 ||
            (Sku is not null && string.IsNullOrWhiteSpace(Sku)))
        {
            throw new ArgumentException("Host requirements must be exact and complete.");
        }
    }

    internal string Product { get; }

    internal string? SystemVersion { get; }

    internal string? Family { get; }

    internal string Board { get; }

    internal string BoardVersion { get; }

    internal string? Sku { get; }

    internal bool Matches(HostIdentitySnapshot actual) =>
        string.Equals(actual.Product, Product, StringComparison.Ordinal) &&
        (SystemVersion is null || string.Equals(
            actual.SystemVersion,
            SystemVersion,
            StringComparison.Ordinal)) &&
        (Family is null || string.Equals(
            actual.Family,
            Family,
            StringComparison.Ordinal)) &&
        string.Equals(actual.Board, Board, StringComparison.Ordinal) &&
        string.Equals(actual.BoardVersion, BoardVersion, StringComparison.Ordinal) &&
        biosVersions.Contains(actual.BiosVersion, StringComparer.Ordinal) &&
        ecVersions.Contains(actual.EcVersion) &&
        (Sku is null || string.Equals(actual.Sku, Sku, StringComparison.Ordinal));

    internal bool Overlaps(HostRequirement other) =>
        string.Equals(Product, other.Product, StringComparison.Ordinal) &&
        Compatible(SystemVersion, other.SystemVersion) &&
        Compatible(Family, other.Family) &&
        string.Equals(Board, other.Board, StringComparison.Ordinal) &&
        string.Equals(BoardVersion, other.BoardVersion, StringComparison.Ordinal) &&
        Compatible(Sku, other.Sku) &&
        biosVersions.Intersect(other.biosVersions, StringComparer.Ordinal).Any() &&
        ecVersions.Intersect(other.ecVersions).Any();

    private static bool Compatible(string? first, string? second) =>
        first is null || second is null ||
        string.Equals(first, second, StringComparison.Ordinal);
}

internal sealed class ByteSignature
{
    private readonly byte[] expected;
    private readonly byte[] mask;

    internal ByteSignature(byte[] expected, byte[] mask)
    {
        if (expected.Length == 0 || expected.Length != mask.Length)
        {
            throw new ArgumentException("A byte signature requires equal nonempty arrays.");
        }
        this.expected = (byte[])expected.Clone();
        this.mask = (byte[])mask.Clone();
    }

    internal int Length => expected.Length;

    internal bool HasMaskedBytes => mask.ContainsAnyExcept((byte)0);

    internal bool Matches(ReadOnlySpan<byte> actual)
    {
        if (actual.Length != expected.Length)
        {
            return false;
        }
        for (int index = 0; index < actual.Length; index++)
        {
            if ((actual[index] & mask[index]) != (expected[index] & mask[index]))
            {
                return false;
            }
        }
        return true;
    }
}

internal sealed class CpuProfileDefinition
{
    private readonly byte[] bands;
    private readonly byte[] baseline;

    internal CpuProfileDefinition(byte selector, byte[] bands, byte[] baseline)
    {
        Selector = selector;
        this.bands = (byte[])bands.Clone();
        this.baseline = (byte[])baseline.Clone();
    }

    internal byte Selector { get; }

    internal ReadOnlySpan<byte> Bands => bands;

    internal ReadOnlySpan<byte> Baseline => baseline;
}

internal sealed class F7PlatformProfile
{
    private readonly HostRequirement[] hosts;
    private readonly byte[] expectedSystemTable;

    internal F7PlatformProfile(
        string id,
        string displayName,
        HostRequirement[] hosts,
        ByteSignature pnpIdentity,
        ByteSignature controllerIdentity,
        CpuProfileDefinition[] cpuProfiles,
        byte[] systemPolicy,
        byte systemMaximumCode)
    {
        Id = id;
        DisplayName = displayName;
        this.hosts = (HostRequirement[])hosts.Clone();
        PnpIdentity = pnpIdentity;
        ControllerIdentity = controllerIdentity;
        CpuProfiles = Array.AsReadOnly((CpuProfileDefinition[])cpuProfiles.Clone());
        expectedSystemTable = (byte[])systemPolicy.Clone();
        SystemMaximumCode = systemMaximumCode;
        Validate();
    }

    internal string Id { get; }

    internal string DisplayName { get; }

    internal ByteSignature PnpIdentity { get; }

    internal ByteSignature ControllerIdentity { get; }

    internal IReadOnlyList<CpuProfileDefinition> CpuProfiles { get; }

    internal ReadOnlySpan<byte> ExpectedSystemTable => expectedSystemTable;

    internal byte SystemMaximumCode { get; }

    internal bool MatchesHost(HostIdentitySnapshot host) =>
        hosts.Any(requirement => requirement.Matches(host));

    internal bool OverlapsHosts(F7PlatformProfile other) =>
        hosts.Any(first => other.hosts.Any(first.Overlaps));

    internal bool MatchesPnp(ReadOnlySpan<byte> pnp) =>
        PnpIdentity.Matches(pnp);

    internal bool MatchesController(
        ReadOnlySpan<byte> pnp,
        ReadOnlySpan<byte> controller) =>
        MatchesPnp(pnp) &&
        ControllerIdentity.Matches(controller) &&
        pnp.SequenceEqual(controller[..pnp.Length]);

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) ||
            string.IsNullOrWhiteSpace(DisplayName) ||
            hosts.Length == 0 ||
            CpuProfiles.Count == 0 ||
            expectedSystemTable.Length != F7bsdProfile.SystemPolicyAddresses.Length ||
            SystemMaximumCode is 0 or > F7bsdProfile.MaximumCode ||
            CpuProfiles.Select(item => item.Selector).Distinct().Count() !=
                CpuProfiles.Count)
        {
            throw new ArgumentException($"Platform profile {Id} is incomplete.");
        }

        foreach (CpuProfileDefinition cpu in CpuProfiles)
        {
            if (cpu.Bands.Length != F7bsdProfile.CpuBandAddresses.Length ||
                cpu.Baseline.Length != F7bsdProfile.CpuOwnedAddresses.Length ||
                cpu.Selector is not (0x00 or 0xb1 or 0xb2) ||
                cpu.Baseline[..F7bsdProfile.CpuBaseAddresses.Length]
                    .ContainsAnyExceptInRange((byte)0, F7bsdProfile.MaximumCode) ||
                !HasContiguousBands(cpu.Bands, finalUpper: 93))
            {
                throw new ArgumentException($"CPU profile {Id}/0x{cpu.Selector:X2} is invalid.");
            }
        }

        ReadOnlySpan<byte> systemRows = expectedSystemTable.AsSpan(0, 9);
        if (!HasContiguousRows(systemRows, finalUpper: 100) ||
            systemRows.ToArray().Where((_, index) => index % 3 == 0)
                .Any(value => value > SystemMaximumCode) ||
            expectedSystemTable.AsSpan(9).ContainsAnyExcept((byte)0))
        {
            throw new ArgumentException($"System table {Id} is invalid.");
        }

        if (PnpIdentity.Length != 3 ||
            !PnpIdentity.HasMaskedBytes ||
            ControllerIdentity.Length !=
                F7bsdProfile.ControllerProfileAddresses.Length ||
            !ControllerIdentity.HasMaskedBytes)
        {
            throw new ArgumentException($"Controller signature {Id} is invalid.");
        }
    }

    private static bool HasContiguousBands(
        ReadOnlySpan<byte> bands,
        byte finalUpper)
    {
        byte previousUpper = 0;
        for (int row = 0; row < bands.Length / 2; row++)
        {
            byte upper = bands[row * 2];
            byte lower = bands[(row * 2) + 1];
            if (upper <= lower || lower != previousUpper)
            {
                return false;
            }
            previousUpper = upper;
        }
        return previousUpper == finalUpper;
    }

    private static bool HasContiguousRows(
        ReadOnlySpan<byte> rows,
        byte finalUpper)
    {
        byte previousUpper = 0;
        for (int row = 0; row < rows.Length / 3; row++)
        {
            byte upper = rows[(row * 3) + 1];
            byte lower = rows[(row * 3) + 2];
            if (upper <= lower || lower != previousUpper)
            {
                return false;
            }
            previousUpper = upper;
        }
        return previousUpper == finalUpper;
    }
}

internal static class F7ProfileCatalog
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

    private static readonly ByteSignature ExactRevision02Pnp = new(
        [0x55, 0x71, 0x02],
        [0xff, 0xff, 0xff]);

    private static readonly ByteSignature ExactRevision02Controller = new(
        [0x55, 0x71, 0x02, 0x43, 0x14, 0x7f],
        [0xff, 0xff, 0xff, 0xff, 0xff, 0xff]);

    // The additional projects still require the observed IT5571 product, I2EC
    // mode, PWM clock, and counter value. Only the silicon revision byte may
    // differ, and PNP/XRAM identities must agree byte-for-byte at runtime.
    private static readonly ByteSignature RevisionAgnosticPnp = new(
        [0x55, 0x71, 0x00],
        [0xff, 0xff, 0x00]);

    private static readonly ByteSignature RevisionAgnosticController = new(
        [0x55, 0x71, 0x00, 0x43, 0x14, 0x7f],
        [0xff, 0xff, 0x00, 0xff, 0xff, 0xff]);

    private static readonly CpuProfileDefinition[] F7bsdCpuProfiles =
    [
        Cpu(0x00, StandardBands,
            [0, 16, 18, 21, 28, 34, 36, 0, 10, 33, 58, 60, 16, 200]),
        Cpu(0xb1, StandardBands,
            [0, 16, 18, 21, 28, 32, 33, 0, 10, 33, 58, 60, 16, 200]),
        Cpu(0xb2, PerformanceBands,
            [0, 18, 21, 28, 36, 42, 46, 0, 15, 77, 66, 40, 50, 100]),
    ];

    private static readonly CpuProfileDefinition[] F7bscCpuProfiles =
    [
        Cpu(0x00, StandardBands,
            [0, 16, 18, 21, 28, 34, 36, 0, 10, 33, 58, 60, 16, 200]),
        Cpu(0xb1, StandardBands,
            [0, 16, 18, 21, 28, 34, 36, 0, 10, 33, 58, 60, 16, 200]),
        Cpu(0xb2, StandardBands,
            [0, 18, 21, 28, 36, 40, 46, 0, 15, 77, 66, 40, 50, 100]),
    ];

    private static readonly CpuProfileDefinition[] F7bsiCpuProfiles =
    [
        Cpu(0x00, StandardBands,
            [0, 16, 18, 22, 25, 27, 29, 0, 10, 44, 25, 20, 16, 75]),
        Cpu(0xb1, StandardBands,
            [0, 16, 18, 22, 25, 27, 29, 0, 10, 44, 25, 20, 16, 75]),
        Cpu(0xb2, PerformanceBands,
            [0, 18, 22, 29, 31, 34, 37, 0, 20, 77, 16, 25, 37, 125]),
    ];

    private static readonly CpuProfileDefinition[] HpbsdCpuProfiles =
    [
        Cpu(0xb1, StandardBands,
            [0, 18, 22, 24, 28, 32, 34, 0, 14, 16, 21, 28, 10, 154]),
        Cpu(0xb2, PerformanceBands,
            [0, 18, 21, 28, 30, 33, 36, 0, 15, 77, 16, 21, 37, 255]),
    ];

    private static readonly F7PlatformProfile[] Profiles =
    [
        new(
            "f7bsd",
            "F7BSD",
            [Host(
                "Venus series",
                null,
                null,
                "F7BSD",
                "1.1",
                ["1.06"],
                [(0, 8)])],
            ExactRevision02Pnp,
            ExactRevision02Controller,
            F7bsdCpuProfiles,
            StandardSystemPolicy,
            systemMaximumCode: 51),
        new(
            "f7bsh-f7bsd",
            "F7BSD EC on F7BSH",
            [Host(
                "Venus series",
                null,
                null,
                "F7BSH",
                "1.1",
                ["1.06"],
                [(0, 8)])],
            ExactRevision02Pnp,
            ExactRevision02Controller,
            F7bsdCpuProfiles,
            StandardSystemPolicy,
            systemMaximumCode: 51),
        new(
            "f7bsc",
            "F7BSC",
            [Host(
                "Venus series",
                null,
                null,
                "F7BSC",
                "Default string",
                ["1.07", "1.09"],
                [(2, 6)])],
            RevisionAgnosticPnp,
            RevisionAgnosticController,
            F7bscCpuProfiles,
            StandardSystemPolicy,
            systemMaximumCode: 51),
        new(
            "f7bsi",
            "F7BSI/F7BSW",
            [
                Host(
                    "EliteMini Series",
                    "1.0",
                    "EliteMini",
                    "F7BSI",
                    "1.0",
                    ["1.08"],
                    [(0, 5)],
                    "MGF7BSI"),
                Host(
                    "EliteMini Series",
                    null,
                    null,
                    "F7BSW",
                    "1.0",
                    ["1.01"],
                    [(0, 5)]),
            ],
            RevisionAgnosticPnp,
            RevisionAgnosticController,
            F7bsiCpuProfiles,
            ReducedSystemPolicy,
            systemMaximumCode: 51),
        new(
            "hpbsd",
            "HPBSD",
            [
                Host(
                    "EliteMini Series",
                    null,
                    null,
                    "HPBSD",
                    "1.0",
                    ["1.06"],
                    [(0, 1), (0, 2)]),
            ],
            RevisionAgnosticPnp,
            RevisionAgnosticController,
            HpbsdCpuProfiles,
            ReducedSystemPolicy,
            systemMaximumCode: 40),
    ];

    static F7ProfileCatalog()
    {
        if (Profiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal)
                .Count() != Profiles.Length)
        {
            throw new InvalidOperationException("Platform profile IDs must be unique.");
        }

        for (int first = 0; first < Profiles.Length; first++)
        {
            for (int second = first + 1; second < Profiles.Length; second++)
            {
                if (Profiles[first].OverlapsHosts(Profiles[second]))
                {
                    throw new InvalidOperationException(
                        $"Host gates overlap between {Profiles[first].Id} and " +
                        $"{Profiles[second].Id}.");
                }
            }
        }
    }

    internal static F7PlatformProfile Resolve(HostIdentitySnapshot host)
    {
        F7PlatformProfile[] matches = Profiles
            .Where(profile => profile.MatchesHost(host))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new PlatformNotSupportedException(
                $"No compiled Minisforum EC profile matches {host}."),
            _ => throw new PlatformNotSupportedException(
                $"Multiple compiled Minisforum EC profiles match {host}."),
        };
    }

    internal static F7PlatformProfile Get(string id) =>
        Profiles.Single(profile => string.Equals(
            profile.Id,
            id,
            StringComparison.Ordinal));

    private static CpuProfileDefinition Cpu(
        byte selector,
        byte[] bands,
        byte[] baseline) => new(selector, bands, baseline);

    private static HostRequirement Host(
        string product,
        string? systemVersion,
        string? family,
        string board,
        string boardVersion,
        string[] biosVersions,
        (int Major, int Minor)[] ecVersions,
        string? sku = null) => new(
            product,
            systemVersion,
            family,
            board,
            boardVersion,
            biosVersions,
            ecVersions.Select(item => new EcVersion(item.Major, item.Minor)).ToArray(),
            sku);
}
