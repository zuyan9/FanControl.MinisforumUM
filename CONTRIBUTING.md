# Contributing

Thanks for helping improve FanControl.MinisforumUM. This plugin talks to an
embedded controller, so changes should stay fail-closed and be tested without
hardware before any live validation.

## Prerequisites

- A Windows x64 development environment.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- The .NET 10 build of Fan Control V273. The build needs its
  `FanControl.Plugins.dll` reference.

The project looks for Fan Control in `C:\Program Files (x86)\FanControl` by
default. If it is installed or extracted elsewhere, pass
`-p:FanControlDir=C:\path\to\FanControl`. `FanControlDir` must name the directory
that directly contains `FanControl.Plugins.dll`.

Run the commands below in the repository root.

## Build and test

Build the x64 plugin in Release configuration:

```powershell
dotnet build .\FanControl.MinisforumUMSeries.csproj -c Release
```

For a non-default Fan Control location, or to set the version explicitly:

```powershell
dotnet build .\FanControl.MinisforumUMSeries.csproj -c Release `
  "-p:FanControlDir=C:\path\to\FanControl" `
  -p:Version=0.2.0
```

Run the hardware-free profile, guard, control, and recovery harness:

```powershell
dotnet run `
  --project .\tests\FanControl.MinisforumUMSeries.Tests.csproj `
  -c Release `
  "-p:FanControlDir=C:\path\to\FanControl"
```

Omit the `FanControlDir` property when Fan Control is in the default location.
Nullable analysis and warnings-as-errors are enabled in both projects.

## Hardware safety

Always run the hardware-free harness before a live test. Live EC testing is not
a routine validation step and must use an exact supported board, BIOS, EC, and
controller profile. See [Supported systems](README.md#supported-systems) for
current validation status. Validation of one host does not validate other
models or firmware.

For every live test:

- Stop every other EC-writing or fan-control utility first. Never run two EC
  writers concurrently.
- Preserve all exact host, firmware, controller-signature, and table-fingerprint
  gates. Keep writes bounded to the profile's validated values.
- Verify that disabling controls and closing Fan Control normally restores the
  selected BIOS CPU table and returns the system fan to firmware control.
- Keep the CPU critical-temperature row untouched. Remember that the system fan
  has no firmware temperature fallback while its manual handoff is active.
- Record the machine model, BIOS version, EC version, selected profile, commands,
  observed behavior, and verified restoration in the pull request.
- If cleanup or controller state is uncertain, stop testing and fully power off
  the machine before accessing the controller again.

Do not commit firmware, raw captures, machine-state dumps, privileged experiment
output, or built binaries from live testing.

## Pull requests

Keep each pull request focused and include:

- the hardware and safety impact, including whether EC reads or writes change;
- a linked issue when applicable;
- the exact build and test commands run, with their results;
- hardware, BIOS, EC, profile, and restoration details for any live validation;
- tests for changed behavior and updates to affected documentation or ABI
  definitions; and
- screenshots only when a visible UI change needs them.

Use a concise imperative commit subject and make sure the Release build is free
of warnings before requesting review.

## CI and releases

The **Build and release** GitHub Actions workflow runs for pushes and pull
requests targeting `master`. Non-release runs use version
`0.0.0-ci.<run-number>`. The workflow:

1. installs the .NET 10 SDK;
2. downloads the Fan Control V273 .NET 10 archive and verifies its pinned SHA-256
   checksum before using `FanControl.Plugins.dll`;
3. runs the hardware-free test harness in Release configuration;
4. verifies the plugin's assembly, file, and product versions;
5. creates `FanControl.MinisforumUMSeries.zip` containing exactly one root-level
   `FanControl.MinisforumUMSeries.dll`; and
6. uploads that ZIP as a workflow artifact.

To publish a release, manually run **Build and release** from `master` and enter
an unused `X.Y.Z` version without a leading `v`. The workflow rejects other
branches and invalid versions, performs the same build and verification, checks
that `vX.Y.Z` is unused, and then creates the GitHub release with the plugin ZIP
and generated release notes.
