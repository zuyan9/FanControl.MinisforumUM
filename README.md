# FanControl.MinisforumUM

A [Fan Control](https://github.com/Rem0o/FanControl.Releases) plugin for
selected Minisforum UM-series computers. It provides CPU and system fan speeds,
raw EC temperatures, and independent CPU and system fan controls.

[Download the latest release](https://github.com/zuyan9/FanControl.MinisforumUM/releases/latest/download/FanControl.MinisforumUMSeries.zip)

> [!WARNING]
> Only some models have been live write-tested. Support for other listed
> models is based on firmware analysis and public hardware data.

## Supported systems

| Models | Required firmware | Validation |
|---|---|---|
| UM780 XTX | BIOS `1.06`, EC `0.8` | Live write-tested |
| UM790 XTX | BIOS `1.06`, EC `0.8` | Shared vendor image; not live-tested |
| UM690 Pro | BIOS `1.06`, EC `0.8` | Firmware-derived |
| UM760 Pro, UM780 Pro, UM790 Pro | BIOS `1.07` or `1.09`, EC `2.6` | Firmware-derived |
| UM760 Slim, UM870 Slim | BIOS `1.08`, EC `0.5` | Firmware-derived |
| UM760 Plus, UM870 Plus | BIOS `1.01`, EC `0.5` | Firmware-derived |
| UM880 Plus | BIOS `1.01`, EC `0.5` | Live write-tested |
| UM880 Pro, UM890 Pro | BIOS `1.06`, EC `0.1` or `0.2` | Firmware-derived |

Model name alone is not sufficient. The plugin first checks machine identity,
then verifies the controller signature, firmware tables, and current state
before exposing controls. It refuses to load on a mismatch. All other systems
are unsupported.

## Install

1. Install the .NET 10 build of Fan Control V273 with PawnIO enabled.
2. Download and extract `FanControl.MinisforumUMSeries.zip` from the link above.
3. In Fan Control, open **Settings > Plugins > Install plugin...** and select
   `FanControl.MinisforumUMSeries.dll`.
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
