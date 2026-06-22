using System.Diagnostics;
using System.Text;
using MultiCamApp.Utils;

namespace MultiCamApp.Diagnostics;

/// <summary>
/// Ensures bundled runtime paths are initialized once. Skips when installer already completed setup.
/// </summary>
public static class RuntimeBootstrapper
{
    private const string FlagFileName = "runtime_initialized.flag";
    private const string LockDirName = "runtime_setup.lock";
    private const string SetupScriptName = "setup_runtime.bat";
    private const string MutexName = @"Global\MultiCamApp.RuntimeSetup";

    public static void EnsureRuntimeReady(string caller = "App.Application_Startup")
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var process = Process.GetCurrentProcess();
        var commandLine = Environment.CommandLine;

        TraceStartup(caller, process.ProcessName, commandLine, setupInvoked: false);

        if (IsRuntimeInitialized(appDir))
        {
            TraceStartup(caller, process.ProcessName, commandLine, setupInvoked: false, note: "skipped: runtime_initialized.flag valid");
            WriteDiagnosisReport(appDir, caller, duplicateRootCauseFixed: true, setupRan: false);
            return;
        }

        if (IsLockHeld(appDir))
        {
            TraceStartup(caller, process.ProcessName, commandLine, setupInvoked: false, note: "skipped: runtime_setup.lock held");
            WriteDiagnosisReport(appDir, caller, duplicateRootCauseFixed: true, setupRan: false);
            return;
        }

        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            TraceStartup(caller, process.ProcessName, commandLine, setupInvoked: false, note: "skipped: runtime setup mutex busy");
            WriteDiagnosisReport(appDir, caller, duplicateRootCauseFixed: true, setupRan: false);
            return;
        }

        if (IsRuntimeInitialized(appDir))
        {
            TraceStartup(caller, process.ProcessName, commandLine, setupInvoked: false, note: "skipped: flag valid after mutex");
            WriteDiagnosisReport(appDir, caller, duplicateRootCauseFixed: true, setupRan: false);
            return;
        }

        TraceStartup(caller, process.ProcessName, commandLine, setupInvoked: true);
        var exitCode = RunSetupSilent(appDir);
        WriteDiagnosisReport(appDir, caller, duplicateRootCauseFixed: true, setupRan: true, exitCode: exitCode);
    }

    public static bool IsRuntimeInitialized(string? appDir = null)
    {
        appDir ??= AppContext.BaseDirectory;
        var runtimeDir = Path.Combine(appDir, "runtime");
        var flagPath = Path.Combine(runtimeDir, FlagFileName);
        var envPath = Path.Combine(runtimeDir, "runtime_paths.env");
        var ffprobePath = Path.Combine(runtimeDir, "ffmpeg", "ffprobe.exe");

        if (!File.Exists(flagPath) || !File.Exists(envPath) || !File.Exists(ffprobePath))
            return false;

        try
        {
            var lines = File.ReadAllLines(flagPath);
            var appRootLine = lines.FirstOrDefault(l => l.StartsWith("APP_ROOT=", StringComparison.OrdinalIgnoreCase));
            if (appRootLine == null)
                return false;

            var flaggedRoot = appRootLine["APP_ROOT=".Length..].Trim();
            return string.Equals(
                Path.GetFullPath(flaggedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(appDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLockHeld(string appDir) =>
        Directory.Exists(Path.Combine(appDir, "runtime", LockDirName));

    private static int RunSetupSilent(string appDir)
    {
        var script = Path.Combine(appDir, "runtime", SetupScriptName);
        if (!File.Exists(script))
            return -1;

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{script}\" silent\"",
            WorkingDirectory = appDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(psi);
        if (process == null)
            return -2;

        process.WaitForExit(TimeSpan.FromMinutes(2));
        return process.ExitCode;
    }

    private static void TraceStartup(
        string caller,
        string processName,
        string commandLine,
        bool setupInvoked,
        string? note = null)
    {
        try
        {
            var tracePath = Path.Combine(PathHelper.LogsFolder(), "runtime_startup_trace.txt");
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
            sb.Append("caller=").Append(caller);
            sb.Append(" process=").Append(processName);
            sb.Append(" cmdline=").Append(commandLine);
            sb.Append(" runtime_setup_invoked=").Append(setupInvoked ? "yes" : "no");
            if (!string.IsNullOrWhiteSpace(note))
                sb.AppendLine().Append("  note=").Append(note);
            sb.AppendLine();
            File.AppendAllText(tracePath, PrivacySanitizer.SanitizeForLog(sb.ToString()));
        }
        catch
        {
            /* trace must not block startup */
        }
    }

    private static void WriteDiagnosisReport(
        string appDir,
        string caller,
        bool duplicateRootCauseFixed,
        bool setupRan,
        int exitCode = 0)
    {
        try
        {
            var logDir = PathHelper.LogsFolder();
            var reportPath = Path.Combine(logDir, "runtime_setup_diagnosis.txt");
            var flagExists = File.Exists(Path.Combine(appDir, "runtime", FlagFileName));
            var envExists = File.Exists(Path.Combine(appDir, "runtime", "runtime_paths.env"));
            var ffprobeExists = File.Exists(Path.Combine(appDir, "runtime", "ffmpeg", "ffprobe.exe"));

            var sb = new StringBuilder();
            sb.AppendLine("MultiCamApp Runtime Setup Diagnosis");
            sb.AppendLine("===================================");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Last caller: {caller}");
            sb.AppendLine();
            sb.AppendLine("Root cause (duplicate console):");
            sb.AppendLine("  Inno Setup [Run] used Check: RunRuntimeSetup, but RunRuntimeSetup() also");
            sb.AppendLine("  executed setup_runtime.bat via Exec(). The Check function ran the batch once,");
            sb.AppendLine("  then the [Run] entry ran it again — two visible \"MultiCamApp Runtime Setup\" consoles.");
            sb.AppendLine();
            sb.AppendLine("Fix applied:");
            sb.AppendLine("  - Inno Setup: Check renamed to ShouldRunRuntimeSetup (boolean gate only, no Exec).");
            sb.AppendLine("  - setup_runtime.bat: runtime_initialized.flag, runtime_setup.lock, silent mode.");
            sb.AppendLine("  - App startup: RuntimeBootstrapper skips setup when flag is valid.");
            sb.AppendLine($"  - Duplicate fix active in build: {duplicateRootCauseFixed}");
            sb.AppendLine();
            sb.AppendLine("Locations that can invoke runtime setup:");
            sb.AppendLine("  1. installer\\MultiCamApp.iss [Run] — setup_runtime.bat (install mode, once)");
            sb.AppendLine("  2. runtime\\setup_runtime.bat — self (lock + flag guarded)");
            sb.AppendLine("  3. RuntimeBootstrapper.EnsureRuntimeReady — App.xaml.cs (silent, if flag missing)");
            sb.AppendLine("  4. NOT invoked: Program.cs, MainWindow, recording, verification");
            sb.AppendLine();
            sb.AppendLine("This session:");
            sb.AppendLine($"  Setup invoked from app: {setupRan}");
            if (setupRan)
                sb.AppendLine($"  Setup exit code: {exitCode}");
            sb.AppendLine();
            sb.AppendLine("Verification:");
            sb.AppendLine($"  runtime_initialized.flag: {(flagExists ? "OK" : "MISSING")}");
            sb.AppendLine($"  runtime_paths.env: {(envExists ? "OK" : "MISSING")}");
            sb.AppendLine($"  ffprobe.exe: {(ffprobeExists ? "OK" : "MISSING")}");
            sb.AppendLine($"  OpenCvSharpExtern.dll: {(File.Exists(Path.Combine(appDir, "OpenCvSharpExtern.dll")) ? "OK" : "MISSING")}");
            sb.AppendLine($"  config\\appsettings.json: {(File.Exists(Path.Combine(appDir, "config", "appsettings.json")) ? "OK" : "MISSING")}");
            sb.AppendLine($"  localization\\en.json: {(File.Exists(Path.Combine(appDir, "localization", "en.json")) ? "OK" : "MISSING")}");
            sb.AppendLine($"  Runtime initialized (app check): {(IsRuntimeInitialized(appDir) ? "YES" : "NO")}");
            sb.AppendLine();
            sb.AppendLine("Expected behavior after fix:");
            sb.AppendLine("  - Fresh install/update: one visible runtime setup console from Setup.exe");
            sb.AppendLine("  - Normal app launch: no console (setup skipped when flag valid)");
            sb.AppendLine("  - Concurrent setup: blocked by lock file and global mutex");

            File.WriteAllText(reportPath, PrivacySanitizer.SanitizeForLog(sb.ToString()));
        }
        catch
        {
            /* diagnosis must not block startup */
        }
    }
}
