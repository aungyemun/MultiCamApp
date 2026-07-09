using System.IO;
using System.Text.Json;
using System.Windows;
using MultiCamApp.Localization;
using MultiCamApp.Utils;

namespace MultiCamApp.Diagnostics;

public sealed class StartupDiagnostics
{
    private readonly LogService _log = new();
    private readonly string _appDir = AppDomain.CurrentDomain.BaseDirectory;
    private readonly LanguageManager _language;

    public StartupDiagnostics(LanguageManager? language = null) =>
        _language = language ?? StartupLanguage.Load();

    public bool RunFullCheck(bool headless = false)
    {
        var results = new StartupResults
        {
            Timestamp = DateTime.Now,
            AppVersion = GetAppVersion(),
            OsVersion = Environment.OSVersion.ToString(),
            AppPath = _appDir
        };

        // 1. Check FFmpeg
        results.FfprobePath = Path.Combine(_appDir, "runtime", "ffmpeg", "ffprobe.exe");
        results.FfprobeExists = File.Exists(results.FfprobePath);

        // 2. Check OpenCV DLLs
        results.OpenCvExternExists = File.Exists(Path.Combine(_appDir, "OpenCvSharpExtern.dll"));
        results.OpenCvSharpExists = File.Exists(Path.Combine(_appDir, "OpenCvSharp.dll"))
            || IsOpenCvSharpManagedAssemblyAvailable();

        // 3. Check VC++ Runtime DLLs (System32)
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        results.VcRuntime140Exists = File.Exists(Path.Combine(system32, "vcruntime140.dll"));
        results.Msvcp140Exists = File.Exists(Path.Combine(system32, "msvcp140.dll"));
        results.VcRuntime140_1Exists = File.Exists(Path.Combine(system32, "vcruntime140_1.dll"));

        // 4. Check Config & Localization
        results.ConfigExists = File.Exists(Path.Combine(_appDir, "config", "appsettings.json"));
        results.LocalizationExists = Directory.Exists(Path.Combine(_appDir, "localization")) && 
                                     Directory.GetFiles(Path.Combine(_appDir, "localization"), "*.json").Length > 0;

        // 5. Test Native DLL Loading (advisory — V2 uses MediaFoundation, not OpenCV)
        try
        {
            if (results.OpenCvExternExists)
            {
                System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(_appDir, "OpenCvSharpExtern.dll"));
                results.NativeDllLoadOk = true;
            }
            else
            {
                // OpenCV DLL is not present; V2 pipeline does not require it.
                results.NativeDllLoadOk = true;
            }
        }
        catch (Exception ex)
        {
            // OpenCV load failed — log as advisory, do not block startup.
            results.NativeDllLoadError = ex.Message;
            results.NativeDllLoadOk = true; // non-fatal: V2 pipeline does not need OpenCV
            _log.Warn("startup", $"OpenCvSharpExtern.dll load failed (advisory): {ex.Message}");
        }

        SaveResults(results);

        if (!results.FfprobeExists)
        {
            _log.Warn("startup", "ffprobe.exe missing; video verification will be disabled.");
        }

        if (!results.ConfigExists || !results.LocalizationExists)
        {
            if (!headless)
                ShowCriticalError(_language["startupConfigMissing"]);
            return false;
        }

        return true;
    }

    private string GetAppVersion()
    {
        try
        {
            var versionPath = Path.Combine(_appDir, "config", "version.json");
            if (File.Exists(versionPath))
            {
                var json = File.ReadAllText(versionPath);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("version").GetString() ?? "0.0.0";
            }
        }
        catch { }
        return "0.0.0";
    }

    private static bool IsOpenCvSharpManagedAssemblyAvailable()
    {
        try
        {
            return Type.GetType("OpenCvSharp.Mat, OpenCvSharp", throwOnError: false) != null;
        }
        catch
        {
            return false;
        }
    }

    private void SaveResults(StartupResults results)
    {
        try
        {
            var logDir = PathHelper.LogsFolder();
            var path = Path.Combine(logDir, "startup_diagnostics.json");
            var json = PrivacySanitizer.SanitizeForOutput(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _log.Error("startup", "Failed to save startup diagnostics", ex);
        }
    }

    private void ShowCriticalError(string message)
    {
        MessageBox.Show(
            string.Format(_language["startupValidationFailed"], message),
            _language["startupCriticalTitle"],
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private class StartupResults
    {
        public DateTime Timestamp { get; set; }
        public string? AppVersion { get; set; }
        public string? OsVersion { get; set; }
        public string? AppPath { get; set; }
        public string? FfprobePath { get; set; }
        public bool FfprobeExists { get; set; }
        public bool OpenCvExternExists { get; set; }
        public bool OpenCvSharpExists { get; set; }
        public bool VcRuntime140Exists { get; set; }
        public bool Msvcp140Exists { get; set; }
        public bool VcRuntime140_1Exists { get; set; }
        public bool NativeDllLoadOk { get; set; }
        public string? NativeDllLoadError { get; set; }
        public bool ConfigExists { get; set; }
        public bool LocalizationExists { get; set; }
    }
}
