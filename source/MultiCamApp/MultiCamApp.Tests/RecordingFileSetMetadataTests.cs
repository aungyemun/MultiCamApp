using MultiCamApp.Recording.Writers;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class RecordingFileSetMetadataTests
{
    [Fact]
    public void Create_ProvidesPrefixedAndCanonicalMetadataPaths()
    {
        var cameraFolder = Path.Combine("session", "cam1");
        var files = RecordingFileSet.Create(cameraFolder, "cam1");

        Assert.Equal(Path.Combine(cameraFolder, "cam1_metadata.json"), files.MetadataJsonPath);
        Assert.Equal(Path.Combine(cameraFolder, "cam1_metadata.txt"), files.MetadataTxtPath);
        Assert.Equal(Path.Combine(cameraFolder, "metadata.json"), files.CanonicalMetadataJsonPath);
        Assert.Equal(Path.Combine(cameraFolder, "metadata.txt"), files.CanonicalMetadataTxtPath);
    }
}
