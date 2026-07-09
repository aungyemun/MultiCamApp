using MultiCamApp.Capture.VideoEngineV2;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MultiCamApp.Ui;

/// <summary>
/// Developer / test toggle panel for VideoEngineV2.
/// Shows backend status, renderer type, live FPS, camera connection, and any warnings.
/// Bind to a <see cref="VideoEngineV2"/> instance via <see cref="Attach"/>.
/// </summary>
public partial class VideoEngineV2TestPanel : UserControl
{
    private VideoEngineV2? _engine;
    private readonly DispatcherTimer _refreshTimer;

    public VideoEngineV2TestPanel()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _refreshTimer.Tick += (_, _) => RefreshStatus();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Attaches the panel to a <see cref="VideoEngineV2"/> instance and starts the status refresh timer.
    /// Call from the parent window after creating the engine.
    /// </summary>
    public void Attach(VideoEngineV2 engine)
    {
        _engine = engine;
        _engine.DiagnosticsAvailable += OnDiagnosticsAvailable;
        _refreshTimer.Start();
        SyncCheckBoxes();
    }

    /// <summary>Detaches from the engine and stops the timer.</summary>
    public void Detach()
    {
        _refreshTimer.Stop();
        if (_engine is not null)
        {
            _engine.DiagnosticsAvailable -= OnDiagnosticsAvailable;
            _engine = null;
        }
    }

    // ── Status refresh ────────────────────────────────────────────────────────

