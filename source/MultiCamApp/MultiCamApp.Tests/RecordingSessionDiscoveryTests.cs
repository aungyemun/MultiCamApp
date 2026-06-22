using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class RecordingSessionDiscoveryTests
{
    [Fact]
    public void DiscoverSessionFolders_FindsNestedSessions()
    {
        var root = CreateTempRoot();
        try
        {
            var session1 = Path.Combine(root, "June_10_final_test1_20260610_085138");
            var session2 = Path.Combine(root, "June_10_final_test2_20260610_090000");
            Directory.CreateDirectory(Path.Combine(session1, "cam1"));
            Directory.CreateDirectory(Path.Combine(session1, "cam2"));
            Directory.CreateDirectory(Path.Combine(session2, "cam1"));

            var sessions = RecordingSessionDiscovery.DiscoverSessionFolders(root);

            Assert.Equal(2, sessions.Count);
            Assert.Contains(session1, sessions, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(session2, sessions, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DiscoverSessionFolders_TreatsSelectedSessionFolderAsSingleSession()
    {
        var root = CreateTempRoot();
        try
        {
            var session = Path.Combine(root, "single_session");
            Directory.CreateDirectory(Path.Combine(session, "cam1"));
            Directory.CreateDirectory(Path.Combine(session, "cam3"));

            var sessions = RecordingSessionDiscovery.DiscoverSessionFolders(session);

            Assert.Single(sessions);
            Assert.Equal(Path.GetFullPath(session), sessions[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetCameraFolders_OrdersCamSlotsNumerically()
    {
        var root = CreateTempRoot();
        try
        {
            var session = Path.Combine(root, "session");
            Directory.CreateDirectory(Path.Combine(session, "cam4"));
            Directory.CreateDirectory(Path.Combine(session, "cam1"));
            Directory.CreateDirectory(Path.Combine(session, "cam10"));

            var folders = RecordingSessionDiscovery.GetCameraFolders(session);

            Assert.Equal(3, folders.Count);
            Assert.Equal("cam1", Path.GetFileName(folders[0]));
            Assert.Equal("cam4", Path.GetFileName(folders[1]));
            Assert.Equal("cam10", Path.GetFileName(folders[2]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "MultiCamAppTests_" + Guid.NewGuid().ToString("N"));
}
