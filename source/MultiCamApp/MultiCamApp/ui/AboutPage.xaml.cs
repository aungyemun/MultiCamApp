using System.Windows;
using System.Windows.Controls;
using MultiCamApp.Core;
using MultiCamApp.Localization;
using MultiCamApp.Utils;

namespace MultiCamApp.Ui;

public partial class AboutPage : UserControl
{
    private VersionInfo? _currentVersion;
    private LanguageManager? _lang;

    public AboutPage() => InitializeComponent();

    public void ApplyLanguage(LanguageManager lang, VersionInfo version)
    {
        _lang = lang;
        _currentVersion = version;

        VersionText.Text = $"MultiCamApp v{version.Version}";
        BuildText.Text = $"{lang["buildNumber"]}: {version.Build}";
        StageText.Text = $"{lang["releaseStage"]}: {version.Stage}";
        ReleaseText.Text = $"Released: {(!string.IsNullOrWhiteSpace(version.ReleaseDate) ? version.ReleaseDate : "Not specified")}";

        CopyVersionBtn.Content = lang["copyVersionInfo"];

        AppInformationTitle.Text = lang["appInformationTitle"];
        CopyrightBody.Text = lang["copyrightBody"];

        LicenseUsageTitle.Text = lang["licenseUsageTitle"];
        LicenseUsageBody.Text = lang["licenseUsageBody"];
        CommercialUseBody.Text = lang["commercialUseBody"];

        CitationTitle.Text = lang["citationTitle"];
        CitationBody.Text = BuildCitationBody(version, lang);
        CopyCitationBtn.Content = lang["copyCitation"];

        AttributionTitle.Text = lang["attributionTitle"];
        AttributionKoketsuTitle.Text = lang["attributionKoketsuTitle"];
        AttributionKoketsuBody.Text = lang["attributionKoketsuBody"];
        AttributionAungTitle.Text = lang["attributionAungTitle"];
        AttributionAungBody.Text = lang["attributionAungBody"];

        ThirdPartyNoticesTitle.Text = lang["thirdPartyNoticesTitle"];
        ThirdPartyNoticesBody.Text = "This application includes third-party components. After installation, see THIRD_PARTY_NOTICES.md and runtime/ffmpeg/FFMPEG_LICENSE.txt for license, attribution, and source-code reference information.";
        ThirdPartyNoticesBtn.Content = lang["viewThirdPartyNotices"];
        FfmpegLicenseBtn.Content = "View FFmpeg License (FFMPEG_LICENSE.txt)";
        ProjectHomepageTitle.Text = lang["projectHomepage"];
        HomepageLink.Text = "https://github.com/aungyemun/MultiCamApp";
        FontHelper.ApplyLanguageFont(this, lang.CurrentLanguage);
    }

    private void CopyVersionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentVersion == null || _lang == null) return;
        
        var info = $"MultiCamApp v{_currentVersion.Version}\n" +
                   $"{_lang["buildNumber"]}: {_currentVersion.Build}\n" +
                   $"{_lang["releaseStage"]}: {_currentVersion.Stage}\n" +
                   (!string.IsNullOrEmpty(_currentVersion.ReleaseDate)
                       ? $"{_lang["releaseDateLabel"]}: {_currentVersion.ReleaseDate}\n"
                       : "") +
                   $"OS: {System.Environment.OSVersion}\n\n" +
                   $"{_lang["citationTitle"]}:\n{BuildCitationBody(_currentVersion, _lang)}";

        TryCopy(info, CopyVersionBtn);
    }

    private void CopyCitationBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentVersion == null || _lang == null) return;
        TryCopy(BuildCitationBody(_currentVersion, _lang), CopyCitationBtn);
    }

    private static string BuildCitationBody(VersionInfo version, LanguageManager lang)
    {
        return $"Mun AY, Koketsu S. MultiCamApp: Offline multi-camera synchronized recording and verification platform for behavioral analysis. Version v{version.Version}. 2026.";
    }

    private void TryCopy(string text, Button btn)
    {
        try
        {
            Clipboard.SetText(text);
            
            // Temporary visual feedback
            var originalContent = btn.Content;
            btn.Content = "✓";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) => { btn.Content = originalContent; timer.Stop(); };
            timer.Start();
        }
        catch
        {
            // Ignore clipboard errors
        }
    }

    private void HomepageLink_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            var url = "https://github.com/aungyemun/MultiCamApp";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening browser
        }
    }

    private void ThirdPartyNoticesBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenLocalFile("THIRD_PARTY_NOTICES.md");
    }

    private void FfmpegLicenseBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenLocalFile(System.IO.Path.Combine("runtime", "ffmpeg", "FFMPEG_LICENSE.txt"));
    }

    private static void OpenLocalFile(string relativePath)
    {
        try
        {
            var candidates = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, relativePath),
                System.IO.Path.Combine(PathHelper.FindProjectRoot() ?? "", relativePath)
            };
            var target = System.Array.Find(candidates, System.IO.File.Exists);
            if (target == null) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening local files.
        }
    }
}