    private void OnDiagnosticsAvailable(object? sender, VideoEngineDiagnosticsSnapshot snap)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ApplySnapshot(snap));
    }

    private void RefreshStatus()
    {
        if (_engine is null) return;
        ApplySnapshot(_engine.GetDiagnosticsSnapshot());
    }

    private void ApplySnapshot(VideoEngineDiagnosticsSnapshot snap)
    {
        BackendLabel.Text    = snap.Backend.ToString();
        RendererLabel.Text   = snap.ActivePreviewRenderer.ToString();
        EncoderLabel.Text    = snap.ActiveEncoderBackend == EncoderBackendType.NotSelected
                                   ? "—" : snap.ActiveEncoderBackend.ToString();
        HwEncoderLabel.Text  = snap.ActiveEncoderBackend == EncoderBackendType.NotSelected
                                   ? "—"
                                   : snap.HardwareEncoderUsed ? "Hardware (NVENC/QuickSync)" : "Software (MF H.264)";
        ResolutionLabel.Text = snap.SelectedFormat?.ToString() ?? "—";
        LiveFpsLabel.Text    = snap.HealthSnapshot is not null
                                   ? $"{snap.HealthSnapshot.LiveFps:F1} fps" : "—";

        // Frames delivered / dropped
        if (snap.HealthSnapshot is not null)
        {
            // FramesDropped is always 0 — MediaFrameReader.Realtime drops silently, no callback.
            FramesLabel.Text = $"{snap.HealthSnapshot.FramesDelivered} delivered  " +
                               "dropped=N/A (Realtime mode)";
        }
        else
        {
            FramesLabel.Text = "—";
        }

        // Shutter speed (exposure readback, in ms and 1/N format)
        var exposureResult = snap.ControlResults.FirstOrDefault(
            r => r.Control == V2CameraControl.Exposure);
        if (exposureResult?.ReadbackValue is { } expSec && expSec > 0)
        {
            double expMs = expSec * 1000.0;
            double shutterN = 1.0 / expSec;
            ShutterLabel.Text = $"{expMs:F2} ms  (1/{shutterN:F0}s)";
        }
        else if (exposureResult?.ReadbackStatus == V2ControlReadbackStatus.Unsupported)
        {
            ShutterLabel.Text = "Unsupported";
        }
        else
        {
            ShutterLabel.Text = "—";
        }

        CameraStatusLabel.Text = snap.SelectedDeviceName is null
                                     ? "No device" : $"{snap.SelectedDeviceName} — {snap.PipelineState}";
        FormatKindLabel.Text   = snap.FormatSelectionKind?.ToString() ?? "—";
        CapabilityLabel.Text   = $"D3D11={snap.Direct3DAvailability}  MF={snap.MediaFoundationAvailability}";
        UsbPnpLabel.Text       = snap.UsbPnpStatus ?? "—";

        // Fallback warning
        if (!string.IsNullOrEmpty(snap.FallbackReason))
        {
            FallbackWarningLabel.Text       = $"Format fallback: {snap.FallbackReason}";
            FallbackWarningLabel.Visibility = Visibility.Visible;
        }
        else
        {
            FallbackWarningLabel.Visibility = Visibility.Collapsed;
        }

        // Studio Effects warning
        if (!string.IsNullOrEmpty(snap.WindowsStudioEffectsWarning))
        {
            StudioEffectsWarningLabel.Text       = snap.WindowsStudioEffectsWarning;
            StudioEffectsWarningLabel.Visibility = Visibility.Visible;
        }
        else
        {
            StudioEffectsWarningLabel.Visibility = Visibility.Collapsed;
        }
    }

    // ── Preview bitmap ────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the V2 cam1 preview surface. Pass null to clear.
    /// Safe to call from any thread.
    /// </summary>
    public void SetPreviewBitmap(System.Windows.Media.Imaging.WriteableBitmap? bitmap)
    {
        Dispatcher.InvokeAsync(() =>
        {
            V2PreviewImage.Source              = bitmap;
            PreviewPlaceholderLabel.Visibility = bitmap is null
                ? Visibility.Visible
                : Visibility.Collapsed;
        });
    }

    /// <summary>Shows a red error message in the panel.</summary>
    public void ShowError(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ErrorLabel.Text       = message;
            ErrorLabel.Visibility = Visibility.Visible;
        });
    }

    /// <summary>Clears the error label.</summary>
    public void ClearError()
    {
        Dispatcher.InvokeAsync(() =>
        {
            ErrorLabel.Text       = "";
            ErrorLabel.Visibility = Visibility.Collapsed;
        });
    }

    // ── CheckBox sync ─────────────────────────────────────────────────────────

    private void SyncCheckBoxes()
    {
        EnabledCheckBox.IsChecked       = VideoEngineV2Flags.Enabled;
        PreviewTestCheckBox.IsChecked   = VideoEngineV2Flags.AllowCam1PreviewTest;
        RecordingTestCheckBox.IsChecked = VideoEngineSettings.AllowCam1RecordingTest;
    }

    // ── CheckBox handlers ──────────────────────────────────────────────────────

    private void EnabledCheckBox_Checked(object sender, RoutedEventArgs e)
        => VideoEngineV2Flags.Enabled = true;

    private void EnabledCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        VideoEngineV2Flags.Enabled = false;
        // Cascade: disabling master also disables sub-flags
        VideoEngineV2Flags.AllowCam1PreviewTest    = false;
        VideoEngineV2Flags.UseAsDefaultPipeline    = false;
        VideoEngineSettings.AllowCam1RecordingTest = false;
        SyncCheckBoxes();
    }

    private void PreviewTestCheckBox_Checked(object sender, RoutedEventArgs e)
        => VideoEngineV2Flags.AllowCam1PreviewTest = true;

    private void PreviewTestCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        VideoEngineV2Flags.AllowCam1PreviewTest    = false;
        VideoEngineV2Flags.UseAsDefaultPipeline    = false;
        VideoEngineSettings.AllowCam1RecordingTest = false;
        SyncCheckBoxes();
    }

    private void RecordingTestCheckBox_Checked(object sender, RoutedEventArgs e)
        => VideoEngineSettings.AllowCam1RecordingTest = true;

    private void RecordingTestCheckBox_Unchecked(object sender, RoutedEventArgs e)
        => VideoEngineSettings.AllowCam1RecordingTest = false;
}
