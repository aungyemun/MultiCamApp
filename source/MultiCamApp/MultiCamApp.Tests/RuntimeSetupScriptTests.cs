using System.Diagnostics;
using MultiCamApp.Diagnostics;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>
/// Runs the real runtime/setup_runtime.bat against a throwaway app folder and checks that the
/// flag it writes is accepted by RuntimeBootstrapper.IsRuntimeInitialized. Before v2.1.0 the
/// script never wrote APP_ROOT=..., so the app re-ran setup synchronously on every launch.
/// </summary>
public sealed class RuntimeSetupScriptTests
{
    private static string? FindRepoScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "runtime", "setup_runtime.bat");
            if (File.Exists(candidate) && Directory.Exists(Path.Combine(dir.FullName, "source")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with (parens) and spaces")]
    public void SetupScript_WritesFlagThatBootstrapperAccepts(string folderName)
    {
        var script = FindRepoScript();
        Assert.True(script is not null, "runtime/setup_runtime.bat not found above the test output folder");

        var appDir = Path.Combine(Path.GetTempPath(), "multicam_rtscript_" + Guid.NewGuid().ToString("N"), folderName);
        var runtime = Path.Combine(appDir, "runtime");
        Directory.CreateDirectory(Path.Combine(runtime, "ffmpeg"));
        File.Copy(script!, Path.Combine(runtime, "setup_runtime.bat"));
        File.WriteAllText(Path.Combine(runtime, "ffmpeg", "ffprobe.exe"), "stub");
        File.WriteAllText(Path.Combine(runtime, "ffmpeg", "ffmpeg.exe"), "stub");
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c \"\"{Path.Combine(runtime, "setup_runtime.bat")}\" silent\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = appDir,
            };
            using var process = Process.Start(psi)!;
            Assert.True(process.WaitForExit(30_000), "setup_runtime.bat did not finish");
            Assert.Equal(0, process.ExitCode);

            var flag = File.ReadAllText(Path.Combine(runtime, "runtime_initialized.flag"));
            Assert.Contains("APP_ROOT=", flag);
            Assert.Contains("FFPROBE_PATH=", File.ReadAllText(Path.Combine(runtime, "runtime_paths.env")));
            Assert.True(RuntimeBootstrapper.IsRuntimeInitialized(appDir));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(appDir)!, true); } catch { }
        }
    }
}
