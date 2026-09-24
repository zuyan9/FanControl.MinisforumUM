using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace FanControl.MinisforumUMSeries;

internal static class Program
{
    private const string ToolVersion = "0.2.2-debug.2";
    private const string ToolMutexName = @"Global\MinisforumFanDiagnostics";
    private const string MonitorLibraryName = "LibreHardwareMonitorLib.dll";

    private static int Main(string[] args)
    {
        CommandLine options;
        try
        {
            options = ParseArguments(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine("Run MinisforumFanDiagnostics.exe --help for usage.");
            return 2;
        }

        if (options.Help)
        {
            PrintHelp();
            return 0;
        }

        int exitCode;
        string? reportPath = null;
        try
        {
            reportPath = Path.GetFullPath(options.OutputPath ?? Path.Combine(
                AppContext.BaseDirectory,
                $"MinisforumFanDiagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}.log"));
            using FileStream stream = new(
                reportPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            using StreamWriter writer = new(stream, new UTF8Encoding(false))
            {
                AutoFlush = true,
            };

            void Log(string message)
            {
                foreach (string part in message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                {
                    string line = $"[{DateTime.UtcNow:O}] {part}";
                    writer.WriteLine(line);
                    Console.WriteLine(line);
                }
            }

            Log($"Minisforum Fan Diagnostics {ToolVersion}");
            Log($"Report: {reportPath}");
            Log($"OS: {RuntimeInformation.OSDescription}; " +
                $"process architecture: {RuntimeInformation.ProcessArchitecture}; " +
                $"runtime: {RuntimeInformation.FrameworkDescription}");
            Log($"Options: hold-seconds={options.HoldSeconds}; " +
                $"target={(options.TargetPercent is int target ? target.ToString(CultureInfo.InvariantCulture) : "automatic 100/80/60/50/40/30/20/10")}");
            exitCode = Run(options, Log);
            Log($"Exit code: {exitCode}. Report saved to: {reportPath}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unable to complete diagnostics: {exception}");
            if (reportPath is not null)
            {
                Console.Error.WriteLine($"Report path: {reportPath}");
            }
            exitCode = 1;
        }

        if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
        {
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
        return exitCode;
    }

    private static int Run(CommandLine options, Action<string> log)
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (!OperatingSystem.IsWindows() ||
                RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException(
                    "This tool requires 64-bit Windows on an x64 PC.");
            }

            HostIdentitySnapshot host = HostIdentity.Read();
            log($"Host: {host}");
            log($"Recognized profile: {F7ProfileCatalog.Resolve(host).Id}");
            using Mutex toolMutex = new(false, ToolMutexName);
            bool mutexHeld = false;
            try
            {
                try
                {
                    mutexHeld = toolMutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    mutexHeld = true;
                    log("A previous diagnostic process ended unexpectedly; startup recovery will be checked.");
                }
                if (!mutexHeld)
                {
                    throw new InvalidOperationException(
                        "Another MinisforumFanDiagnostics instance is running. Close it first.");
                }

                AssertFanControlClosed();
                string fanControlDirectory = ResolveFanControlDirectory(options.FanControlDirectory);
                log($"Fan Control directory: {fanControlDirectory}");
                string pawnIoLibrary = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PawnIO",
                    "PawnIOLib.dll");
                if (!File.Exists(pawnIoLibrary))
                {
                    throw new FileNotFoundException(
                        "PawnIO is missing. Install PawnIO through the Fan Control installer, " +
                        "then close Fan Control and run this tool again.",
                        pawnIoLibrary);
                }

                log("Close other fan-control utilities and keep the PC idle. Ctrl+C stops the test and restores control.");
                PawnIoF7bsdBackend backend = new(fanControlDirectory);
                SystemFanDebugSession session = new(backend, log, AssertFanControlClosed);
                session.Run(
                    new SystemFanDebugOptions(options.HoldSeconds, options.TargetPercent),
                    cancellation.Token);
                return 0;
            }
            finally
            {
                if (mutexHeld)
                {
                    toolMutex.ReleaseMutex();
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            log("Test cancelled; cleanup completed.");
            return 130;
        }
        catch (Exception exception)
        {
            log($"ERROR: {exception}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void AssertFanControlClosed()
    {
        Process[] processes = Process.GetProcessesByName("FanControl");
        try
        {
            if (processes.Length != 0)
            {
                throw new InvalidOperationException(
                    "Fan Control is running. Exit it from its tray icon before running diagnostics.");
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string ResolveFanControlDirectory(string? explicitDirectory)
    {
        if (explicitDirectory is not null)
        {
            string fullPath = Path.GetFullPath(explicitDirectory);
            if (!File.Exists(Path.Combine(fullPath, MonitorLibraryName)))
            {
                throw new FileNotFoundException(
                    $"{MonitorLibraryName} was not found in '{fullPath}'. " +
                    "Set --fancontrol-dir to the folder containing FanControl.exe.");
            }
            return fullPath;
        }

        string? executableDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        string[] candidates =
        [
            AppContext.BaseDirectory,
            executableDirectory ?? AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FanControl"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FanControl"),
        ];
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(candidate, MonitorLibraryName)))
            {
                return Path.GetFullPath(candidate);
            }
        }
        throw new FileNotFoundException(
            $"{MonitorLibraryName} was not found. Extract this tool into your Fan Control folder, " +
            "or pass --fancontrol-dir followed by that folder's path.");
    }

    private static CommandLine ParseArguments(string[] args)
    {
        int holdSeconds = 60;
        int? targetPercent = null;
        string? fanControlDirectory = null;
        string? outputPath = null;
        bool help = false;
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (!seen.Add(option))
            {
                throw new ArgumentException($"Option '{option}' was supplied more than once.");
            }
            switch (option)
            {
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--target":
                    targetPercent = ReadNumber(option, 0, 100);
                    break;
                case "--hold-seconds":
                    holdSeconds = ReadNumber(option, 5, 300);
                    break;
                case "--fancontrol-dir":
                    fanControlDirectory = ReadValue(option);
                    break;
                case "--output":
                    outputPath = ReadValue(option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }

            string ReadValue(string name)
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) ||
                    args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Option '{name}' requires a value.");
                }
                return args[index];
            }

            int ReadNumber(string name, int minimum, int maximum)
            {
                string text = ReadValue(name);
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                    value < minimum || value > maximum)
                {
                    throw new ArgumentException($"Option '{name}' requires an integer from {minimum} to {maximum}.");
                }
                return value;
            }
        }
        return new CommandLine(holdSeconds, targetPercent, fanControlDirectory, outputPath, help);
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            Minisforum Fan Diagnostics {ToolVersion}

            Close Fan Control and other fan-control utilities before starting.
            Run without options to record a baseline, test system-fan targets of
            100%, 80%, 60%, 50%, 40%, 30%, 20%, and 10%, then restore firmware control.
            Targets are percentages of the profile's supported speed range, not PWM duty.
            The default test takes about 9 minutes. Ctrl+C stops and restores control.
            Each stage includes a final-10-second RPM median and RPM/PWM ranges.

            Usage: MinisforumFanDiagnostics.exe [options]
              --target N            Test one target from 0 to 100 instead of the sweep.
              --hold-seconds N      Hold each target for 5 to 300 seconds (default: 60).
              --fancontrol-dir PATH Folder containing FanControl.exe and {MonitorLibraryName}.
              --output PATH         New log file path (default: timestamped log beside this EXE).
              --help, -h            Show this help without accessing hardware.

            Example: MinisforumFanDiagnostics.exe --target 40 --hold-seconds 120

            Requires Windows x64, Fan Control with PawnIO installed, and a supported
            Minisforum UM-series board/EC profile. Unsupported profiles are rejected.
            The log includes board, BIOS/EC versions, and diagnostic register samples.
            """);
    }

    private sealed record CommandLine(
        int HoldSeconds,
        int? TargetPercent,
        string? FanControlDirectory,
        string? OutputPath,
        bool Help);
}
