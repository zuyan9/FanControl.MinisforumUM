# FanControl.MinisforumUM

A [Fan Control](https://github.com/Rem0o/FanControl.Releases) plugin for
selected Minisforum UM-series computers. It provides CPU and system fan speeds,
raw EC temperatures, and independent CPU and system fan controls.

> [!WARNING]
> Only some models have been live write-tested. Support for other listed
> models is based on firmware analysis and public hardware data.

## Supported systems

| Models | Required EC | BIOS versions examined | Validation |
|---|---|---|---|
| UM780 XTX | `0.8` | `1.06` | Live write-tested |
| UM790 XTX | `0.8` | `1.06` | Shared vendor image; not live-tested |
| UM690 Pro | `0.8` | `1.06` | Firmware-derived |
| UM760 Pro, UM780 Pro, UM790 Pro | `2.6` | `1.07`, `1.09` | Firmware-derived |
| UM760 Slim, UM870 Slim | `0.5` | `1.08`, `1.09` | Firmware-derived |
| UM760 Plus, UM870 Plus | `0.5` | `1.01` | Firmware-derived |
| UM880 Plus | `0.5` | `1.01` | Live write-tested |
| UM880 Pro, UM890 Pro | `0.1` or `0.2` | `1.06` | Firmware-derived |

BIOS versions are recorded for reference and do not restrict loading. The plugin
requires an exact supported board/revision and EC version, then checks the live
controller, fan tables, and ownership state. Other BIOS releases can load when
these checks pass, but have not necessarily been validated. Other board and EC
combinations remain unsupported.

The UM760 Slim system-fan behavior reported in
[issue #14](https://github.com/zuyan9/FanControl.MinisforumUM/issues/14) still
needs hardware validation.

> [!TIP]
> Checkout [FanControl.MinisforumM1Pro](https://github.com/zuyan9/FanControl.MinisforumM1Pro) for Minisforum M1 Pro and M1 Lite.


## Install

1. Install the .NET 10 build of Fan Control V275 with PawnIO enabled.
2. Download the plugin from the [latest release](https://github.com/zuyan9/FanControl.MinisforumUM/releases/latest/download/FanControl.MinisforumUMSeries.zip).
3. In Fan Control, open **Settings > Plugins > Install plugin...** and select
   `FanControl.MinisforumUMSeries.zip`.
4. On first use, verify plausible temperature readings, then enable one control
   at a time and confirm that its RPM reading and physical fan response agree.

The plugin appears as **Minisforum UM Series**.

## Controls and safety

The plugin exposes CPU and system fan RPM, two raw EC temperatures, and CPU and
system fan controls. Fan Control owns the requested policy; the plugin does not
add minimums, curves, thermal promotion, or rate limiting.

CPU control preserves the firmware's independent critical-temperature row, which
requests full speed at 94 C and above. System control temporarily uses the
firmware's fixed-target handoff and therefore has no automatic temperature
fallback while active. Resetting or disabling a control, refreshing the plugin,
or closing Fan Control normally restores firmware control.

## Recovery

After an abnormal termination, reopen Fan Control once so the plugin can repair
an exact interrupted control or restoration state. If it refuses recovery, fully
power off the machine before trying again. Never bypass a compatibility or state
check.

## More information

- [Technical compatibility and safety design](docs/technical.md)
- [Building, testing, and contributing](CONTRIBUTING.md)
