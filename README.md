# FanControl.MinisforumUM

A hardware-specific [Fan Control](https://github.com/Rem0o/FanControl.Releases)
plugin for selected Minisforum UM-series machines with closely related F7-series
embedded controller firmware. It exposes the EC's CPU and system fan RPM and raw
temperatures, plus CPU and system fan controls, on every compiled profile listed
below.

The plugin assembly is `FanControl.MinisforumUMSeries.dll`, and it appears in
Fan Control as **Minisforum UM Series**.

## Compatibility

Compatibility is authorized by exact host and firmware gates, profile-specific
controller signatures, and exact table fingerprints. Model names below come
from vendor package mappings; they are not enough to authorize EC access by
themselves.

| Profile | Exact host/firmware gate | Vendor package models | System code | Access |
|---|---|---|---|---|
| `f7bsd` | `Venus series`, `F7BSD` rev `1.1`, BIOS `1.06`, EC `0.8` | UM780 XTX; UM790 XTX | `0..51` | Telemetry and controls |
| `f7bsh-f7bsd` | `Venus series`, `F7BSH` rev `1.1`, BIOS `1.06`, EC `0.8` | UM690 Pro | `0..51` | Telemetry and controls |
| `f7bsc` | `Venus series`, `F7BSC` rev `Default string`, BIOS `1.07` or `1.09`, EC `2.6` | UM760 Pro; UM780 Pro; UM790 Pro | `0..51` | Telemetry and controls |
| `f7bsi` | `EliteMini Series`; `F7BSI` rev `1.0`, system rev `1.0`, family `EliteMini`, SKU `MGF7BSI`, BIOS `1.08`, EC `0.5`; or `F7BSW` rev `1.0`, BIOS `1.01`, EC `0.5` | F7BSI: UM760 Slim, UM870 Slim; F7BSW: UM760 Plus, UM870 Plus | `0..51` | Telemetry and controls |
| `hpbsd` | `EliteMini Series`, `HPBSD` rev `1.0`, BIOS `1.06`, EC `0.1` or `0.2` | UM880 Pro; UM890 Pro | `0..40` | Telemetry and controls |

The UM780 XTX is the only live write-tested machine. The UM790 XTX is included
because Minisforum publishes one identical full BIOS/EC image for both XTX
models, but it still needs live validation. Every other mapping and controller
signature is based on offline firmware analysis and public DMI evidence rather
than live fan-control validation.

The non-UM780 host values mix public DMI corroboration with vendor-package evidence.
In particular, F7BSH/F7BSW live controller identities are not captured, and the
HPBSD package conflicts between EC `0.1` and advertised EC `0.2`; both HPBSD
values are consequently compiled as explicit alternatives.

That shared HPBSD package can also emit an `F7BSX` board identity based on a
GPIO strap. No exact live `F7BSX` host tuple or marketing-model mapping is
available, so that branch is deliberately excluded. A vendor-package model name
therefore does not guarantee that every physical variant will load the plugin.

Every load is fail-closed. In addition to the host tuple, the plugin requires:

- matching physical PNP and live-controller signatures, including agreement on
  the silicon revision byte;
- an exact byte-for-byte match for a compiled BIOS CPU selector, temperature
  bands, canonical bases/slopes, and critical row `(51,100,93,0)`; and
- the exact compiled system policy table and a canonical or exactly recoverable
  ownership state.

The F7BSD and F7BSH profiles pin PNP identity `55 71 02` and controller profile
`55 71 02 43 14 7f`. F7BSC, F7BSI/F7BSW, and HPBSD require the same product,
I2EC-mode, clock, and counter bytes, while accepting a silicon revision only
when physical PNP and live XRAM identities agree. Those signatures are
hypotheses derived from the common firmware lineage, not live captures. CPU and
system fingerprints are checked again as atomic write preconditions.

All other machines are unsupported. Every listed profile loads telemetry and
controls normally after all profile checks pass; no experimental environment
variables or diagnostic tokens are required. Reads still change Super-I/O and
EC address latches, so a mismatch is rejected as soon as it can be detected.

It targets Windows x64, .NET 10, Fan Control V272, and PawnIO API 2.0.

## Sensors and controls

| ID | Kind |
|---|---|
| `cpu-rpm` | CPU fan speed |
| `system-rpm` | System fan speed |
| `cpu-temperature` | Raw EC CPU temperature |
| `system-temperature` | Raw EC system temperature |
| `cpu-control` | CPU fan target |
| `system-control` | System fan target |

Fan Control percentages map linearly to the profile's EC target-code range. CPU
targets and all non-HPBSD system targets use `0..51`, nominally `0..5100 RPM`.
HPBSD system targets use `0..40`; `100%` therefore writes code `40`, not `51`.
F7BSI/F7BSW still use `0..51`: although their stored system table tops out at
`40`, the active firmware selector independently hardcodes `51` for its hottest
band. HPBSD instead loads its target from the stored row and therefore remains
capped at `40`.
There are no plugin-side minimums, curves, thermal promotions, or rate limits.
Fan Control owns that policy.

CPU control sets the same target in all seven normal temperature rows. The
plugin never changes the independent critical row, so firmware still requests
full speed at 94 C and above. It recognizes the BIOS-selected Default, Balance,
or Performance table and restores that exact table when raw control ends.

System control uses the firmware's `0xff` fixed-target handoff. While that
handoff is active, firmware has no automatic system-temperature fallback. On
Reset or a clean Close, the plugin seeds the profile maximum, clears the handoff,
and verifies that firmware control resumed.

The plugin never writes fan PWM/DCR outputs, CPU temperature override, CPU
temperature bands, the CPU critical row, system temperature thresholds, or
firmware.

## Install

Install Fan Control with PawnIO enabled, download or build
`FanControl.MinisforumUMSeries.dll`, then select it under
**Settings > Plugins > Install plugin...**. Do not run another EC or fan-control
utility at the same time.

All compiled profiles expose controls without an opt-in token once their exact
host and EC checks pass. Because only the UM780 XTX has been live write-tested,
initial use on every other listed machine should be supervised: confirm
temperatures, tachometers, physical fan mapping, cleanup, and recovery before
depending on the plugin for cooling.

### Upgrading to v0.2

The renamed plugin is intentionally not installed alongside the old assembly.
Never install both DLLs because both plugins could access the same EC.

1. Exit Fan Control normally and let the plugin restore firmware control.
2. Delete `FanControl.MinisforumUM780XTX.dll` from Fan Control's plugin folder.
3. Start Fan Control and install `FanControl.MinisforumUMSeries.dll`.
4. Recreate affected curves, mixes, and other sensor or control bindings.

## Build

Install the .NET 10 SDK and Fan Control, then run:

```powershell
dotnet build .\FanControl.MinisforumUMSeries.csproj -c Release
```

For a non-default Fan Control location or a versioned build:

```powershell
dotnet build .\FanControl.MinisforumUMSeries.csproj -c Release `
  "-p:FanControlDir=C:\path\to\FanControl" `
  -p:Version=0.2.0
```

Run the hardware-free profile, guard, and recovery tests with:

```powershell
dotnet run `
  --project .\tests\FanControl.MinisforumUMSeries.Tests.csproj `
  -c Release `
  "-p:FanControlDir=C:\path\to\FanControl"
```

Pushes and pull requests to `master` are built automatically. To publish a
release, run the **Build and release** workflow from `master` and enter an
unused `X.Y.Z` version. It verifies the binary version, uploads the plugin ZIP,
and creates the matching `vX.Y.Z` GitHub release.

## Recovery

Disabling a control, refreshing the plugin, or exiting Fan Control normally
restores the selected BIOS CPU table and returns the system fan to firmware.

Force-terminating Fan Control, suspending or crashing Windows, or a complete
machine freeze can prevent that cleanup. On its next load, the plugin repairs an
exact leftover raw state or interrupted restoration sequence before exposing
controls. Recovery is a one-time handoff, not a background fan policy.

The CPU controller has no ownership marker, so automatic recovery relies on the
requirement that no other EC-writing utility runs concurrently. If the table or
override state is not an exact state this plugin can produce, it refuses to write;
fully power off the machine before accessing the controller again.
