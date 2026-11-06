using System.Windows;
using System.Windows.Controls;
using MultiCamApp.Capture.VideoEngineV2;
using MultiCamApp.Localization;

namespace MultiCamApp.Ui;

/// <summary>
/// Video quality / file size preset for VideoEngineV2 (2.1.0). Sets
/// <see cref="VideoEngineSettings.BitrateProfile"/> (bitrate defined at 1080p30 and scaled by pixel rate,
/// see <see cref="VideoEngineSettings.ResolveTargetBitrateKbps"/>). Each item shows the expected file size
/// per camera-minute for the currently selected resolution/FPS. The profile is recorded in the metadata.
/// </summary>
public partial class VideoQualitySelector : UserControl
{
    private LanguageManager? _language;
    private bool _syncing;

    public VideoQualitySelector()
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
        HeaderText.Text = T("qualityHeader", "Video quality (file size)");
        HintText.Text = T("qualityHint", "Maximum keeps the most detail for analysis. Compact roughly halves file size; fine detail and noise are smoothed more. Applied when recording starts.");
        var (w, h, fps) = (VideoEngineSettings.DefaultPreferredWidth, VideoEngineSettings.DefaultPreferredHeight, VideoEngineSettings.DefaultPreferredFps);
        var names = new[] { T("qualityMaximum", "Maximum (default)"), T("qualityHigh", "High"), T("qualityCompact", "Compact") };
        var profiles = new[] { V2BitrateProfile.WindowsCameraLike, V2BitrateProfile.High, V2BitrateProfile.Standard };
        var perMin = T("qualityPerMinute", "≈ {0:F0} MB/min per camera");
        for (var i = 0; i < QualityBox.Items.Count && i < names.Length; i++)
            ((ComboBoxItem)QualityBox.Items[i]).Content =
                $"{names[i]}  ·  {string.Format(System.Globalization.CultureInfo.CurrentCulture, perMin, VideoEngineSettings.MegabytesPerMinute(profiles[i], w, h, fps))}";
        if (QualityBox.Items.Count > 3)
            ((ComboBoxItem)QualityBox.Items[3]).Content = T("qualityEfficient", "Efficient (constant quality) · size follows scene detail");
    }

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            var current = VideoEngineSettings.RateControl == V2RateControl.QualityVbr ? "Efficient" : VideoEngineSettings.BitrateProfile.ToString();
            foreach (ComboBoxItem item in QualityBox.Items)
                if ((string)item.Tag == current) { QualityBox.SelectedItem = item; break; }
            if (QualityBox.SelectedIndex < 0) QualityBox.SelectedIndex = 0;
        }
        finally { _syncing = false; }
    }

    // Resolution/FPS can change after load: refresh the size estimates whenever the list opens.
    private void QualityBox_DropDownOpened(object? sender, EventArgs e) => ApplyTexts();

    private void QualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || QualityBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (tag == "Efficient")
        {
            // Constant-quality rate control; the bitrate profile stays at Maximum as the fallback ceiling reference.
            VideoEngineSettings.RateControl = V2RateControl.QualityVbr;
            VideoEngineSettings.EncoderQuality = VideoEngineSettings.EfficientEncoderQuality;
            VideoEngineSettings.BitrateProfile = V2BitrateProfile.WindowsCameraLike;
            Utils.AppDiagnosticLogger.Runtime($"VIDEO_QUALITY_SELECTED profile=Efficient rateControl=QualityVbr quality={VideoEngineSettings.EncoderQuality}");
        }
        else if (Enum.TryParse<V2BitrateProfile>(tag, out var profile))
        {
            VideoEngineSettings.RateControl = V2RateControl.ConstantBitrate;
            VideoEngineSettings.BitrateProfile = profile;
            Utils.AppDiagnosticLogger.Runtime($"VIDEO_QUALITY_SELECTED profile={profile} baseKbps={(int)profile}");
        }
    }
}
