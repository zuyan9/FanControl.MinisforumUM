# System-fan diagnostics

1. Close Fan Control (and other fan utilities).
2. Extract the diagnostic ZIP into your Fan Control folder and run
   **MinisforumFanDiagnostics.exe**. Accept the administrator prompt and let it finish.
3. Share the generated **MinisforumFanDiagnostics-*.log** and say whether the fan noise changed.

The test takes a few minutes: firmware baseline, targets of **100%, 70%, 40%**,
then firmware restoration. **Ctrl+C** stops early and runs cleanup; keep the window
open until cleanup finishes. The tool uses all board/EC profiles supported by the
plugin. Unrecognized hardware is reported before EC access.

## Optional tests

Run from a terminal in the Fan Control folder:

```powershell
# Hold one system-fan target for two minutes, then restore firmware control.
.\MinisforumFanDiagnostics.exe --target 40 --hold-seconds 120

# Show all options without accessing hardware.
.\MinisforumFanDiagnostics.exe --help
```

The percentage selects a target within the model's supported range, not a measured 
PWM duty. `--fancontrol-dir` selects a portable installation; `--output` selects a
new log file. The tool uses the .NET 10 runtime required by Fan Control;PawnIO must
already be installed through Fan Control.

## What is collected

- Board, BIOS/EC version, controller identity, selected profile, and any startup recovery.
- Live target/ownership, PWM duty, raw tach bytes, RPM and raw temperatures every two seconds.
- Raw PWM clock, enable, polarity, pin/tach selection and fan tables before and after the test.
- Stage summaries, errors, cancellation, and the restoration result.

Each target is written once through the existing guarded backend, then observed.
Other fan control or EC utilities must be closed too.

Samples are sequential. `pwm2` is the raw duty register, not a measured wire signal.
Unstable tach reads are labelled. `ram0680`, `ram06A1`, and `ram0891` remain raw
address-labelled observations. Stage ranges do not prove settled speed or identify
a hardware fault by themselves.

Only the existing supported target/handoff writes are used. Direct duty, pin-mode,
clock and arbitrary register writes are not exposed: the EC also changes duty,
and those writes need a different ownership/restoration protocol. Startup can
repair an exact interrupted CPU or system control state, as the plugin already does.

Cleanup failures are reported and cause a failed exit. If restoration is not
verified, fully power off before another attempt. Forced process termination or
power loss cannot run normal cleanup. No target machine was available for live testing.

## Build

The standalone project links the same profile, transport, and backend sources as
the plugin. It does not replace the installed plugin.

```powershell
dotnet publish .\tools\SystemFanDebug\MinisforumFanDiagnostics.csproj -c Release `
  -r win-x64 --self-contained false -p:PublishSingleFile=true `
  -o .\artifacts\diagnostics

dotnet run --project .\tests\FanControl.MinisforumUMSeries.Tests.csproj -c Release `
  "-p:FanControlDir=C:\path\to\FanControl" -p:SystemFanDiagnostics=true
```

Also run the harness without `SystemFanDiagnostics` to check normal-build isolation.
Normal plugin builds have no additional diagnostic reads. An optional plugin
diagnostic build still supports five-second passive tracing via
`-p:SystemFanDiagnostics=true`; the automatic active test runs only in the standalone tool.
