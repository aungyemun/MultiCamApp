using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MultiCamApp.Core;

namespace MultiCamApp.Ui;

public partial class HeaderBar : UserControl
{
    public event EventHandler<string>? LanguageChanged;
    public event EventHandler? AboutRequested;

    public ImageSource? LogoSource => LogoImage.Source;

    public HeaderBar() => InitializeComponent();

    public void LoadLogo()
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "icons", "Multicam.png"),
            Path.Combine(Utils.PathHelper.FindProjectRoot() ?? "", "source", "MultiCamApp", "MultiCamApp", "assets", "icons", "Multicam.png")
        };
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            LogoImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(p));
            break;
        }
    }

    public void Apply(VersionInfo version, string appTitle, string subtitle, string languageLabel, string aboutButtonText)
    {
        AppTitleText.Text = appTitle;
        VersionText.Text = version.HeaderVersionLabel;
        VersionText.ToolTip = version.StageTooltip;
        SubtitleText.Text = subtitle;
        LangLabel.Text = languageLabel;
        AboutButton.Content = aboutButtonText;
    }

    public void SetLanguageLockEnabled(bool enabled, string? lockedTooltip = null)
    {
        LanguageBox.IsEnabled = enabled;
        LanguageBox.ToolTip = enabled ? null : (object)(lockedTooltip ?? "Language cannot be changed during recording.");
    }

    public void SyncLanguage(string code)
    {
        for (var i = 0; i < LanguageBox.Items.Count; i++)
        {
            if (LanguageBox.Items[i] is ComboBoxItem ci && (string?)ci.Tag == code)
            {
                if (LanguageBox.SelectedIndex != i)
                    LanguageBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is ComboBoxItem item && item.Tag is string code)
            LanguageChanged?.Invoke(this, code);
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        // MessageBox.Show("HeaderBar: About Button Clicked");
        AboutRequested?.Invoke(this, EventArgs.Empty);
    }
}
