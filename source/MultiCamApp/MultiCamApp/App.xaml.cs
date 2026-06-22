using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using MultiCamApp.Utils;
using MultiCamApp.Core;
using MultiCamApp.Localization;
using MultiCamApp.Diagnostics;

namespace MultiCamApp;

public partial class App : System.Windows.Application
{
    private readonly LogService _log = new();
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;
    private const string SingleInstanceMutexName = @"Local\MultiCamApp.SingleInstance";

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        LogStartupAttempt();

        bool isSmokeTest = false;
        bool isExternal4PreviewStress = false;
        bool isExternal4PreviewStressFinal = false;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                isSmokeTest = true;
            }
            else if (arg.Equals("--external4-preview-stress", StringComparison.OrdinalIgnoreCase))
            {
                isExternal4PreviewStress = true;
            }
            else if (arg.Equals("--external4-preview-stress-final", StringComparison.OrdinalIgnoreCase))
            {
                isExternal4PreviewStress = true;
                isExternal4PreviewStressFinal = true;
            }
        }

        if (!isSmokeTest && !isExternal4PreviewStress && !EnsureSingleUiInstance())
            return;

        if (!isSmokeTest && !isExternal4PreviewStress)
            RuntimeBootstrapper.EnsureRuntimeReady("App.Application_Startup");

        if (isSmokeTest)
        {
            string smokeLog = string.Empty;
            string resultFile = string.Empty;
            try
            {
                smokeLog = GetLogPath("startup_smoketest.log");
                resultFile = GetLogPath("smoke_test_result.txt");
                var smokeResult = RunMinimalSmokeTest(smokeLog);
                File.WriteAllText(resultFile, PrivacySanitizer.SanitizeForOutput(smokeResult.ToReportText()));
                Shutdown(smokeResult.ExitCode);
                return;
            }
            catch (Exception ex)
            {
                var result = SmokeTestResult.Fail(2, "Smoke test exception", ex, AppContext.BaseDirectory, Environment.CurrentDirectory, Environment.GetEnvironmentVariable("PATH"));
                if (!string.IsNullOrWhiteSpace(resultFile))
                {
                    File.WriteAllText(resultFile, PrivacySanitizer.SanitizeForOutput(result.ToReportText()));
                }
                try
                {
                    if (!string.IsNullOrWhiteSpace(smokeLog))
                    {
                        File.AppendAllText(smokeLog, PrivacySanitizer.SanitizeForLog(result.ToLogText()));
                    }
                }
                catch { }
                Shutdown(2);
                return;
            }
        }

        if (isExternal4PreviewStress)
        {
            DispatcherUnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            Exit += OnExit;

            try
            {
                var runner = new External4PreviewStressRunner(isExternal4PreviewStressFinal);
                var exitCode = await runner.RunAsync();
                Shutdown(exitCode);
                return;
            }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Failure("external4-preview-stress", "Stress runner failed", ex);
                Shutdown(3);
                return;
            }
        }

        _log.Info("startup", "Application launched (offline-capable; camera/recording require explicit user action)");

        try
        {
            var language = StartupLanguage.Load();
            var diagnostics = new MultiCamApp.Diagnostics.StartupDiagnostics(language);
            if (!diagnostics.RunFullCheck())
            {
                Shutdown(1);
                return;
            }

            DispatcherUnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            SessionEnding += OnSessionEnding;
            Exit += OnExit;
            _mainWindow = new MainWindow();
            _mainWindow.Show();
        }
        catch (Exception ex)
        {
            var language = StartupLanguage.Load();
            var logDir = GetLogDir();
            var logPath = Path.Combine(logDir, "crash.log");
            var msg = string.Join("\n\n",
            [
                language["startupCouldNotStart"],
                string.Format(language["startupLogFile"], logPath),
                string.Format(language["startupErrorDetail"], ex.Message)
            ]);
            File.AppendAllText(GetLogPath("startup_attempt.log"), PrivacySanitizer.SanitizeForLog($"\nCRITICAL STARTUP ERROR: {ex}\n"));
            File.AppendAllText(logPath, PrivacySanitizer.SanitizeForLog($"[{DateTime.Now}] STARTUP EXCEPTION: {ex}\n"));
            MessageBox.Show(msg, language["startupErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private SmokeTestResult RunMinimalSmokeTest(string smokeLog)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("--- MultiCamApp Smoke Test ---");
        sb.AppendLine($"Timestamp: {DateTime.Now}");
        sb.AppendLine($"App Base Path: {AppContext.BaseDirectory}");
        sb.AppendLine($"Current Working Dir: {Environment.CurrentDirectory}");
        sb.AppendLine($"Command Line: {Environment.CommandLine}");
        sb.AppendLine($"PATH: {Environment.GetEnvironmentVariable("PATH")}");
        sb.AppendLine($"Process Architecture: {(Environment.Is64BitProcess ? "x64" : "x86")}");

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var logDir = GetLogDir();
            sb.AppendLine($"Log Dir: {logDir}");
            sb.AppendLine($"Log Dir Writable: {Directory.Exists(logDir)}");

            var checks = new (string Name, string Path, bool Required)[]
            {
                ("MultiCamApp.exe", Path.Combine(baseDir, "MultiCamApp.exe"), true),
                ("MultiCamApp.dll", Path.Combine(baseDir, "MultiCamApp.dll"), false),
                ("MultiCamApp.runtimeconfig.json", Path.Combine(baseDir, "MultiCamApp.runtimeconfig.json"), false),
                ("OpenCvSharp.dll", Path.Combine(baseDir, "OpenCvSharp.dll"), false),
                ("OpenCvSharpExtern.dll", Path.Combine(baseDir, "OpenCvSharpExtern.dll"), true),
                ("config\\appsettings.json", Path.Combine(baseDir, "config", "appsettings.json"), true),
                ("localization\\en.json", Path.Combine(baseDir, "localization", "en.json"), true),
                ("runtime\\ffmpeg\\ffprobe.exe", Path.Combine(baseDir, "runtime", "ffmpeg", "ffprobe.exe"), true),
            };

            foreach (var check in checks)
            {
                var exists = File.Exists(check.Path);
                sb.AppendLine($"{check.Name}: exists={exists} path={check.Path}");
                if (check.Required && !exists)
                {
                    var result = SmokeTestResult.Fail(1, $"Missing required file: {check.Name}", null, baseDir, Environment.CurrentDirectory, Environment.GetEnvironmentVariable("PATH"));
                    sb.AppendLine(result.ToLogText());
                    File.AppendAllText(smokeLog, PrivacySanitizer.SanitizeForLog(sb.ToString()));
                    return result;
                }
            }

            var ffprobePath = Path.Combine(baseDir, "runtime", "ffmpeg", "ffprobe.exe");
            var ffprobeOutcome = RunWithTimeout(ffprobePath, "-version", TimeSpan.FromSeconds(5), sb);
            if (!ffprobeOutcome.Success)
            {
                var result = SmokeTestResult.Warning(1, ffprobeOutcome.Message, null, baseDir, Environment.CurrentDirectory, Environment.GetEnvironmentVariable("PATH"));
                sb.AppendLine(result.ToLogText());
                File.AppendAllText(smokeLog, PrivacySanitizer.SanitizeForLog(sb.ToString()));
                return result;
            }

            var resultOk = SmokeTestResult.Pass(0, "Minimal smoke test passed", baseDir, Environment.CurrentDirectory, Environment.GetEnvironmentVariable("PATH"));
            sb.AppendLine(resultOk.ToLogText());
            File.AppendAllText(smokeLog, PrivacySanitizer.SanitizeForLog(sb.ToString()));
            return resultOk;
        }
        catch (Exception ex)
        {
            var result = SmokeTestResult.Fail(2, ex.Message, ex, AppContext.BaseDirectory, Environment.CurrentDirectory, Environment.GetEnvironmentVariable("PATH"));
            sb.AppendLine(result.ToLogText());
            File.AppendAllText(smokeLog, PrivacySanitizer.SanitizeForLog(sb.ToString()));
            return result;
        }
    }

    private static (bool Success, string Message) RunWithTimeout(string fileName, string arguments, TimeSpan timeout, System.Text.StringBuilder sb)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                return (false, $"Failed to start {fileName}");
            }

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(true); } catch { }
                return (false, $"{Path.GetFileName(fileName)} timed out after {timeout.TotalSeconds:0}s");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            sb.AppendLine($"[{Path.GetFileName(fileName)} exit={process.ExitCode}]");
            if (!string.IsNullOrWhiteSpace(stdout)) sb.AppendLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) sb.AppendLine(stderr);

            return (process.ExitCode == 0, process.ExitCode == 0 ? "ffprobe OK" : $"{Path.GetFileName(fileName)} exit code {process.ExitCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private sealed class SmokeTestResult
    {
        public string Status { get; }
        public int ExitCode { get; }
        public string ExactFailedCheck { get; }
        public string Timestamp { get; }
        public string AppBaseDir { get; }
        public string CurrentDirectory { get; }
        public string PathValue { get; }
        public string? ExceptionType { get; }
        public string? ExceptionMessage { get; }

        private SmokeTestResult(string status, int exitCode, string exactFailedCheck, string? exceptionType, string? exceptionMessage, string appBaseDir, string currentDirectory, string pathValue)
        {
            Status = status;
            ExitCode = exitCode;
            ExactFailedCheck = exactFailedCheck;
            Timestamp = DateTime.Now.ToString("o");
            AppBaseDir = appBaseDir;
            CurrentDirectory = currentDirectory;
            PathValue = pathValue;
            ExceptionType = exceptionType;
            ExceptionMessage = exceptionMessage;
        }

        public static SmokeTestResult Pass(int exitCode, string exactFailedCheck, string appBaseDir, string currentDirectory, string? pathValue) =>
            new("PASS", exitCode, exactFailedCheck, null, null, appBaseDir, currentDirectory, pathValue ?? "");

        public static SmokeTestResult Warning(int exitCode, string exactFailedCheck, Exception? ex, string appBaseDir, string currentDirectory, string? pathValue) =>
            new("WARNING", exitCode, exactFailedCheck, ex?.GetType().FullName, ex?.Message, appBaseDir, currentDirectory, pathValue ?? "");

        public static SmokeTestResult Fail(int exitCode, string exactFailedCheck, Exception? ex, string appBaseDir, string currentDirectory, string? pathValue) =>
            new("FAIL", exitCode, exactFailedCheck, ex?.GetType().FullName, ex?.Message, appBaseDir, currentDirectory, pathValue ?? "");

        public string ToReportText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Status);
            sb.AppendLine(ExitCode.ToString());
            sb.AppendLine(ExactFailedCheck);
            sb.AppendLine(Timestamp);
            sb.AppendLine(AppBaseDir);
            sb.AppendLine(CurrentDirectory);
            sb.AppendLine(PathValue);
            if (!string.IsNullOrWhiteSpace(ExceptionType)) sb.AppendLine(ExceptionType);
            if (!string.IsNullOrWhiteSpace(ExceptionMessage)) sb.AppendLine(ExceptionMessage);
            return sb.ToString();
        }

        public string ToLogText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{Timestamp}] Status={Status}");
            sb.AppendLine($"ExitCode={ExitCode}");
            sb.AppendLine($"ExactFailedCheck={ExactFailedCheck}");
            sb.AppendLine($"AppBaseDir={AppBaseDir}");
            sb.AppendLine($"CurrentDirectory={CurrentDirectory}");
            sb.AppendLine($"PATH={PathValue}");
            if (!string.IsNullOrWhiteSpace(ExceptionType)) sb.AppendLine($"ExceptionType={ExceptionType}");
            if (!string.IsNullOrWhiteSpace(ExceptionMessage)) sb.AppendLine($"ExceptionMessage={ExceptionMessage}");
            return sb.ToString();
        }
    }

    private void LogStartupAttempt()
    {
        try
        {
            var logPath = GetLogPath("startup_attempt.log");
            var sb = new System.Text.StringBuilder();
            var baseDir = AppContext.BaseDirectory;
            sb.AppendLine("--- MultiCamApp Startup Attempt ---");
            sb.AppendLine($"Timestamp: {DateTime.Now}");
            sb.AppendLine($"App Version: {VersionService.Load().Display}");
            sb.AppendLine($"App Base Path: {baseDir}");
            sb.AppendLine($"Current Working Dir: {Environment.CurrentDirectory}");
            sb.AppendLine($"Command Line: {Environment.CommandLine}");
            sb.AppendLine($"OS Version: {Environment.OSVersion}");
            sb.AppendLine($"Process Architecture: {(Environment.Is64BitProcess ? "x64" : "x86")}");

            var configPath = Path.Combine(baseDir, "config", "appsettings.json");
            sb.AppendLine($"Config Path exists: {File.Exists(configPath)} ({configPath})");
            
            var locPath = Path.Combine(baseDir, "localization");
            sb.AppendLine($"Localization Dir exists: {Directory.Exists(locPath)} ({locPath})");
            
            var ffprobePath = Path.Combine(baseDir, "runtime", "ffmpeg", "ffprobe.exe");
            sb.AppendLine($"ffprobe.exe exists: {File.Exists(ffprobePath)} ({ffprobePath})");
            
            var opencvPath = Path.Combine(baseDir, "OpenCvSharpExtern.dll");
            sb.AppendLine($"OpenCvSharpExtern.dll exists: {File.Exists(opencvPath)} ({opencvPath})");
            
            var envPath = Path.Combine(baseDir, "runtime", "runtime_paths.env");
            sb.AppendLine($"runtime_paths.env exists: {File.Exists(envPath)} ({envPath})");

            // Offline check
            sb.AppendLine("Internet check: Skipped (Offline-first policy).");
            sb.AppendLine("No internet-dependent startup tasks scheduled.");
            sb.AppendLine("-----------------------------------");
            File.WriteAllText(logPath, PrivacySanitizer.SanitizeForLog(sb.ToString()));
        }
        catch { }
    }

    private string GetLogDir() => PathHelper.LogsFolder();

    private string GetLogPath(string fileName)
    {
        var dir = GetLogDir();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    private bool EnsureSingleUiInstance()
    {
        CleanupHiddenSiblingProcesses();

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (createdNew)
            return true;

        _log.Info("startup", "Another MultiCamApp instance is already running; shutting down this launch");
        MessageBox.Show(
            "MultiCamApp is already running. Close the existing window before launching again.",
            "MultiCamApp already running",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Shutdown(0);
        return false;
    }

    private void CleanupHiddenSiblingProcesses()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            var currentPath = current.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                using (process)
                {
                    if (process.Id == current.Id)
                        continue;

                    string? path;
                    try { path = process.MainModule?.FileName; }
                    catch { continue; }

                    if (!string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (process.MainWindowHandle != IntPtr.Zero)
                        continue;

                    _log.Info("startup", $"Terminating hidden stale MultiCamApp process pid={process.Id}");
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                    }
                    catch (Exception ex)
                    {
                        _log.Info("startup", $"Could not terminate hidden stale process pid={process.Id}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Info("startup", $"Hidden process cleanup skipped: {ex.Message}");
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        _log.Error("crash", e.Exception.Message, e.Exception);
        _ = _mainWindow?.ForceCleanupAsync();
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            var language = StartupLanguage.Load();
            MessageBox.Show(
                string.Format(language["startupCrashMessage"], e.Exception.Message),
                language["startupCrashTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        e.Handled = true;
    }

    private void OnDomainUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex);
            _log.Error("crash", ex.Message, ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        _log.Error("crash", "Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void LogCrash(Exception ex)
    {
        try
        {
            AppDiagnosticLogger.Failure("app", "Unhandled exception", ex);
            var logPath = GetLogPath("crash.log");
            var msg = $"[{DateTime.Now}] CRASH: {ex.GetType().FullName}: {ex.Message}\nStack Trace:\n{ex.StackTrace}\n";
            if (ex.InnerException != null)
            {
                msg += $"Inner Exception: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}\n";
            }
            File.AppendAllText(logPath, PrivacySanitizer.SanitizeForLog(msg + "\n-----------------------------------\n"));
        }
        catch { }
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e) =>
        _mainWindow?.ForceCleanupAsync().GetAwaiter().GetResult();

    private void OnExit(object sender, ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }

        _log.Info("startup", "Application exit");
    }
}
