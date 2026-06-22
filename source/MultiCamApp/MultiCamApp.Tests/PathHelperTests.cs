using MultiCamApp.Utils;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class PathHelperTests
{
    [Fact]
    public void LogsFolder_returns_existing_directory()
    {
        var logs = PathHelper.LogsFolder();
        Assert.False(string.IsNullOrWhiteSpace(logs));
        Directory.CreateDirectory(logs);
        Assert.True(Directory.Exists(logs));

        var probe = Path.Combine(logs, $".test_{Guid.NewGuid():N}");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }

    [Fact]
    public void UserDataRoot_is_under_local_app_data()
    {
        var root = PathHelper.UserDataRoot();
        Assert.Contains("MultiCamApp", root);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            root,
            StringComparison.OrdinalIgnoreCase);
    }
}
