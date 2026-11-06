using System.Reflection;
using System.Text.Json;
using MultiCamApp.Localization;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>English/Japanese key parity and English fallback for missing Japanese keys (v2.1.0).</summary>
public sealed class LocalizationParityTests
{
    private static Dictionary<string, string> ReadEmbedded(string fileName)
    {
        var asm = typeof(LanguageManager).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())!;
    }

    [Fact]
    public void EnglishAndJapanese_HaveIdenticalKeySets()
    {
        var en = ReadEmbedded("en.json").Keys.ToHashSet();
        var ja = ReadEmbedded("ja.json").Keys.ToHashSet();

        Assert.Empty(en.Except(ja));
        Assert.Empty(ja.Except(en));
    }

    [Theory]
    [InlineData("nonFatalErrorTitle")]
    [InlineData("nonFatalErrorMessage")]
    [InlineData("hwDiagCopyFailed")]
    public void NewKeys_ResolveInBothLanguages(string key)
    {
        var lang = new LanguageManager();
        lang.Load("en");
        Assert.NotEqual(key, lang[key]);
        lang.Load("ja");
        Assert.Equal("ja", lang.CurrentLanguage);
        Assert.NotEqual(key, lang[key]);
    }

    [Fact]
    public void Japanese_FallsBackToEnglish_ForEveryEnglishKey()
    {
        var lang = new LanguageManager();
        lang.Load("ja");
        foreach (var key in ReadEmbedded("en.json").Keys)
            Assert.NotEqual(key, lang[key]);
    }
}
