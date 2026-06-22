using MultiCamApp.Diagnostics;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class RuntimeBootstrapperTests
{
    [Fact]
    public void IsRuntimeInitialized_ReturnsFalse_WhenFlagMissing()
    {
        var temp = Path.Combine(Path.GetTempPath(), "multicam_rt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "runtime"));
        try
        {
            Assert.False(RuntimeBootstrapper.IsRuntimeInitialized(temp));
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public void IsRuntimeInitialized_ReturnsTrue_WhenFlagAndEnvMatchAppRoot()
    {
        var temp = Path.Combine(Path.GetTempPath(), "multicam_rt_" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(temp, "runtime");
        Directory.CreateDirectory(Path.Combine(runtime, "ffmpeg"));
        File.WriteAllText(Path.Combine(runtime, "ffmpeg", "ffprobe.exe"), "stub");
        File.WriteAllText(Path.Combine(runtime, "runtime_paths.env"), "APP_ROOT=" + temp);
        File.WriteAllText(Path.Combine(runtime, "runtime_initialized.flag"), $"APP_ROOT={temp}\r\nVERSION=1.0.46\r\n");
        try
        {
            Assert.True(RuntimeBootstrapper.IsRuntimeInitialized(temp));
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}
