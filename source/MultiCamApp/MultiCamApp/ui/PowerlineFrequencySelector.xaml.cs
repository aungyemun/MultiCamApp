using System.Windows;
using System.Windows.Controls;
using MultiCamApp.Capture.VideoEngineV2;
using MultiCamApp.Localization;

namespace MultiCamApp.Ui;

/// <summary>
/// Selects the camera anti-flicker mains frequency (UVC Power Line Frequency). Writes
/// <see cref="VideoEngineSettings.PowerlineFrequencyHz"/>; the value is applied to every camera
/// when preview starts and recorded in the per-camera metadata (controls.flickerReduction).
/// </summary>
public partial class PowerlineFrequencySelector : UserControl
{
    private LanguageManager? _language;
    private bool _syncing;

    public PowerlineFrequencySelector()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachLanguage();
    }

    private void AttachLanguage()
    {
        var lang = Diagnostics.DiagnosticsLocalization.Current;
        if (lang is not null && !ReferenceEquals(lang, _language))
        {
            if (_language is not null) _language.LanguageChanged -= ApplyTexts;
            _language = lang;
            _language.LanguageChanged += ApplyTexts;
        }
        ApplyTexts();
        SyncSelection();
    }

    private string T(string key, string fallback) =>
        _language?[key] is { Length: > 0 } v && v != key ? v : fallback;

    private void ApplyTexts()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(ApplyTexts); return; }
        HeaderText.Text = T("powerlineHeader", "Anti-flicker (mains frequency)");
        HintText.Text = T("powerlineHint", "Match your local mains to avoid flicker/banding under fluorescent or LED light (Japan: east 50 Hz, west 60 Hz). Applied when you start preview.");
        var labels = new[]
        {
            T("powerlineKeep", "Camera default"),
            T("powerline50", "50 Hz"),
            T("powerline60", "60 Hz"),
            T("powerlineAuto", "Auto (camera)"),
        };
        for (var i = 0; i < FrequencyBox.Items.Count && i < labels.Length; i++)
            ((ComboBoxItem)FrequencyBox.Items[i]).Content = labels[i];
    }

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            var current = VideoEngineSettings.PowerlineFrequencyHz.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (ComboBoxItem item in FrequencyBox.Items)
                if ((string)item.Tag == current) { FrequencyBox.SelectedItem = item; break; }
            if (FrequencyBox.SelectedIndex < 0) FrequencyBox.SelectedIndex = 0;
        }
        finally { _syncing = false; }
    }

    private void FrequencyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FrequencyBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (int.TryParse(tag, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var hz))
        {
            VideoEngineSettings.PowerlineFrequencyHz = hz;
            Utils.AppDiagnosticLogger.Runtime($"POWERLINE_FREQUENCY_SELECTED hz={hz}");
        }
    }
}
