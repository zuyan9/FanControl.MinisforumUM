# Technical reference

This document records the hardware admission rules, firmware interactions, and
recovery behavior of FanControl.MinisforumUM. For installation and everyday
use, see the [README](../README.md).

The plugin targets Windows x64, .NET 10, Fan Control V275, and PawnIO API 2.0.
Its assembly is `FanControl.MinisforumUMSeries.dll`, and Fan Control displays it
as **Minisforum UM Series**.

## Exposed sensors and controls

Every admitted profile exposes the same six endpoints:

| ID | Kind |
|---|---|
| `cpu-rpm` | CPU fan speed |
| `system-rpm` | System fan speed |
| `cpu-temperature` | Raw EC CPU temperature |
| `system-temperature` | Raw EC system temperature |
| `cpu-control` | CPU fan target |
| `system-control` | System fan target |

## Profiles and exact host gates

Compatibility is authorized by an exact host and firmware tuple, a
profile-specific controller signature, and exact policy-table fingerprints.
Vendor package model mappings alone never authorize EC access.

| Profile | Exact host/firmware gate | Model mappings | System code | Access |
|---|---|---|---|---|
| `f7bsd` | `Venus series`, `F7BSD` rev `1.1`, BIOS `1.06`, EC `0.8` | UM780 XTX; UM790 XTX | `0..51` | Telemetry and controls |
| `f7bsh-f7bsd` | `Venus series`, `F7BSH` rev `1.1`, BIOS `1.06`, EC `0.8` | UM690 Pro | `0..51` | Telemetry and controls |
| `f7bsc` | `Venus series`, `F7BSC` rev `Default string`, BIOS `1.07` or `1.09`, EC `2.6` | UM760 Pro; UM780 Pro; UM790 Pro | `0..51` | Telemetry and controls |
| `f7bsi` | `EliteMini Series`; `F7BSI` rev `1.0`, system rev `1.0`, family `EliteMini`, SKU `MGF7BSI`, BIOS `1.08`, EC `0.5`; or `F7BSW` rev `1.0`, BIOS `1.01`, EC `0.5` | F7BSI: UM760 Slim, UM870 Slim; F7BSW: UM760 Plus, UM870 Plus, UM880 Plus | `0..51` | Telemetry and controls |
| `hpbsd` | `EliteMini Series`, `HPBSD` rev `1.0`, BIOS `1.06`, EC `0.1` or `0.2` | UM880 Pro; UM890 Pro | `0..40` | Telemetry and controls |

All other machines are unsupported. A listed profile exposes telemetry and
controls normally only after every profile check passes; there are no
experimental environment variables or diagnostic tokens that bypass the
checks. Reads still change Super-I/O and EC address latches, so the plugin
rejects a mismatch as soon as it can be detected.

## Evidence and validation limits

UM780 XTX and UM880 Plus have been live write-tested with the firmware listed
above.
The UM790 XTX is included because Minisforum publishes one identical full
BIOS/EC image for both XTX models, but it still needs live validation. The
remaining models rely on offline firmware analysis and public DMI evidence.

Validation of one machine does not cover other models or firmware revisions.
Additional profile evidence and limits:

- No live controller identity has been recorded for F7BSH or F7BSW.
- The HPBSD package conflicts between EC `0.1` and the advertised EC `0.2`, so
  both values are compiled as explicit alternatives.
- The shared HPBSD package can emit an `F7BSX` board identity based on a GPIO
  strap. There is no exact live `F7BSX` host tuple or marketing-model mapping,
  so that branch is deliberately excluded.

Consequently, a vendor-package model name does not guarantee that every
physical variant will load the plugin. Supervise initial use: confirm
temperatures, tachometers, physical fan mapping, cleanup, and recovery before
depending on the plugin for cooling.

## Fail-closed admission and write guards

In addition to the exact host tuple, every load requires:

- matching physical PNP and live-controller signatures, including agreement
  on the silicon revision byte;
- an exact byte-for-byte match for a compiled BIOS CPU selector, temperature
  bands, canonical bases and slopes, and critical row `(51,100,93,0)`; and
- the exact compiled system policy table and a canonical or exactly
  recoverable ownership state.

The `f7bsd` and `f7bsh-f7bsd` profiles pin PNP identity `55 71 02` and
controller profile `55 71 02 43 14 7f`. The `f7bsc`, `f7bsi`/F7BSW, and
`hpbsd` profiles require the same product, I2EC-mode, clock, and counter bytes.
They accept a silicon revision only when the physical PNP and live XRAM
identities agree. These signatures were derived from the common firmware
lineage.

The plugin checks CPU and system fingerprints again as atomic write
preconditions. It refuses to write if a controller, table, or ownership state
does not match an explicitly compiled or exactly recoverable state.

## Target scaling and firmware interaction

Fan Control percentages map linearly to each profile's EC target-code range.
CPU targets and all non-HPBSD system targets use `0..51`, nominally
`0..5100 RPM`. HPBSD system targets use `0..40`; `100%` therefore writes code
`40`, not `51`.

F7BSI and F7BSW still use `0..51` even though their stored system table tops
out at `40`: the active firmware selector independently hardcodes `51` for its
hottest band. HPBSD loads its target from the stored row instead and therefore
remains capped at `40`.

The plugin adds no minimum target, fan curve, thermal promotion, or rate limit.
Fan Control owns that policy.

### CPU control

CPU control writes the same target into all seven normal temperature rows. It
does not change the independent critical row, so firmware continues to request
full speed at 94 C and above.

The plugin recognizes the BIOS-selected Default, Balance, or Performance CPU
table and restores that exact table when raw control ends.

### System control

System control uses firmware's `0xff` fixed-target handoff. While that handoff
is active, firmware has no automatic system-temperature fallback.

On Reset or a clean Close, the plugin seeds the profile maximum, clears the
handoff, and verifies that firmware control resumed.

### Writes deliberately excluded

The plugin never writes:

- fan PWM or DCR outputs;
- the CPU temperature override;
- CPU temperature bands or the CPU critical row;
- system temperature thresholds; or
- firmware.

## Cleanup and recovery

Disabling a control, refreshing the plugin, or exiting Fan Control normally
restores the selected BIOS CPU table and returns the system fan to firmware.

Force-terminating Fan Control, suspending or crashing Windows, or a complete
machine freeze can prevent cleanup. On the next load, the plugin repairs an
exact leftover raw state or interrupted restoration sequence before exposing
controls. Recovery is a one-time handoff, not a background fan policy.

Never run another EC or fan-control utility at the same time. The CPU
controller has no ownership marker, so automatic recovery depends on that
exclusivity. The old `FanControl.MinisforumUM780XTX.dll` and current
`FanControl.MinisforumUMSeries.dll` must likewise never be installed together,
because both plugins could access the same EC.

If a table or override state is not an exact state this plugin can produce, it
refuses to write. Fully power off the machine before accessing the controller
again.
