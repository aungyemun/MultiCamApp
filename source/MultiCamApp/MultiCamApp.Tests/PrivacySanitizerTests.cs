using System.Text.Json;
using MultiCamApp.Utils;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>
/// Regression tests for PrivacySanitizer on already-serialized JSON, UNC paths and
/// user/computer-name token replacement (v2.1.0).
/// </summary>
public sealed class PrivacySanitizerTests
{
    [Fact]
    public void SanitizeForOutput_KeepsSerializedJsonValid_AndRedactsFullDrivePath()
    {
        var json = JsonSerializer.Serialize(new { appPath = @"C:\Program Files\MultiCamApp\MultiCamApp.exe" });

        var sanitized = PrivacySanitizer.SanitizeForOutput(json);

        using var doc = JsonDocument.Parse(sanitized); // threw before: "[path]\Program Files..." is an invalid escape
        Assert.Equal("[path]/MultiCamApp.exe", doc.RootElement.GetProperty("appPath").GetString());
        Assert.DoesNotContain("Program Files", sanitized);
    }

    [Fact]
    public void SanitizeForOutput_RedactsUncPaths_PlainAndJsonEscaped()
    {
        var plain = PrivacySanitizer.SanitizeForOutput(@"Saved to \\labnas\share\someone\session1\cam1.mp4");
        Assert.Equal("Saved to [path]/cam1.mp4", plain);

        var json = JsonSerializer.Serialize(new { output = @"\\labnas\share\someone\session1" });
        var sanitized = PrivacySanitizer.SanitizeForOutput(json);
        using var doc = JsonDocument.Parse(sanitized);
        Assert.Equal("[path]/session1", doc.RootElement.GetProperty("output").GetString());
    }

    [Fact]
    public void SanitizeForOutput_LeavesUrlsAndTimesAlone()
    {
        const string text = "See https://github.com/example/repo at 12:30:01";
        Assert.Equal(text, PrivacySanitizer.SanitizeForOutput(text));
    }

    [Fact]
    public void SanitizeForOutput_ForwardSlashDrivePath_IsRedacted()
    {
        Assert.Equal("[path]/cam2.mp4", PrivacySanitizer.SanitizeForOutput("C:/Users/someone/Videos/cam2.mp4"));
    }

    [Theory]
    [InlineData("CameraName=cam1 user=cam", "cam", "CameraName=cam1 user=[user]")]
    [InlineData("lab-PC opened labelled file", "lab", "[user]-PC opened labelled file")]
    [InlineData("Video VIDEO videos", "video", "[user] [user] videos")]
    public void ReplaceTokenIfKnown_ReplacesWholeTokensOnly(string input, string name, string expected)
    {
        Assert.Equal(expected, PrivacySanitizer.ReplaceTokenIfKnown(input, name, "[user]"));
    }

    [Fact]
    public void ReplaceTokenIfKnown_IgnoresNamesShorterThanThreeCharacters()
    {
        Assert.Equal("ab cd", PrivacySanitizer.ReplaceTokenIfKnown("ab cd", "ab", "[user]"));
    }

    // 2.1.0: the long-hex ID rule turned 0.0016666666666666668 into "0.[id]", breaking JSON exports.
    [Theory]
    [InlineData("\"FpsSpread\": 0.0016666666666666668,")]
    [InlineData("DurationSpreadSeconds=12.3456789012345678901")]
    public void DecimalNumbers_AreNotRedactedAsIds(string text)
        => Assert.Equal(text, PrivacySanitizer.SanitizeForOutput(text));

    [Fact]
    public void LongHexIds_AreStillRedacted()
        => Assert.Equal("serial [id] end", PrivacySanitizer.SanitizeForOutput("serial 0123456789ABCDEF0123 end"));

    [Fact]
    public async Task JsonExport_IsValidJson_WithDecimalsAndRedactedPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "mca_json_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var report = new MultiCamApp.Verification.VerificationReport();
            report.Summary.DurationSpreadSeconds = 0.0016666666666666668;
            report.Summary.SelectedFolder = @"C:\Users\someone\Videos\session";
            await new MultiCamApp.Verification.VerificationReportWriter().ExportJsonAsync(report, path);
            var text = await File.ReadAllTextAsync(path);
            using var doc = System.Text.Json.JsonDocument.Parse(text);   // throws if invalid
            Assert.DoesNotContain("[id]", text);
            Assert.DoesNotContain(@"C:\\Users", text);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
