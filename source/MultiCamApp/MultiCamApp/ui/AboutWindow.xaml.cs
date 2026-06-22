using System.Windows;
using MultiCamApp.Core;
using MultiCamApp.Localization;

namespace MultiCamApp.Ui;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    public void ApplyLanguage(LanguageManager lang, VersionInfo version)
    {
        Title = lang["aboutWindowTitle"];
        AboutContent.ApplyLanguage(lang, version);
    }
}
