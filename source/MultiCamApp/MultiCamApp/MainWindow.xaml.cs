////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MultiCamApp.Capture;
using MultiCamApp.Capture.Backend;
using MultiCamApp.Capture.VideoEngineV2;
using MultiCamApp.Recording;
using MultiCamApp.Recording.Writers;
using MultiCamApp.Ui;
using MultiCamApp.Utils;

namespace MultiCamApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly VideoEngineV2 _v2Engine = new();
    private readonly VideoEngineRegistry _backendRegistry;
    private readonly RecordingFileSet?[] _v2RecordingFileSets    = new RecordingFileSet?[4];

    private string? _v2SessionFolderPath;
    private bool _previewUiBusy;
    private bool _recordUiBusy;
    // True from the moment Stop is clicked until internal MP4/metadata finalization completes.
    // The UI presents as "stopped" immediately — this flag only prevents new recordings from
    // starting and suppresses transient status overwrites during the silent post-stop phase.
    private bool _isPostStopProcessing;
    // Elapsed time frozen at the stop-click moment; displayed during post-stop processing.
    private TimeSpan _postStopFrozenElapsed;
    private readonly Utils.StopRecordingGuard _stopRecordingGuard = new();
    private readonly System.Windows.Controls.Image[] _previewImages = new System.Windows.Controls.Image[4];
    private readonly TextBlock[] _statLabels = new TextBlock[4];
    private readonly TextBlock[] _slotLabels = new TextBlock[4];
    private readonly Border[] _cellBorders = new Border[4];
    // Saved WPF Viewbox per slot so it can be restored when GPU preview host is removed.
    private readonly Viewbox?[] _slotViewboxes = new Viewbox?[4];
    private readonly Border[] _cardBorders = new Border[4];
    private readonly TextBlock[] _headerStats = new TextBlock[4];
    private readonly System.Windows.Shapes.Ellipse[] _activeDots = new System.Windows.Shapes.Ellipse[4];
    private readonly Border[] _cells = new Border[4];
    private readonly System.Windows.Controls.ComboBox[] _deviceBoxes;
    private readonly StackPanel[] _camBlocks;
    private readonly ResponsiveLayoutManager _layoutManager = new();
    private DispatcherTimer? _elapsedTimer;
    private DispatcherTimer? _resizeTimer;
    private readonly System.Diagnostics.Stopwatch _v2RecordingStopwatch = new();
    private string _sessionLanguage = "en";
    private readonly Diagnostics.UiFreezeWatchdog _freezeWatchdog;
    // Tracks whether the user clicked Default Focus/Exposure for each camera slot.
    // When true, the manual override for that slot is cleared; old values are not re-applied at recording start.
    private readonly bool[] _focusDefaultActivePerCamera   = new bool[4];
    private readonly bool[] _exposureDefaultActivePerCamera = new bool[4];
    private int _pendingLayoutCount = 1;
    private bool _populatingVideoSettings;
    private bool _populatingDeviceBoxes;
    private bool _syncingLayoutRadios;
    private bool _syncingFocusControls;
    private bool _syncingExposureControls;
    private bool _syncingWbControls;
    private bool _envLocked;
    // Last lock result — stored so recording metadata can stamp the frozen hardware values.
    private Capture.VideoEngineV2.V2EnvironmentLockResult? _lastEnvLockResult;
    // Hardware WB bounds stored from ProbeCapabilities; used to clamp before SetValueAsync.
    private uint _wbMinK;
    private uint _wbMaxK;
    private bool _shutdownCleanupStarted;
    private bool _shutdownCleanupCompleted;
    private readonly CancellationTokenSource _windowCts = new();   // cancelled in Window_Closing
    private CancellationTokenSource? _videoSettingsReapplyCts;
    private Task? _videoSettingsReapplyTask;
    private Task _postRecordingMetadataTask = Task.CompletedTask;
    public MainWindow()
    {
        _freezeWatchdog  = new Diagnostics.UiFreezeWatchdog(Dispatcher);
        _backendRegistry = new VideoEngineRegistry(_v2Engine);
        _v2Engine.SlotFallenBackToWpf += OnV2SlotFallenBackToWpf;
        InitializeComponent();
        SessionBox.TextChanged += SessionBox_TextChanged;
        _deviceBoxes = [Cam1Box, Cam2Box, Cam3Box, Cam4Box];
        _camBlocks = [Cam1Block, Cam2Block, Cam3Block, Cam4Block];
        _vm.PreloadLanguage();
        HeaderBarControl.LoadLogo();
        if (HeaderBarControl.LogoSource != null)
            Icon = HeaderBarControl.LogoSource;
        BuildLayoutRadios();
        BuildVideoSettingsCombos();
        BuildPreviewCells();
        ApplyLayout(1);
        HeaderBarControl.LanguageChanged += OnHeaderLanguageChanged;
        HeaderBarControl.AboutRequested += OnAboutRequested;
        MainNavControl.MainSelected += (_, _) => ShowPage("main");
        MainNavControl.VerificationSelected += (_, _) => ShowPage("verification");
        MainNavControl.HardwareSelected += (_, _) => ShowPage("hardware");
        VerificationPage.Initialize(_vm.Config, _vm.Language);
        HardwareDiagnosticsPage.Initialize(_vm.Version, GetAllCamerasForDiagnostics, GetSelectedCamerasForDiagnostics, () => _vm.CurrentLayoutCount);
        RefreshTexts();
        PopulateFocusCameraTargets();
        UpdateManualFocusAvailability();
        UpdateActionButtons();
        _vm.UiRefreshRequested += OnUiRefreshRequested;
        _vm.Language.LanguageChanged += OnLanguageChanged;
        _vm.DevicesListChanged += RepopulateDevices;
        _vm.SlotDeviceSelectionCleared += OnSlotDeviceSelectionCleared;
        _vm.CameraAccessDenied += OnCameraAccessDenied;
        _vm.UiDispatcher = Dispatcher;
        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => _freezeWatchdog.Dispose();
    }

    private async Task InitAsync()
    {
        try
        {
            await _vm.InitializeAsync();
            OutputBox.Text = _vm.OutputFolderDisplay;
            PopulateDevices();
            HeaderBarControl.SyncLanguage(_vm.Language.CurrentLanguage);
            RefreshTexts();
            FontHelper.ApplyLanguageFont(RecordingPagePanel, _vm.Language.CurrentLanguage);
            VerificationPage.ApplyLanguage();
            HardwareDiagnosticsPage.Initialize(_vm.Version, GetAllCamerasForDiagnostics, GetSelectedCamerasForDiagnostics, () => _vm.CurrentLayoutCount);
            ApplyLayout(_vm.CurrentLayoutCount > 0 ? _vm.CurrentLayoutCount : 1);
            UpdateActionButtons();
        }
        catch (Exception ex)
        {
            StatusValue.Text = ex.Message;
            RefreshTexts();
        }
        _freezeWatchdog.CurrentAppState = "Initialising";
        _freezeWatchdog.Start();
        // V2 device enumeration — runs silently; no camera is opened yet
        await InitV2EngineAsync();
    }

    private void OnCameraAccessDenied()
    {
        var L = _vm.Language;
        var result = MessageBox.Show(
            L["cameraAccessBlocked"],
            L["appTitle"],
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.OK)
            CameraAccessHelper.OpenWindowsCameraPrivacySettings();
    }

    private void BuildVideoSettingsCombos()
    {
        ResolutionBox.Items.Clear();
        ResolutionBox.Items.Add(new ComboBoxItem { Content = CaptureResolutionPreset.Label360, Tag = CaptureResolutionPreset.Label360 });
        ResolutionBox.Items.Add(new ComboBoxItem { Content = CaptureResolutionPreset.Label720, Tag = CaptureResolutionPreset.Label720 });
        ResolutionBox.Items.Add(new ComboBoxItem { Content = CaptureResolutionPreset.Label1080, Tag = CaptureResolutionPreset.Label1080 });
        ResolutionBox.SelectedIndex = 1; // default: 720p

        FpsBox.Items.Clear();
        foreach (var fps in new[] { 15, 30, 60 })
            FpsBox.Items.Add(new ComboBoxItem { Content = $"{fps} fps", Tag = fps });
        FpsBox.SelectedIndex = 1; // default: 30 fps
    }

    private void ApplyVideoSettingsToViewModel()
    {
        if (_populatingVideoSettings) return;

        var w = 0;
        var h = 0;
        if (ResolutionBox.SelectedItem is ComboBoxItem resItem && resItem.Tag is string resTag)
            CaptureResolutionPreset.TryFromLabel(resTag, out w, out h);

        var fps = 30.0;
        if (FpsBox.SelectedItem is ComboBoxItem fpsItem && fpsItem.Tag is int f)
            fps = f;
        else if (FpsBox.SelectedItem is ComboBoxItem fpsItem2 && fpsItem2.Tag is double fd)
            fps = fd;

        _vm.ApplyCaptureSettings(w, h, fps);
        // ReapplyFocus/Exposure always enabled internally — no longer driven by checkbox.
        _vm.Config.ReapplyFocusBeforeRecording = true;
        _vm.Config.ReapplyExposureBeforeRecording = true;
        if (double.TryParse(ManualFocusValueBox.Text, out var manualFocus))
            _vm.Config.ManualFocusValue = manualFocus;
        _vm.Config.AutoExposureEnabled = AdvancedAutoExposureCheckBox.IsChecked == true;
        // Disable Low-Light Compensation always on internally — checkbox removed from UI (it was
        // structurally dependent on exposure control, which every camera tested reported as
        // unsupported, making the toggle a no-op in practice).
        _vm.Config.DisableLowLightCompensation = true;
        if (double.TryParse(ManualExposureValueBox.Text, out var manualExposure))
            _vm.Config.ManualExposureValue = manualExposure;
    }

    private void SyncVideoSettingsFromConfig()
    {
        _populatingVideoSettings = true;
        try
        {
            var w = _vm.Config.PreferredCaptureWidth;
            var h = _vm.Config.PreferredCaptureHeight;
            var resTag = CaptureResolutionPreset.ToLabel(w, h);
            if (string.IsNullOrEmpty(resTag)) resTag = "0x0";
            for (var i = 0; i < ResolutionBox.Items.Count; i++)
            {
                if (ResolutionBox.Items[i] is ComboBoxItem ci && (string?)ci.Tag == resTag)
                {
                    ResolutionBox.SelectedIndex = i;
                    break;
                }
            }

            var targetFps = (int)Math.Round(_vm.Config.PreferFps);
            for (var i = 0; i < FpsBox.Items.Count; i++)
            {
                if (FpsBox.Items[i] is ComboBoxItem ci && ci.Tag is int f && f == targetFps)
                {
                    FpsBox.SelectedIndex = i;
                    break;
                }
            }

            // HighStabilityRecordingMode always on internally — checkbox removed from UI.
            _vm.Config.HighStabilityRecordingMode = true;
            SyncFocusControlsFromConfig();
            SyncExposureControlsFromConfig();
        }
        finally
        {
            _populatingVideoSettings = false;
        }
    }

    private void BuildLayoutRadios()
    {
        var keys = new[] { "layout1", "layout2", "layout3", "layout4" };
        for (var i = 0; i < 4; i++)
        {
            var n = i + 1;
            var rb = new RadioButton
            {
                Tag = n,
                IsChecked = n == 1,
                Style = (Style)FindResource("LayoutRadioStyle"),
                Content = _vm.Language.Get(keys[i])
            };
            rb.Checked += (_, _) => { if (!_syncingLayoutRadios && rb.IsChecked == true) ApplyLayout(n); };
            LayoutPanel.Children.Add(rb);
        }
    }

    private void BuildPreviewCells()
    {
        for (var i = 0; i < 4; i++)
        {
            var img = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality);

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = img,
                Margin = new Thickness(6)
            };

            var videoBorder = new Border
            {
                Background = PreviewPanelTheme.CardBackground,
                BorderBrush = PreviewPanelTheme.InnerVideoBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Child = viewbox,
                Margin = new Thickness(6, 0, 6, 0)
            };

            var slotLabel = new TextBlock
            {
                Text = PreviewPanelTheme.SlotLabel(i),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = PreviewPanelTheme.GetCamAccentBrush(i),
                VerticalAlignment = VerticalAlignment.Center
            };

            var stats = new TextBlock
            {
                Foreground = PreviewPanelTheme.StatsForeground,
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = PreviewPanelTheme.ActiveDot,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            var headerGrid = new Grid { Background = PreviewPanelTheme.HeaderBarBackground, MinHeight = 32 };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(slotLabel, 0);
            Grid.SetColumn(stats, 1);
            Grid.SetColumn(dot, 3);
            slotLabel.Margin = new Thickness(10, 8, 8, 8);
            stats.Margin = new Thickness(0, 8, 4, 8);
            headerGrid.Children.Add(slotLabel);
            headerGrid.Children.Add(stats);
            headerGrid.Children.Add(dot);

            var cardInner = new Grid();
            cardInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(headerGrid, 0);
            Grid.SetRow(videoBorder, 1);
            cardInner.Children.Add(headerGrid);
            cardInner.Children.Add(videoBorder);

            var cardBorder = new Border
            {
                Background = PreviewPanelTheme.CardBackground,
                BorderBrush = PreviewPanelTheme.GetCamBorderBrush(i),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Child = cardInner
            };

            _previewImages[i] = img;
            _statLabels[i] = stats;
            _slotLabels[i] = slotLabel;
            _cellBorders[i] = videoBorder;
            _slotViewboxes[i] = viewbox;
            _cardBorders[i] = cardBorder;
            _headerStats[i] = stats;
            _activeDots[i] = dot;
            _cells[i] = cardBorder;
        }
    }

    private void ApplyPreviewPanelChrome(int cameraCount)
    {
        LayoutHost.Background = PreviewPanelTheme.HostBackground;

        for (var i = 0; i < 4; i++)
        {
            _cells[i].Margin = i < cameraCount ? new Thickness(6) : new Thickness(0);
            if (i >= cameraCount) continue;

            _cardBorders[i].BorderBrush = PreviewPanelTheme.GetCamBorderBrush(i);
            _slotLabels[i].Foreground = PreviewPanelTheme.GetCamAccentBrush(i);
        }
    }

    private async void ApplyLayout(int count)
    {
        try
        {
            await ApplyLayoutCoreAsync(count);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Failure("MainWindow", "ApplyLayout failed", ex);
        }
    }

    private async Task ApplyLayoutCoreAsync(int count)
    {
        var prev = _vm.CurrentLayoutCount;
        if (count != prev
            && _vm.BlockPreviewMutation(
                "LAYOUT_CHANGE_BLOCKED_PREVIEW_ACTIVE",
                "Stop preview before changing layout or camera selection."))
        {
            SyncLayoutRadioSelection(prev);
            UpdateActionButtons();
            return;
        }

        var wasPreviewing = _vm.State.RunState == Core.AppRunState.Previewing;

        _pendingLayoutCount = count;
        _vm.SetLayout(count);
        _vm.AssignDistinctDevicesForLayout();
        RepopulateDevices();
        _layoutManager.Apply(LayoutHost, _cells.Cast<UIElement>().ToList(), count,
            LayoutHost.ActualWidth > 0 ? LayoutHost.ActualWidth : 800,
            LayoutHost.ActualHeight > 0 ? LayoutHost.ActualHeight : 600);
        ApplyPreviewPanelChrome(count);

        for (var i = 0; i < 4; i++)
            _camBlocks[i].Visibility = i < count ? Visibility.Visible : Visibility.Collapsed;

        if (count < prev)
            await _vm.CloseInactiveLayoutSlotsAsync();

        if (wasPreviewing)
        {
            if (count > prev)
                await _vm.ExtendPreviewForActiveLayoutAsync(i => _previewImages[i]);
        }

        if (count >= 2)
        {
            _populatingVideoSettings = true;
            try
            {
                AdvancedAutoFocusCheckBox.IsChecked = false;
                _vm.Config.AutoFocusEnabled = false;
            }
            finally
            {
                _populatingVideoSettings = false;
            }
        }

        RefreshTexts();
        UpdateActionButtons();
    }

    private void SyncLayoutRadioSelection(int count)
    {
        _syncingLayoutRadios = true;
        try
        {
            foreach (var child in LayoutPanel.Children)
            {
                if (child is RadioButton rb && rb.Tag is int n)
                    rb.IsChecked = n == count;
            }
        }
        finally
        {
            _syncingLayoutRadios = false;
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_vm.Config.EnableResponsiveUi) return;
        _resizeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _resizeTimer.Tick -= ResizeTimer_Tick;
        _resizeTimer.Tick += ResizeTimer_Tick;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ResizeTimer_Tick(object? sender, EventArgs e)
    {
        _resizeTimer?.Stop();
        _layoutManager.Apply(LayoutHost, _cells.Cast<UIElement>().ToList(), _pendingLayoutCount,
            LayoutHost.ActualWidth, LayoutHost.ActualHeight);
    }

    private async void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            await _vm.OnMinimizedAsync();
        else if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
        {
            await _vm.OnRestoredAsync();
            _vm.UpdateRecordingTitle(t => Title = t);
        }
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownCleanupCompleted)
            return;

        if (_shutdownCleanupStarted)
        {
            e.Cancel = true;
            return;
        }

        // Cancel any background async operations (e.g. device enumeration) tied to the window.
        try { _windowCts.Cancel(); } catch { }

        if (_vm.State.IsRecording)
        {
            var result = MessageBox.Show(
                "Recording is active. Stop recording and close the app?",
                "MultiCamApp",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        e.Cancel = true;
        _shutdownCleanupStarted = true;
        IsEnabled = false;
        await CleanupForCloseAsync(forceShutdown: true).ConfigureAwait(true);
        _shutdownCleanupCompleted = true;
        Close();
    }

    private void PopulateDevices() => RepopulateDevices();

    private void RepopulateDevices()
    {
        _populatingDeviceBoxes = true;
        try
        {
            for (var i = 0; i < 4; i++)
                SetDeviceComboSelection(i, _vm.SelectedDeviceIds[i]);
        }
        finally
        {
            _populatingDeviceBoxes = false;
        }

        UpdateActionButtons();
        PopulateFocusCameraTargets();
        UpdateManualFocusAvailability();
    }

    private void SetDeviceComboSelection(int slotIndex, string? deviceId)
    {
        var box = _deviceBoxes[slotIndex];
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = _vm.Language["none"], Tag = null });
        foreach (var d in _vm.Devices)
            box.Items.Add(new ComboBoxItem { Content = d.DisplayName, Tag = d.Id });

        var idx = 0;
        if (!string.IsNullOrEmpty(deviceId))
        {
            var found = false;
            for (var j = 0; j < box.Items.Count; j++)
            {
                if (box.Items[j] is ComboBoxItem ci && (string?)ci.Tag == deviceId)
                {
                    idx = j;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var label = _vm.GetUnavailableDeviceLabel(deviceId);
                box.Items.Add(new ComboBoxItem { Content = label, Tag = deviceId, IsEnabled = false });
                idx = box.Items.Count - 1;
            }
        }

        box.SelectedIndex = idx;
        _vm.SelectedDeviceIds[slotIndex] = deviceId;
    }

    private void OnSlotDeviceSelectionCleared(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 4) return;
        _populatingDeviceBoxes = true;
        try
        {
            SetDeviceComboSelection(slotIndex, null);
            if (_previewImages[slotIndex] is { } img)
                img.Source = null;
        }
        finally
        {
            _populatingDeviceBoxes = false;
        }

        UpdateActionButtons();
        UpdatePreviewOverlayStats();
    }

    private void SyncDeviceSelection(int slotIndex)
    {
        if (_deviceBoxes[slotIndex].SelectedItem is ComboBoxItem ci)
            _vm.SelectedDeviceIds[slotIndex] = ci.Tag as string;
    }

    private void OnUiRefreshRequested()
    {
        RefreshTexts();
        UpdatePreviewOverlayStats();
        UpdateStatusDashboard();
        UpdateActionButtons();
    }

    private void OnLanguageChanged()
    {
        RefreshTexts();
        FontHelper.ApplyLanguageFont(RecordingPagePanel, _vm.Language.CurrentLanguage);
        VerificationPage.ApplyLanguage();
        HardwareDiagnosticsPage.Initialize(_vm.Version, GetAllCamerasForDiagnostics, GetSelectedCamerasForDiagnostics, () => _vm.CurrentLayoutCount);
    }

    private void OnHeaderLanguageChanged(object? sender, string code)
    {
        if (code != _vm.Language.CurrentLanguage)
            _vm.SetLanguage(code);
    }

    private void OnAboutRequested(object? sender, EventArgs e)
    {
        // MessageBox.Show("DEBUG: About Button Clicked"); // User can uncomment to debug if needed
        try
        {
            var about = new AboutWindow();
            about.Owner = this;
            about.ApplyLanguage(_vm.Language, _vm.Version);
            about.ShowDialog();
        }
        catch (Exception ex)
        {
            var L = _vm.Language;
            MessageBox.Show(
                string.Format(L["dialogAboutOpenError"], ex.Message),
                L["dialogErrorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void VideoSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyVideoSettingsToViewModel();
        UpdateSessionTitleResolutionToken();
        _videoSettingsReapplyTask = ReapplyVideoSettingsAfterChangeAsync();
    }

    private void AdvancedAutoFocusCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingFocusControls) return;
        var enabled = AdvancedAutoFocusCheckBox.IsChecked == true;
        var slot = GetCurrentFocusTargetSlot();
        if (slot >= 0) _vm.Config.AutoFocusEnabledPerCamera[slot] = enabled;
        else _vm.Config.AutoFocusEnabled = enabled;
        UpdateManualFocusAvailability();
    }

    private async void ApplyFocusSettingButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyVideoSettingsToViewModel();
        ApplyFocusSettingButton.IsEnabled = false;
        var slot = FocusCameraTargetBox.SelectedItem is ComboBoxItem item && item.Tag is int si ? si : -1;
        var deviceName = slot >= 0 ? _vm.GetCameraSlotLabel(slot) : "all";
        var autoFocus = slot >= 0 ? _vm.Config.AutoFocusEnabledPerCamera[slot] : _vm.Config.AutoFocusEnabled;
        var manualVal = slot >= 0 ? _vm.Config.ManualFocusValuesPerCamera[slot] : _vm.Config.ManualFocusValue;
        AppDiagnosticLogger.Runtime(
            $"APPLY_FOCUS_CLICK slot={slot} device=\"{deviceName}\" autoFocus={autoFocus} manualValue={manualVal}");
        try
        {
            var target = slot >= 0 ? slot : (int?)null;
            if (target is >= 0 and < 4)
                _focusDefaultActivePerCamera[target.Value] = false;
            var results = await _vm.ApplyFocusSettingsAsync(target);
            foreach (var r in results)
                AppDiagnosticLogger.Runtime(
                    $"APPLY_FOCUS_RESULT slot={slot} device=\"{deviceName}\" " +
                    $"autoReq={r.AutoFocusRequested} attempted={r.AutoFocusApplyAttempted} succeeded={r.AutoFocusApplySucceeded} " +
                    $"mode={r.FocusControlMode} readback={r.AutoFocusReadbackValue} warn={r.FocusWarning}");
            UpdateManualFocusAvailability();
            RefreshTexts();
            UpdateActionButtons();
        }
        finally
        {
            ApplyFocusSettingButton.IsEnabled = true;
        }
    }

    private void FocusCameraTargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadPerCameraFocusToUi();
        LoadPerCameraExposureToUi();
        UpdateCameraControlButtonLabels();
        UpdateManualFocusAvailability();
        UpdateManualExposureAvailability();
        _ = ProbeAndShowCameraCapabilitiesAsync();
    }

    private async Task ProbeAndShowCameraCapabilitiesAsync()
    {
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) return;

        // Skip if the slot's camera isn't actually open — this handler fires on every camera
        // target selector change, including during layout transitions where slot 0's camera can
        // be briefly mid-teardown/reopen. Probing then threw "The object has been closed" 58
        // times across a 19-session test batch (always slot 0); the exception was already caught
        // and harmless (cosmetic-only: it only affects displayed exposure/focus range text), but
        // checking pipeline state first avoids the exception — and the log noise — entirely.
        var state = _v2Engine.GetSlotPipelineState(slot);
        if (state is not (CameraPipelineState.Previewing or CameraPipelineState.Recording))
            return;

        try
        {
            var cap = await Task.Run(() => _v2Engine.GetSlotCapabilities(slot));
            if (cap is null || cap.NotAttached) return;

            if (cap.ExposureSupported && cap.ExposureMaxS > 0)
            {
                double minMs  = cap.ExposureMinS  * 1000.0;
                double maxMs  = cap.ExposureMaxS  * 1000.0;
                double curMs  = cap.ExposureCurrentS * 1000.0;
                var L = _vm.Language;
                string rangeStr = string.Format(L["exposureRangeTemplate"], minMs.ToString("F1"), maxMs.ToString("F0"));
                if (cap.ExposureCurrentS > 0)
                    rangeStr += string.Format(L["exposureCurrentSuffix"], curMs.ToString("F1"));
                ExposureStatusText.Text = rangeStr;
                AppDiagnosticLogger.Runtime(
                    $"CAPABILITY_PROBE slot={slot} expMinMs={minMs:F1} expMaxMs={maxMs:F0} expCurMs={curMs:F1}");
            }
            else if (!cap.ExposureSupported)
            {
                ExposureStatusText.Text = _vm.Language["exposureNotSupported"];
                AppDiagnosticLogger.Runtime($"CAPABILITY_PROBE slot={slot} exposure=Unsupported");
            }

            if (!cap.FocusSupported)
            {
                FocusRestoreStatusText.Text = _vm.Language["focusNotSupported"];
                AppDiagnosticLogger.Runtime($"CAPABILITY_PROBE slot={slot} focus=Unsupported");
            }
            else if (cap.FocusMax > 0)
            {
                FocusRestoreStatusText.Text = string.Format(_vm.Language["focusSupportedRangeTemplate"], cap.FocusMin, cap.FocusMax, cap.FocusCurrent);
                AppDiagnosticLogger.Runtime(
                    $"CAPABILITY_PROBE slot={slot} focMin={cap.FocusMin} focMax={cap.FocusMax} focCur={cap.FocusCurrent}");
            }

            // Update slider ranges and show/hide WB panel from live hardware capabilities.
            InitCameraControlSlidersFromCapability(cap);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"CAPABILITY_PROBE_ERROR slot={slot} {ex.Message}");
        }
    }

    /// <summary>
    /// Populates hardware-accurate slider ranges and shows/hides the WB panel based on
    /// what the open camera driver actually supports. Call after ProbeCapabilities returns.
    /// </summary>
    private void InitCameraControlSlidersFromCapability(Capture.VideoEngineV2.V2CameraCapabilitySnapshot cap)
    {
        // Focus: update slider range to driver-reported steps (not hardcoded 0-255)
        if (cap.FocusSupported && cap.FocusMax > cap.FocusMin)
        {
            _syncingFocusControls = true;
            try
            {
                ManualFocusSlider.Minimum       = cap.FocusMin;
                ManualFocusSlider.Maximum       = cap.FocusMax;
                ManualFocusSlider.TickFrequency = Math.Max(1, cap.FocusStep);
                // Clamp current slider value into new range
                ManualFocusSlider.Value = Math.Clamp(ManualFocusSlider.Value,
                                                     cap.FocusMin, cap.FocusMax);
                ManualFocusValueBox.Text = ManualFocusSlider.Value.ToString("F0");
            }
            finally { _syncingFocusControls = false; }
        }

        // White balance: show/hide panel, set K range from hardware
        if (cap.WhiteBalanceSupported && cap.WhiteBalanceMaxK > cap.WhiteBalanceMinK)
        {
            // Cache hardware bounds — used for explicit clamp in WbSlider_CommitValue.
            _wbMinK = cap.WhiteBalanceMinK;
            _wbMaxK = cap.WhiteBalanceMaxK;

            WbCalibrationPanel.Visibility = System.Windows.Visibility.Visible;
            _syncingWbControls = true;
            try
            {
                WbSlider.Minimum       = cap.WhiteBalanceMinK;
                WbSlider.Maximum       = cap.WhiteBalanceMaxK;
                WbSlider.TickFrequency = Math.Max(100, cap.WhiteBalanceStepK);
                if (cap.WhiteBalanceCurrentK >= cap.WhiteBalanceMinK)
                {
                    WbSlider.Value   = Math.Clamp(cap.WhiteBalanceCurrentK,
                                                  cap.WhiteBalanceMinK, cap.WhiteBalanceMaxK);
                    WbValueBox.Text  = cap.WhiteBalanceCurrentK.ToString();
                }
            }
            finally { _syncingWbControls = false; }
            WbStatusText.Text = string.Format(_vm.Language["wbRangeTemplate"], cap.WhiteBalanceMinK, cap.WhiteBalanceMaxK) +
                                (cap.WhiteBalanceCurrentK > 0 ? string.Format(_vm.Language["wbCurrentSuffix"], cap.WhiteBalanceCurrentK) : "");
        }
        else
        {
            _wbMinK = 0;
            _wbMaxK = 0;
            WbCalibrationPanel.Visibility = System.Windows.Visibility.Collapsed;
        }
    }

    private async void LockEnvironmentButton_Click(object sender, RoutedEventArgs e)
    {
        var L = _vm.Language;
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) { CalibrationStatusLabel.Text = L["calibSelectCameraFirst"]; return; }

        LockEnvironmentButton.IsEnabled    = false;
        OneShotCalibrateButton.IsEnabled   = false;
        CalibrationStatusLabel.Text        = L["calibFreezingRegisters"];

        try
        {
            if (_envLocked)
            {
                // Unlock: restore auto exposure, WB, and ISO gain adaptive tracking.
                await _v2Engine.ReleaseSlotEnvironmentalLockAsync(slot);
                _envLocked = false;
                _lastEnvLockResult = null;
                WbSlider.IsEnabled   = false;
                WbValueBox.IsEnabled = false;
                CalibrationStatusLabel.Text   = L["calibEnvLockReleased"];
                LockEnvironmentButton.Content = L["lockEnvironmentalSettings"];
                AppDiagnosticLogger.Runtime($"ENV_LOCK_RELEASE_UI slot={slot}");
                return;
            }

            var result = await _v2Engine.ExecuteSlotEnvironmentalLockAsync(slot);
            if (result is null) { CalibrationStatusLabel.Text = L["calibCameraNotOpen"]; return; }

            // Reflect frozen values back into sliders
            if (result.FocusLocked && result.FocusLockedAt > 0)
            {
                _syncingFocusControls = true;
                try
                {
                    ManualFocusSlider.Value = Math.Clamp(result.FocusLockedAt,
                                                         ManualFocusSlider.Minimum, ManualFocusSlider.Maximum);
                    ManualFocusValueBox.Text = result.FocusLockedAt.ToString();
                }
                finally { _syncingFocusControls = false; }
            }

            if (result.WhiteBalanceLocked && result.WhiteBalanceLockedAtK > 0)
            {
                _syncingWbControls = true;
                try
                {
                    WbSlider.Value   = Math.Clamp(result.WhiteBalanceLockedAtK,
                                                  WbSlider.Minimum, WbSlider.Maximum);
                    WbValueBox.Text  = result.WhiteBalanceLockedAtK.ToString();
                    WbSlider.IsEnabled   = true;
                    WbValueBox.IsEnabled = true;
                }
                finally { _syncingWbControls = false; }
            }

            _envLocked = true;
            _lastEnvLockResult = result;
            var sb = new System.Text.StringBuilder(L["calibEnvironmentLocked"]);
            if (result.FocusLocked)        sb.Append($"  Focus={result.FocusLockedAt}steps");
            if (result.ExposureLocked)     sb.Append($"  Exp={(result.ExposureLockedAtS * 1000):F1}ms");
            if (result.WhiteBalanceLocked) sb.Append($"  WB={result.WhiteBalanceLockedAtK}K");
            if (result.IsoLocked)          sb.Append("  ISO=locked");
            if (result.Warning is not null) sb.Append($"  [{result.Warning}]");
            CalibrationStatusLabel.Text   = sb.ToString();
            LockEnvironmentButton.Content = L["unlockSettings"];
        }
        catch (Exception ex)
        {
            CalibrationStatusLabel.Text = string.Format(L["calibLockFailed"], ex.Message);
            AppDiagnosticLogger.Runtime($"ENV_LOCK_ERROR slot={slot} {ex.Message}");
        }
        finally
        {
            LockEnvironmentButton.IsEnabled  = true;
            OneShotCalibrateButton.IsEnabled = true;
        }
    }

    private async void OneShotCalibrateButton_Click(object sender, RoutedEventArgs e)
    {
        var L = _vm.Language;
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) { CalibrationStatusLabel.Text = L["calibSelectCameraFirst"]; return; }

        LockEnvironmentButton.IsEnabled  = false;
        OneShotCalibrateButton.IsEnabled = false;
        CalibrationStatusLabel.Text      = L["calibOneShotWaiting"];

        try
        {
            var result = await _v2Engine.OneShotCalibrateSlotAsync(slot);
            if (result is null) { CalibrationStatusLabel.Text = L["calibCameraNotOpen"]; return; }

            if (result.FocusLocked && result.FocusLockedAt > 0)
            {
                _syncingFocusControls = true;
                try
                {
                    ManualFocusSlider.Value = Math.Clamp(result.FocusLockedAt,
                                                         ManualFocusSlider.Minimum, ManualFocusSlider.Maximum);
                    ManualFocusValueBox.Text = result.FocusLockedAt.ToString();
                }
                finally { _syncingFocusControls = false; }
            }

            if (result.WhiteBalanceLocked && result.WhiteBalanceLockedAtK > 0)
            {
                _syncingWbControls = true;
                try
                {
                    WbSlider.Value   = Math.Clamp(result.WhiteBalanceLockedAtK,
                                                  WbSlider.Minimum, WbSlider.Maximum);
                    WbValueBox.Text  = result.WhiteBalanceLockedAtK.ToString();
                    WbSlider.IsEnabled   = true;
                    WbValueBox.IsEnabled = true;
                }
                finally { _syncingWbControls = false; }
            }

            _envLocked = true;
            _lastEnvLockResult = result;
            var sb = new System.Text.StringBuilder(L["calibOneShotComplete"]);
            if (result.ExposureLocked)     sb.Append($"  Exp={(result.ExposureLockedAtS * 1000):F1}ms");
            if (result.WhiteBalanceLocked) sb.Append($"  WB={result.WhiteBalanceLockedAtK}K");
            if (result.IsoLocked)          sb.Append("  ISO=locked");
            if (result.Warning is not null) sb.Append($"  [{result.Warning}]");
            CalibrationStatusLabel.Text   = sb.ToString();
            LockEnvironmentButton.Content = L["unlockSettings"];
        }
        catch (OperationCanceledException)
        {
            CalibrationStatusLabel.Text = L["calibOneShotCancelled"];
        }
        catch (Exception ex)
        {
            CalibrationStatusLabel.Text = string.Format(L["calibOneShotFailed"], ex.Message);
            AppDiagnosticLogger.Runtime($"ONE_SHOT_ERROR slot={slot} {ex.Message}");
        }
        finally
        {
            LockEnvironmentButton.IsEnabled  = true;
            OneShotCalibrateButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Fires when the WB slider thumb is released (PointerCaptureLost / LostFocus).
    /// Debounced: only one hardware SetValueAsync call per user gesture, not 60/s.
    /// </summary>
    private async void WbSlider_CommitValue(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_syncingWbControls) return;
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0 || !_envLocked) return;

        // Round to nearest 100K then clamp against hardware-reported bounds (_wbMinK/_wbMaxK).
        // Some webcam drivers throw a hardware exception if SetValueAsync receives even 1K
        // outside the supported range, so this guard is non-negotiable.
        uint kelvins = (uint)(Math.Round(WbSlider.Value / 100.0) * 100.0);
        if (_wbMaxK > _wbMinK)
            kelvins = Math.Clamp(kelvins, _wbMinK, _wbMaxK);
        _syncingWbControls = true;
        try { WbValueBox.Text = kelvins.ToString(); }
        finally { _syncingWbControls = false; }

        try
        {
            var result = await _v2Engine.SetSlotWhiteBalanceManualAsync(slot, kelvins);
            WbStatusText.Text = result?.Applied == true
                ? string.Format(_vm.Language["wbSetToTemplate"], kelvins)
                : string.Format(_vm.Language["wbSetFailedTemplate"], result?.WarningMessage);
        }
        catch (Exception ex)
        {
            WbStatusText.Text = string.Format(_vm.Language["wbErrorTemplate"], ex.Message);
            AppDiagnosticLogger.Runtime($"WB_SLIDER_ERROR slot={slot} {ex.Message}");
        }
    }

    private void WbValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingWbControls) return;
        if (!uint.TryParse(WbValueBox.Text, out var k)) return;
        k = (uint)Math.Clamp(k, (uint)WbSlider.Minimum, (uint)WbSlider.Maximum);
        _syncingWbControls = true;
        try { WbSlider.Value = k; }
        finally { _syncingWbControls = false; }
    }

    private void ManualFocusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingFocusControls) return;
        _syncingFocusControls = true;
        try
        {
            ManualFocusValueBox.Text = ManualFocusSlider.Value.ToString("F0");
            var slot = GetCurrentFocusTargetSlot();
            if (slot >= 0) _vm.Config.ManualFocusValuesPerCamera[slot] = ManualFocusSlider.Value;
            else _vm.Config.ManualFocusValue = ManualFocusSlider.Value;
        }
        finally
        {
            _syncingFocusControls = false;
        }
    }

    private void ManualFocusValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFocusControls) return;
        if (!double.TryParse(ManualFocusValueBox.Text, out var value))
            return;

        value = Math.Clamp(value, ManualFocusSlider.Minimum, ManualFocusSlider.Maximum);
        _syncingFocusControls = true;
        try
        {
            ManualFocusSlider.Value = value;
            var slot = GetCurrentFocusTargetSlot();
            if (slot >= 0) _vm.Config.ManualFocusValuesPerCamera[slot] = value;
            else _vm.Config.ManualFocusValue = value;
        }
        finally
        {
            _syncingFocusControls = false;
        }
    }

    private void SyncFocusControlsFromConfig()
    {
        _syncingFocusControls = true;
        try
        {
            AdvancedAutoFocusCheckBox.IsChecked = _vm.Config.AutoFocusEnabled;
        }
        finally
        {
            _syncingFocusControls = false;
        }

        PopulateFocusCameraTargets();
        LoadPerCameraFocusToUi();
        LoadPerCameraExposureToUi();
        UpdateCameraControlButtonLabels();
        UpdateManualFocusAvailability();
    }

    private int GetCurrentFocusTargetSlot() =>
        FocusCameraTargetBox.SelectedItem is ComboBoxItem item && item.Tag is int slot && slot >= 0 ? slot : -1;

    private void PopulateFocusCameraTargets()
    {
        var selectedTag = FocusCameraTargetBox.SelectedItem is ComboBoxItem sel && sel.Tag is int tag ? tag : 0;
        FocusCameraTargetBox.Items.Clear();
        var layout = _vm.State.CameraLayout;
        for (var i = 0; i < layout; i++)
        {
            if (string.IsNullOrEmpty(_vm.SelectedDeviceIds[i])) continue;
            FocusCameraTargetBox.Items.Add(new ComboBoxItem { Content = _vm.GetCameraSlotLabel(i), Tag = i });
        }
        if (FocusCameraTargetBox.Items.Count == 0)
            FocusCameraTargetBox.Items.Add(new ComboBoxItem { Content = _vm.GetCameraSlotLabel(0), Tag = 0 });
        // Restore previously selected slot if still available; otherwise default to first.
        var found = false;
        for (var j = 0; j < FocusCameraTargetBox.Items.Count; j++)
        {
            if (FocusCameraTargetBox.Items[j] is ComboBoxItem ci && ci.Tag is int t && t == selectedTag)
            {
                FocusCameraTargetBox.SelectedIndex = j;
                found = true;
                break;
            }
        }
        if (!found) FocusCameraTargetBox.SelectedIndex = 0;
    }

    private void LoadPerCameraFocusToUi()
    {
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) return;
        _syncingFocusControls = true;
        try
        {
            AdvancedAutoFocusCheckBox.IsChecked = _vm.Config.AutoFocusEnabledPerCamera[slot];
            _vm.Config.ReapplyFocusBeforeRecordingPerCamera[slot] = true; // always on
            var val = _vm.Config.ManualFocusValuesPerCamera[slot] ?? ManualFocusSlider.Value;
            ManualFocusSlider.Value = Math.Clamp(val, ManualFocusSlider.Minimum, ManualFocusSlider.Maximum);
            ManualFocusValueBox.Text = ManualFocusSlider.Value.ToString("F0");
        }
        finally { _syncingFocusControls = false; }
        UpdateManualFocusAvailability();
    }

    private void LoadPerCameraExposureToUi()
    {
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) return;
        _syncingExposureControls = true;
        try
        {
            AdvancedAutoExposureCheckBox.IsChecked = _vm.Config.AutoExposureEnabledPerCamera[slot];
            _vm.Config.DisableLowLightCompensationPerCamera[slot] = true; // always on — checkbox removed from UI
            _vm.Config.ReapplyExposureBeforeRecordingPerCamera[slot] = true; // always on
            var val = _vm.Config.ManualExposureValuesPerCamera[slot] ?? ManualExposureSlider.Value;
            ManualExposureSlider.Value = Math.Clamp(val, ManualExposureSlider.Minimum, ManualExposureSlider.Maximum);
            ManualExposureValueBox.Text = ManualExposureSlider.Value.ToString("F0");
        }
        finally { _syncingExposureControls = false; }
        UpdateManualExposureAvailability();
    }

    private void UpdateCameraControlButtonLabels()
    {
        var slot = GetCurrentFocusTargetSlot();
        var L = _vm.Language;
        var camLabel = slot >= 0 ? $"CAM{slot + 1}" : L["allCamerasLabel"];
        if (ApplyFocusSettingButton != null)
            ApplyFocusSettingButton.Content = string.Format(L["applyFocusToCam"], camLabel);
        if (ApplyExposureSettingButton != null)
            ApplyExposureSettingButton.Content = string.Format(L["applyExposureToCam"], camLabel);
        if (DefaultFocusButton != null)
            DefaultFocusButton.Content = L["defaultFocusButton"];
        if (DefaultExposureButton != null)
            DefaultExposureButton.Content = L["defaultExposureButton"];
    }

    private void UpdateManualFocusAvailability()
    {
        if (ManualFocusAvailabilityText == null) return;

        var target = FocusCameraTargetBox.SelectedItem is ComboBoxItem item && item.Tag is int slotIndex && slotIndex >= 0
            ? slotIndex
            : (int?)null;
        var status = target.HasValue ? _vm.GetFocusStatus(target.Value) : null;
        var autoEnabled = AdvancedAutoFocusCheckBox.IsChecked == true;
        var manualAvailable = !autoEnabled && (status?.ManualFocusSupported != false);
        ManualFocusSlider.IsEnabled = manualAvailable;
        ManualFocusValueBox.IsEnabled = manualAvailable;
        var L = _vm.Language;
        ManualFocusAvailabilityText.Text = autoEnabled
            ? L["manualFocusDisabledWhileAuto"]
            : status?.ManualFocusSupported == false
                ? L["manualFocusUnavailable"]
                : L["manualFocusBestEffort"];
    }

    private void AdvancedAutoExposureCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_syncingExposureControls) return;
        var enabled = AdvancedAutoExposureCheckBox.IsChecked == true;
        var slot = GetCurrentFocusTargetSlot();
        if (slot >= 0) _vm.Config.AutoExposureEnabledPerCamera[slot] = enabled;
        else _vm.Config.AutoExposureEnabled = enabled;
        UpdateManualExposureAvailability();
    }

    private async void ApplyExposureSettingButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyVideoSettingsToViewModel();
        ApplyExposureSettingButton.IsEnabled = false;
        var targetSlot = GetCurrentFocusTargetSlot();
        var deviceName = targetSlot >= 0 ? _vm.GetCameraSlotLabel(targetSlot) : "all";
        var autoExp    = targetSlot >= 0 ? _vm.Config.AutoExposureEnabledPerCamera[targetSlot] : _vm.Config.AutoExposureEnabled;
        var manualVal  = targetSlot >= 0 ? _vm.Config.ManualExposureValuesPerCamera[targetSlot] : _vm.Config.ManualExposureValue;
        AppDiagnosticLogger.Runtime(
            $"APPLY_EXPOSURE_CLICK slot={targetSlot} device=\"{deviceName}\" autoExposure={autoExp} " +
            $"manualSlider={manualVal} (slider 0-255 maps to driver min-max via proportional mapping)");
        try
        {
            var target = targetSlot >= 0 ? targetSlot : (int?)null;
            IReadOnlyList<CameraExposureControlStatus> results = await _vm.ApplyExposureSettingsAsync(target);
            if (targetSlot >= 0 && targetSlot < 4)
                _exposureDefaultActivePerCamera[targetSlot] = false;
            foreach (var r in results)
                AppDiagnosticLogger.Runtime(
                    $"APPLY_EXPOSURE_RESULT slot={targetSlot} device=\"{deviceName}\" " +
                    $"autoReq={r.AutoExposureRequested} applied={r.AutoExposureApplySucceeded} " +
                    $"readback={r.ManualExposureReadbackValue} mode={r.ExposureControlMode} warn={r.ExposureWarning}");
            ExposureStatusText.Text = BuildExposureStatusText(results, targetSlot);
            RefreshTexts();
            UpdateActionButtons();
        }
        finally
        {
            ApplyExposureSettingButton.IsEnabled = true;
        }
    }

    private string BuildExposureStatusText(IReadOnlyList<CameraExposureControlStatus> results, int targetSlot)
    {
        var autoExposure = targetSlot >= 0
            ? _vm.Config.AutoExposureEnabledPerCamera[targetSlot]
            : _vm.Config.AutoExposureEnabled;
        var hasWarning = results.Any(r => !string.IsNullOrEmpty(r.ExposureWarning));
        var L = _vm.Language;
        if (hasWarning)
            return L["exposureWarningReadbackUnavailable"];
        return autoExposure
            ? L["exposureAutoAcceptedNoReadback"]
            : L["exposureManualSentNoReadback"];
    }

    private async void DefaultFocusButton_Click(object sender, RoutedEventArgs e)
    {
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) return;
        DefaultFocusButton.IsEnabled = false;
        FocusRestoreStatusText.Text = "";
        try
        {
            // Clear per-camera manual focus override and mark default as active.
            _vm.Config.ManualFocusValuesPerCamera[slot] = null;
            _vm.Config.AutoFocusEnabledPerCamera[slot] = false; // research default: autofocus off
            _focusDefaultActivePerCamera[slot] = true;
            LoadPerCameraFocusToUi();

            // Restore focus default via engine.
            var result = await _v2Engine.RestoreSlotFocusDefaultAsync(slot);
            var L = _vm.Language;
            FocusRestoreStatusText.Text = result.ReadbackStatus switch
            {
                Capture.VideoEngineV2.V2ControlReadbackStatus.Confirmed    => L["focusRestoredDefault"],
                Capture.VideoEngineV2.V2ControlReadbackStatus.Unsupported  => L["focusRestoreUnsupported"],
                Capture.VideoEngineV2.V2ControlReadbackStatus.Failed       => L["focusRestoreFailed"],
                Capture.VideoEngineV2.V2ControlReadbackStatus.NotAttempted => L["focusRestoreNotOpen"],
                _                                                           => L["focusRestoreAcceptedNoReadback"],
            };
            AppDiagnosticLogger.Runtime($"DEFAULT_FOCUS_RESTORE slot={slot} status={result.ReadbackStatus} applied={result.Applied}");
        }
        catch (Exception ex)
        {
            FocusRestoreStatusText.Text = string.Format(_vm.Language["focusRestoreError"], ex.Message);
            AppDiagnosticLogger.Runtime($"DEFAULT_FOCUS_RESTORE_ERROR slot={slot} {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            DefaultFocusButton.IsEnabled = true;
        }
    }

    private async void DefaultExposureButton_Click(object sender, RoutedEventArgs e)
    {
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) return;
        DefaultExposureButton.IsEnabled = false;
        ExposureStatusText.Text = "";
        try
        {
            // Clear per-camera manual exposure override and mark default as active.
            _vm.Config.ManualExposureValuesPerCamera[slot] = null;
            _vm.Config.AutoExposureEnabledPerCamera[slot] = false; // research default: auto exposure off
            _exposureDefaultActivePerCamera[slot] = true;
            LoadPerCameraExposureToUi();

            // Restore exposure default via engine.
            var result = await _v2Engine.RestoreSlotExposureDefaultAsync(slot);
            var L = _vm.Language;
            ExposureStatusText.Text = result.ReadbackStatus switch
            {
                Capture.VideoEngineV2.V2ControlReadbackStatus.Confirmed    => L["exposureRestoredDefault"],
                Capture.VideoEngineV2.V2ControlReadbackStatus.Unsupported  => L["exposureRestoreUnsupported"],
                Capture.VideoEngineV2.V2ControlReadbackStatus.Failed       => L["exposureRestoreFailed"],
                Capture.VideoEngineV2.V2ControlReadbackStatus.NotAttempted => L["exposureRestoreNotOpen"],
                _                                                           => L["exposureRestoreAcceptedNoReadback"],
            };
            AppDiagnosticLogger.Runtime($"DEFAULT_EXPOSURE_RESTORE slot={slot} status={result.ReadbackStatus} applied={result.Applied}");
        }
        catch (Exception ex)
        {
            ExposureStatusText.Text = string.Format(_vm.Language["exposureRestoreError"], ex.Message);
            AppDiagnosticLogger.Runtime($"DEFAULT_EXPOSURE_RESTORE_ERROR slot={slot} {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            DefaultExposureButton.IsEnabled = true;
        }
    }

    private void ManualExposureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingExposureControls) return;
        _syncingExposureControls = true;
        try
        {
            ManualExposureValueBox.Text = ManualExposureSlider.Value.ToString("F0");
            var slot = GetCurrentFocusTargetSlot();
            if (slot >= 0) _vm.Config.ManualExposureValuesPerCamera[slot] = ManualExposureSlider.Value;
            else _vm.Config.ManualExposureValue = ManualExposureSlider.Value;
        }
        finally
        {
            _syncingExposureControls = false;
        }
    }

    private void ManualExposureValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingExposureControls) return;
        if (!double.TryParse(ManualExposureValueBox.Text, out var value))
            return;

        value = Math.Clamp(value, ManualExposureSlider.Minimum, ManualExposureSlider.Maximum);
        _syncingExposureControls = true;
        try
        {
            ManualExposureSlider.Value = value;
            var slot = GetCurrentFocusTargetSlot();
            if (slot >= 0) _vm.Config.ManualExposureValuesPerCamera[slot] = value;
            else _vm.Config.ManualExposureValue = value;
        }
        finally
        {
            _syncingExposureControls = false;
        }
    }

    private void SyncExposureControlsFromConfig()
    {
        _syncingExposureControls = true;
        try
        {
            AdvancedAutoExposureCheckBox.IsChecked = _vm.Config.AutoExposureEnabled;
            _vm.Config.ReapplyExposureBeforeRecording = true; // always on
            _vm.Config.DisableLowLightCompensation = true; // always on — checkbox removed from UI
            var value = Math.Clamp(_vm.Config.ManualExposureValue ?? ManualExposureSlider.Value, ManualExposureSlider.Minimum, ManualExposureSlider.Maximum);
            ManualExposureSlider.Value = value;
            ManualExposureValueBox.Text = value.ToString("F0");
        }
        finally
        {
            _syncingExposureControls = false;
        }

        UpdateManualExposureAvailability();
    }

    private void UpdateManualExposureAvailability()
    {
        var autoEnabled = AdvancedAutoExposureCheckBox.IsChecked == true;
        ManualExposureSlider.IsEnabled = !autoEnabled;
        ManualExposureValueBox.IsEnabled = !autoEnabled;
    }

    private void SessionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _vm.State.SessionTitle = SessionBox.Text?.Trim() ?? "";
        UpdateSessionFolderPreview();
    }

    private void UpdateSessionFolderPreview()
    {
        var folderName = SessionFolderNameGenerator.PreviewFolderName(SessionBox.Text);
        var L = _vm.Language;
        SessionFolderPreviewValue.Text = string.Format(L["sessionFolderPreviewValue"], folderName);
    }

    private void UpdateSessionTitleResolutionToken()
    {
        if (_populatingVideoSettings || string.IsNullOrWhiteSpace(SessionBox.Text))
            return;

        if (ResolutionBox.SelectedItem is not ComboBoxItem resItem || resItem.Tag is not string resTag)
            return;

        var currentPreset = CaptureResolutionPreset.ToLabel(_vm.Config.PreferredCaptureWidth, _vm.Config.PreferredCaptureHeight);
        if (string.IsNullOrWhiteSpace(currentPreset))
            currentPreset = resTag;

        var title = SessionBox.Text;
        var updated = new System.Text.RegularExpressions.Regex(
            @"(^|[_\-\s])(1080p|720p|360p)(?=$|[_\-\s])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Replace(title, m => m.Groups[1].Value + currentPreset, 1);

        if (updated == title)
            return;

        var caret = SessionBox.CaretIndex;
        SessionBox.Text = updated;
        SessionBox.CaretIndex = Math.Min(caret, SessionBox.Text.Length);
        UpdateSessionFolderPreview();
    }

    private async Task ReapplyVideoSettingsAfterChangeAsync()
    {
        var L = _vm.Language;
        if (!_vm.IsPreviewOrRecordingActive)
        {
            VideoSettingsHint.Text = L["videoSettingsHint"];
            return;
        }

        if (_vm.State.RunState == Core.AppRunState.Recording)
        {
            VideoSettingsHint.Text = L["videoSettingsWhileRecordingHint"];
            return;
        }

        VideoSettingsHint.Text = L["videoSettingsApplying"];
        _videoSettingsReapplyCts?.Cancel();
        _videoSettingsReapplyCts?.Dispose();
        _videoSettingsReapplyCts = new CancellationTokenSource();
        var token = _videoSettingsReapplyCts.Token;

        try
        {
            await Task.Delay(350, token);
            // _vm.ReapplyCaptureSettingsToActivePreviewAsync() only touches the legacy
            // CameraSlotPipeline objects (STABLE_CORE_V1) — those are never opened when
            // VideoEngineV2 is the active engine (the default/exclusive path since v1.2.22-alpha),
            // so for every real V2 session it was a silent no-op: the resolution/FPS dropdowns
            // updated VideoEngineSettings, but the already-open V2 camera kept its original
            // format, and StartRecordingAsync reads the resolution from that already-open
            // capture format — meaning a resolution/FPS change made after Start Preview was
            // never actually applied to the recording. Confirmed via a real recording audit.
            await _vm.ReapplyCaptureSettingsToActivePreviewAsync();
            await ReapplyV2VideoSettingsToActivePreviewAsync(token);
            if (!token.IsCancellationRequested)
                VideoSettingsHint.Text = L["videoSettingsApplied"];
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer selection
        }
        catch (Exception)
        {
            VideoSettingsHint.Text = L["videoSettingsRestartHint"];
        }
    }

    /// <summary>
    /// Re-opens each currently-previewing V2 camera slot so a resolution/FPS change made via the
    /// dropdowns actually takes effect. <c>VideoEngineSettings.DefaultPreferredWidth/Height/Fps</c>
    /// were already updated by <see cref="ApplyVideoSettingsToViewModel"/> before this runs, so
    /// closing and reopening each slot (reusing the same <see cref="OpenV2SlotAsync"/> sequence
    /// Start Preview itself uses) picks up the new values via <c>CameraPipelineV2.OpenAsync</c>'s
    /// null-formatRequest fallback. Only called while merely Previewing (never Recording — the
    /// caller already guards that) and only for slots that are actually open.
    /// </summary>
    private async Task ReapplyV2VideoSettingsToActivePreviewAsync(CancellationToken token)
    {
        var layout = _vm.State.CameraLayout;
        var staggerMs = CaptureResolutionHelper.MultiCameraStaggerMs(
            layout, _vm.Config.PreferredCaptureWidth, _vm.Config.PreferredCaptureHeight);

        var activeSlots = Enumerable.Range(0, layout)
            .Where(i => _v2Engine.GetSlotPipelineState(i) == CameraPipelineState.Previewing
                     && !string.IsNullOrEmpty(_vm.SelectedDeviceIds[i]))
            .ToList();

        for (var idx = 0; idx < activeSlots.Count; idx++)
        {
            if (token.IsCancellationRequested) return;
            if (idx > 0 && staggerMs > 0)
                await Task.Delay(staggerMs, token);

            var slot = activeSlots[idx];
            var deviceId = _vm.SelectedDeviceIds[slot];
            if (string.IsNullOrEmpty(deviceId)) continue; // already filtered above; guards nullability
            try
            {
                await _v2Engine.StopSlotPreviewAsync(slot);
                await OpenV2SlotAsync(slot, deviceId);
                AppDiagnosticLogger.Runtime(
                    $"V2_SLOT_RESOLUTION_REAPPLIED slot={slot} " +
                    $"w={VideoEngineSettings.DefaultPreferredWidth} h={VideoEngineSettings.DefaultPreferredHeight} " +
                    $"fps={VideoEngineSettings.DefaultPreferredFps:F0}");
            }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Runtime(
                    $"V2_SLOT_RESOLUTION_REAPPLY_FAILED slot={slot} {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void UpdateActionButtons()
    {
        UpdateUiStateFromCurrentState();
    }

    private bool IsV2AllActiveSlotsPreviewReady()
    {
        for (var i = 0; i < _vm.State.CameraLayout; i++)
        {
            if (string.IsNullOrEmpty(_vm.SelectedDeviceIds[i])) continue;
            var state = _v2Engine.GetSlotPipelineState(i);
            if (state is not (CameraPipelineState.Previewing or CameraPipelineState.Recording))
                return false;
        }
        return true;
    }

    private void UpdateUiStateFromCurrentState()
    {
        var hasDevice = _vm.HasSelectedDeviceForActiveLayout();
        var previewing = _vm.State.RunState == Core.AppRunState.Previewing;
        var opening = _vm.PreviewStartInProgress;
        var recording = _vm.State.RunState == Core.AppRunState.Recording;
        var allPreviewReady = IsV2AllActiveSlotsPreviewReady();

        if (_previewUiBusy)
        {
            // Busy override during Start Preview click; allow Stop Preview to cancel.
            StartPreviewBtn.IsEnabled = false;
            StopPreviewBtn.IsEnabled = true;
            StartRecordBtn.IsEnabled = false;
            StopRecordBtn.IsEnabled = recording;
            RefreshDevicesBtn.IsEnabled = false;
            return;
        }

        if (_recordUiBusy)
        {
            // Starting/stopping recording: lock most buttons but keep Stop accessible.
            // If any slot has started recording the user must be able to stop it.
            StartPreviewBtn.IsEnabled = false;
            StopPreviewBtn.IsEnabled  = false;
            StartRecordBtn.IsEnabled  = false;
            StopRecordBtn.IsEnabled   = recording || _v2Engine.IsAnySlotRecording;
            RefreshDevicesBtn.IsEnabled = false;
            return;
        }

        if (_isPostStopProcessing)
        {
            // Post-stop: UI shows stopped, but new recording is blocked until internal
            // MP4/metadata finalization completes safely.
            StartPreviewBtn.IsEnabled   = false;
            StopPreviewBtn.IsEnabled    = false;
            StartRecordBtn.IsEnabled    = false;
            StopRecordBtn.IsEnabled     = false;
            RefreshDevicesBtn.IsEnabled = false;
            return;
        }

        StartPreviewBtn.IsEnabled = hasDevice && !_vm.HasDuplicateDeviceSelection()
                                        && !_vm.HasMissingDeviceInActiveLayout()
                                        && !previewing && !opening && !recording;
        // During recording we disable Stop Preview to keep workflow consistent.
        StopPreviewBtn.IsEnabled = opening || previewing;
        StartRecordBtn.IsEnabled = previewing && !opening
                                     && _vm.State.RunState == Core.AppRunState.Previewing
                                     && allPreviewReady;
        StopRecordBtn.IsEnabled = recording;
        RefreshDevicesBtn.IsEnabled = !opening;
    }

    private void UpdatePreviewOverlayStats()
    {
        var L = _vm.Language;
        var previewActive = _vm.State.RunState is Core.AppRunState.Previewing or Core.AppRunState.Recording;
        for (var i = 0; i < 4; i++)
        {
            var p = _vm.Panels[i];
            var active = i < _vm.State.CameraLayout;
            var resLine = "";
            if (previewActive && active)
            {
                if (p.Pipeline.PreviewSlotState == PreviewSlotStateKind.LostConnection
                    || p.Pipeline.Status == "Lost connection")
                    resLine = string.Format(L["previewSlotLostConnection"], p.Pipeline.SlotName);
                else if (p.Pipeline.PreviewSlotState is PreviewSlotStateKind.FailedUnsupportedPreset
                         or PreviewSlotStateKind.FailedDeviceOpen)
                    resLine = p.PreviewOpenProgress;
                else if (!string.IsNullOrWhiteSpace(p.PreviewOpenProgress))
                    resLine = p.PreviewOpenProgress;
                else if (p.Pipeline.Status is "Previewing" or "Recording")
                    resLine = $"{L["resolution"]}: {p.StatsText}   {L["fps"]}: {p.Pipeline.FpsMonitor.AverageFps:F1}";
            }

            _statLabels[i].Text = resLine;
            _activeDots[i].Fill = active && previewActive && p.Pipeline.Status is "Previewing" or "Recording"
                ? PreviewPanelTheme.ActiveDot
                : new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
        }
    }

    private (int Ready, int Required) CountV2ReadySlots()
    {
        var required = 0;
        var ready = 0;
        for (var i = 0; i < _vm.State.CameraLayout; i++)
        {
            if (string.IsNullOrEmpty(_vm.SelectedDeviceIds[i])) continue;
            required++;
            var state = _v2Engine.GetSlotPipelineState(i);
            // StartingRecording/StoppingRecording are still actively capturing — a slot mid-transition
            // between Previewing and Recording is not "not ready", it's just not fully settled yet.
            // Excluding them made "Cameras: X/4 ready" visibly dip to 0/4 then climb back to 4/4 during
            // every Start Recording click, even though nothing was actually wrong.
            if (state is CameraPipelineState.Previewing or CameraPipelineState.Recording
                or CameraPipelineState.StartingRecording or CameraPipelineState.StoppingRecording)
                ready++;
        }
        return (ready, required);
    }

    private void UpdateStatusDashboard()
    {
        // Post-stop: UI already shows "Preview Active"; do not overwrite with recording/idle text.
        if (_isPostStopProcessing) return;

        var L = _vm.Language;
        StatusValue.Text = GetNeutralStatusText();
        // Use V2 stopwatch for elapsed/session display; only update from timer tick while recording.
        // When not recording (idle, previewing), always show 00:00:00.
        if (_vm.State.RunState != Core.AppRunState.Recording)
        {
            ElapsedValue.Text = "00:00:00";
            SessionTimeValue.Text = "00:00:00";
        }

        var isRecording = _vm.State.RunState == Core.AppRunState.Recording;
        StatusValue.Foreground = isRecording
            ? (Brush)FindResource("StatusRecordingBrush")
            : (Brush)FindResource("AppForegroundBrush");

        var previewOn = _vm.State.IsPreviewing || isRecording;
        if (_vm.PreviewStartInProgress)
        {
            PreviewStatusValue.Text = L["previewStarting"];
            PreviewDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        }
        else
        {
            PreviewStatusValue.Text = previewOn ? L["previewActive"] : L["previewInactive"];
            PreviewDot.Fill = previewOn
                ? (Brush)FindResource("ActiveDotBrush")
                : new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
        }

        StatusDot.Fill = isRecording
            ? (Brush)FindResource("ActiveDotBrush")
            : _vm.PreviewStartInProgress
                ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))
            : previewOn
                ? (Brush)FindResource("ActiveDotBrush")
                : new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));

        var active = _vm.State.CameraLayout;
        var selected = 0;
        for (var i = 0; i < active; i++)
            if (!string.IsNullOrEmpty(_vm.SelectedDeviceIds[i])) selected++;
        if (previewOn && !_vm.PreviewStartInProgress)
        {
            var (ready, required) = CountV2ReadySlots();
            CamerasValue.Text = required > 0
                ? string.Format(L["camerasReadyStatus"], ready, required)
                : $"{selected} / {active}";
        }
        else
            CamerasValue.Text = $"{selected} / {active}";

        PreviewStartupStatusText.Text = _vm.PreviewStartupStatus;
        PreviewStartupStatusText.Visibility = string.IsNullOrWhiteSpace(_vm.PreviewStartupStatus)
            ? Visibility.Collapsed
            : Visibility.Visible;

        MultiCamUsbWarningText.Text = "";
        MultiCamUsbWarningText.Visibility = Visibility.Collapsed;
    }

    private string GetNeutralStatusText()
    {
        // Post-stop processing: RunState has already flipped to Previewing at this point,
        // so the switch below returns the correct "Preview Active" / "previewing" label.
        // "Stopping" branch removed in v1.2.18-alpha — stop is silent in the status panel.
        return _vm.State.RunState switch
        {
            Core.AppRunState.Recording => _vm.Language["recording"],
            Core.AppRunState.Previewing => _vm.Language["previewing"],
            _ => _vm.Language["idle"]
        };
    }

    private async void RefreshDevicesBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevicesBtn.IsEnabled = false;
        try { await _vm.RefreshDevicesAsync(userInitiated: true); }
        finally { RefreshDevicesBtn.IsEnabled = true; }
    }

    private void RefreshTexts()
    {
        var L = _vm.Language;
        HeaderBarControl.Apply(_vm.Version, L["appTitle"], L["header"], L["language"], L["aboutButton"]);
        MainNavControl.ApplyLabels(L["main"], L["verifyNav"], L["hardwareDiagnosticsNav"]);
        FooterText.Text = L["footer"];
        HeaderBarControl.SyncLanguage(_vm.Language.CurrentLanguage);
        RefreshDevicesBtn.Content = L["refreshDevices"];
        LayoutSectionTitle.Text = L["sectionLayout"];
        SelectionSectionTitle.Text = L["sectionCameraSelection"];
        VideoSectionTitle.Text = L["sectionVideo"];
        ResolutionLabel.Text = L["resolutionSetting"];
        FpsLabel.Text = L["fpsSetting"];
        ConstantFrameCountInfoText.Text = L["experimentConstantFrameCountHelp"];
        ConstantFrameCountInfoText.ToolTip = L["experimentConstantFrameCountTooltip"];
        VideoSettingsHint.Text = _vm.State.RunState == Core.AppRunState.Recording
            ? L["videoSettingsWhileRecordingHint"]
            : _vm.IsPreviewOrRecordingActive
                ? L["videoSettingsApplied"]
                : L["videoSettingsHint"];
        SyncVideoSettingsFromConfig();
        OutputSectionTitle.Text = L["sectionOutput"];
        ControlsSectionTitle.Text = L["sectionControls"];
        StatusSectionTitle.Text = L["sectionStatus"];
        PreviewControlsLabel.Text = L["previewControls"];
        RecordingControlsLabel.Text = L["recordingControls"];
        Cam1Label.Text = $"{L["cam1"]} — {L["device"]}";
        Cam2Label.Text = $"{L["cam2"]} — {L["device"]}";
        Cam3Label.Text = $"{L["cam3"]} — {L["device"]}";
        Cam4Label.Text = $"{L["cam4"]} — {L["device"]}";
        OutputLabel.Text = L["outputFolder"];
        BrowseBtn.Content = L["browse"];
        SessionLabel.Text = L["sessionTitle"];
        SessionFolderPreviewLabel.Text = L["sessionFolderPreviewLabel"];
        UpdateSessionFolderPreview();
        StartPreviewBtn.Content = L["startPreview"];
        StopPreviewBtn.Content = L["stopPreview"];
        StartRecordBtn.Content = L["startRecording"];
        StopRecordBtn.Content = L["stopRecording"];
        StatusLabel.Text = $"{L["status"]}:";
        ElapsedLabel.Text = $"{L["elapsed"]}:";
        SessionTimeLabel.Text = $"{L["sessionTime"]}:";
        PreviewStatusLabel.Text = $"{L["previewStatus"]}:";
        CamerasLabel.Text = $"{L["cameras"]}:";
        OutputBox.Text = string.IsNullOrEmpty(_vm.State.OutputFolder)
            ? _vm.OutputFolderDisplay
            : _vm.State.OutputFolder;

        var layoutKeys = new[] { "layout1", "layout2", "layout3", "layout4" };
        for (var i = 0; i < LayoutPanel.Children.Count && i < 4; i++)
            if (LayoutPanel.Children[i] is RadioButton rb)
                rb.Content = L[layoutKeys[i]];

        for (var i = 0; i < 4; i++)
            _slotLabels[i].Text = PreviewPanelTheme.SlotLabel(i);

        LockEnvironmentButton.Content = _envLocked ? L["unlockSettings"] : L["lockEnvironmentalSettings"];
        LockEnvironmentButton.ToolTip = L["lockEnvironmentalSettingsTooltip"];
        OneShotCalibrateButton.Content = L["oneShotAutoCalibrate"];
        OneShotCalibrateButton.ToolTip = L["oneShotAutoCalibrateTooltip"];
        HardwareDiagnosticsPage.ApplyLanguage(L);

        FpsBox.ToolTip = L["fpsBoxTooltip"];
        AdvancedCameraControlsExpander.Header = L["advCamControlsHeader"];
        CameraTargetHeaderText.Text = L["cameraTargetHeader"];
        AdjustSettingsForText.Text = L["adjustSettingsFor"];
        FocusExposureScopeNoteText.Text = L["focusExposureScopeNote"];
        AdvancedAutoFocusCheckBox.Content = L["autoFocusCheckbox"];
        AdvancedAutoFocusCheckBox.ToolTip = L["autoFocusTooltip"];
        ManualFocusValueLabelText.Text = L["manualFocusValueLabel"];
        DefaultFocusButton.ToolTip = L["defaultFocusButtonTooltip"];
        ExposureControlsHeaderText.Text = L["exposureControlsHeader"];
        AdvancedAutoExposureCheckBox.Content = L["autoExposureCheckbox"];
        AdvancedAutoExposureCheckBox.ToolTip = L["autoExposureTooltip"];
        ManualExposureIndexLabelText.Text = L["manualExposureIndexLabel"];
        ManualExposureIndexLabelText.ToolTip = L["manualExposureIndexTooltip"];
        ManualExposureBestEffortNoteText.Text = L["manualExposureBestEffortNote"];
        DefaultExposureButton.ToolTip = L["defaultExposureButtonTooltip"];
        WhiteBalanceHeaderText.Text = L["whiteBalanceHeader"];
        WhiteBalanceKelvinNoteText.Text = L["whiteBalanceKelvinNote"];
        EnvironmentalLockHeaderText.Text = L["environmentalLockHeader"];
        EnvironmentalLockDescriptionText.Text = L["environmentalLockDescription"];
        if (string.IsNullOrEmpty(CalibrationStatusLabel.Text))
            CalibrationStatusLabel.Text = L["startPreviewToEnableCalibration"];
        UpdateCameraControlButtonLabels();
        UpdateManualFocusAvailability();

        UpdateStatusDashboard();
        UpdatePreviewOverlayStats();
        UpdateActionButtons();
    }

    private async void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populatingDeviceBoxes) return;

        for (var i = 0; i < 4; i++)
        {
            if (!ReferenceEquals(sender, _deviceBoxes[i])) continue;
            if (_vm.BlockPreviewMutation(
                    "CAMERA_SELECTION_CHANGE_BLOCKED_PREVIEW_ACTIVE",
                    "Stop preview before changing layout or camera selection."))
            {
                _populatingDeviceBoxes = true;
                try { SetDeviceComboSelection(i, _vm.SelectedDeviceIds[i]); }
                finally { _populatingDeviceBoxes = false; }
                UpdateActionButtons();
                return;
            }

            if (_deviceBoxes[i].SelectedItem is ComboBoxItem ci)
                _vm.SelectedDeviceIds[i] = ci.Tag as string;

            try
            {
                await _vm.ApplyDeviceSelectionForSlotAsync(i, idx => _previewImages[idx]);
            }
            catch (Exception ex)
            {
                StatusValue.Text = ex.Message;
            }

            if (_vm.HasDuplicateDeviceSelection())
                StatusValue.Text = _vm.GetDuplicateDeviceWarning() ?? _vm.Language["duplicateDeviceWarning"];
            UpdateActionButtons();
            UpdatePreviewOverlayStats();
            return;
        }
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog() == true)
        {
            _vm.SetOutputFolder(dlg.FolderName);
            OutputBox.Text = dlg.FolderName;
            UpdateSessionFolderPreview();
        }
    }

    private async void StartPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        var uiBefore = new MainViewModel.UiButtonStates(
            StartPreviewBtn.IsEnabled,
            StopPreviewBtn.IsEnabled,
            StartRecordBtn.IsEnabled,
            StopRecordBtn.IsEnabled);
        ApplyVideoSettingsToViewModel();
        _vm.State.SessionTitle = SessionBox.Text;
        SetPreviewButtonsBusy(true);

        _freezeWatchdog.CurrentAppState = "PreviewStarting";
        try
        {
            var started = await StartV2DefaultPipelineAsync();
            if (started)
            {
                _freezeWatchdog.CurrentAppState = "Previewing";
                StartPreviewStatsTimer();
            }
            else
                _freezeWatchdog.CurrentAppState = "Idle";
        }
        finally
        {
            SetPreviewButtonsBusy(false);
            UpdateActionButtons();
        }
    }

    private void SetPreviewButtonsBusy(bool busy)
    {
        _previewUiBusy = busy;
        UpdateUiStateFromCurrentState();
    }

    private DispatcherTimer? _previewStatsTimer;

    private void StartPreviewStatsTimer()
    {
        _previewStatsTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _previewStatsTimer.Tick -= PreviewStatsTimer_Tick;
        _previewStatsTimer.Tick += PreviewStatsTimer_Tick;
        _previewStatsTimer.Start();
    }

    private void StopPreviewStatsTimer()
    {
        _previewStatsTimer?.Stop();
    }

    private void PreviewStatsTimer_Tick(object? sender, EventArgs e)
    {
        UpdatePreviewOverlayStats();
        UpdateStatusDashboard();
    }

    private async void StopPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        StopElapsedTimer();
        StopPreviewStatsTimer();
        StartPreviewBtn.IsEnabled = false;
        StopPreviewBtn.IsEnabled = false;
        StartRecordBtn.IsEnabled = false;
        StopRecordBtn.IsEnabled = false;
        _freezeWatchdog.CurrentAppState = "Idle";

        try
        {
            await StopV2DefaultPipelineAsync(); // clears every slot's preview surface, GPU and CPU alike
        }
        finally
        {
            UpdatePreviewOverlayStats();
            UpdateStatusDashboard();
            UpdateActionButtons();
        }
    }

    private bool ConfirmPreRecordSettingsOrCancel()
    {
        var mismatches = _vm.GetPreRecordSettingsMismatch();
        if (mismatches == null || mismatches.Count == 0) return true;

        var L = _vm.Language;
        var details = new System.Text.StringBuilder();
        foreach (var m in mismatches)
        {
            if (m.IsResolution)
                details.AppendLine(string.Format(L["recordSettingsMismatchLineRes"],
                    m.SlotName, m.RequestedPreset, m.LivePreset));
            else
                details.AppendLine(string.Format(L["recordSettingsMismatchLineFps"],
                    m.SlotName, m.RequestedFps, m.LiveFps));
        }

        var message = L["recordSettingsMismatchIntro"] + "\n\n" + details.ToString().TrimEnd();
        var result = MessageBox.Show(
            message,
            L["recordSettingsMismatchTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        var continued = result == MessageBoxResult.Yes;
        _vm.LogPreRecordMismatchChoice(continued, mismatches.Count);
        return continued;
    }

    private bool ConfirmPreRecordStorageOrCancel()
    {
        var estimate = EstimatePreRecordStorage();
        if (estimate == null)
            return true;

        if (estimate.Value.FreeDiskGB >= estimate.Value.EstimatedSessionGB * 2.0)
            return true;

        var message =
            "Available disk space may be insufficient for this recording duration/resolution/camera count." +
            $"\n\nEstimated session size: {estimate.Value.EstimatedSessionGB:F1} GB" +
            $"\nAvailable disk space: {estimate.Value.FreeDiskGB:F1} GB" +
            $"\nEstimated rate: {estimate.Value.GBPerHourAllCameras:F1} GB/hour for {estimate.Value.CameraCount} camera(s)" +
            "\n\nContinue recording?";
        var result = MessageBox.Show(
            message,
            "Storage warning",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private (double EstimatedSessionGB, double FreeDiskGB, double GBPerHourAllCameras, int CameraCount)? EstimatePreRecordStorage()
    {
        try
        {
            var cameraCount = _vm.SelectedDeviceIds
                .Take(Math.Max(1, _vm.CurrentLayoutCount))
                .Count(id => !string.IsNullOrWhiteSpace(id));
            if (cameraCount <= 0)
                return null;

            var outputFolder = string.IsNullOrWhiteSpace(_vm.State.OutputFolder)
                ? _vm.OutputFolderDisplay
                : _vm.State.OutputFolder;
            if (string.IsNullOrWhiteSpace(outputFolder))
                return null;

            var root = System.IO.Path.GetPathRoot(outputFolder);
            if (string.IsNullOrWhiteSpace(root))
                return null;

            var freeGb = new System.IO.DriveInfo(root).AvailableFreeSpace / 1024d / 1024d / 1024d;
            var durationHours = EstimateSelectedDurationHours();
            var perCameraGbHour = TryReadRecentGbPerHourPerCamera(outputFolder)
                                  ?? EstimateFallbackGbPerHourPerCamera();
            var allCamerasGbHour = perCameraGbHour * cameraCount;
            var estimatedSessionGb = allCamerasGbHour * durationHours;
            return (estimatedSessionGb, freeGb, allCamerasGbHour, cameraCount);
        }
        catch
        {
            return null;
        }
    }

    private double EstimateSelectedDurationHours()
    {
        var seconds = _vm.Config.ExperimentMode.DefaultDurationSeconds > 0
            ? _vm.Config.ExperimentMode.DefaultDurationSeconds
            : 3600;
        return Math.Max(1.0 / 60.0, seconds / 3600.0);
    }

    private double EstimateFallbackGbPerHourPerCamera()
    {
        var width = _vm.Config.PreferredCaptureWidth;
        var height = _vm.Config.PreferredCaptureHeight;
        if (width <= 0 || height <= 0)
            return 8.0;
        var pixels = width * height;
        if (pixels >= 1920 * 1080)
            return 10.0;
        if (pixels >= 1280 * 720)
            return 4.0;
        return 1.5;
    }

    private static double? TryReadRecentGbPerHourPerCamera(string outputFolder)
    {
        try
        {
            if (!System.IO.Directory.Exists(outputFolder))
                return null;

            var latest = System.IO.Directory
                .EnumerateFiles(outputFolder, "recording_diagnostics_summary.json", System.IO.SearchOption.AllDirectories)
                .Select(path => new System.IO.FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest == null)
                return null;

            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(latest.FullName));
            if (doc.RootElement.TryGetProperty("EstimatedGBPerHourPerCamera", out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                var rate = value.GetDouble();
                return rate > 0 ? rate : null;
            }
        }
        catch
        {
            // Best-effort warning only.
        }

        return null;
    }

    private async void StartRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        var uiBefore = new MainViewModel.UiButtonStates(
            StartPreviewBtn.IsEnabled,
            StopPreviewBtn.IsEnabled,
            StartRecordBtn.IsEnabled,
            StopRecordBtn.IsEnabled);
        _vm.State.SessionTitle = SessionBox.Text;
        // Disable the record button immediately so the user sees instant response.
        _recordUiBusy = true;
        UpdateUiStateFromCurrentState();
        if (_videoSettingsReapplyTask is { IsCompleted: false } pendingReapply)
        {
            VideoSettingsHint.Text = _vm.Language["videoSettingsApplying"];
            await pendingReapply;
        }
        if (!ConfirmPreRecordStorageOrCancel())
        {
            _recordUiBusy = false;
            UpdateActionButtons();
            return;
        }

        try
        {
            await StartV2DefaultRecordingAsync();
            if (_vm.State.IsRecording)
            {
                StartElapsedTimer();
                // Log-only (no UI text change on Start Recording — keep the click response silent).
                AppDiagnosticLogger.Runtime($"RECORD_START_LOCK_STATE envLocked={_envLocked}");
            }
        }
        finally
        {
            _vm.UpdateRecordingTitle(t => Title = t);
            _recordUiBusy = false;
            UpdateActionButtons();
        }
    }

    private async void StopRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        // Guard against re-entrancy: a second click while finalization is in progress is ignored.
        if (!_stopRecordingGuard.TryEnter()) return;

        // ── Enter post-stop state immediately (v1.2.18-alpha) ────────────────
        // UI looks stopped right away. Freeze elapsed at click time.
        // MP4 finalization / audit continues silently — never shown in main status.
        _postStopFrozenElapsed = _v2RecordingStopwatch.Elapsed;
        _isPostStopProcessing  = true;
        StopRecordBtn.IsEnabled  = false;
        StartRecordBtn.IsEnabled = false;
        // Clear any in-progress capture messages; show stopped state (not "Finalizing").
        ShowPreviewStatus("");
        var frozenStr = _postStopFrozenElapsed.ToString(@"hh\:mm\:ss");
        ElapsedValue.Text     = frozenStr;
        SessionTimeValue.Text = frozenStr;
        // Status panel: show previewing state immediately — RunState will flip in StopV2DefaultRecordingAsync.
        StatusValue.Text = _vm.Language["previewing"];

        try
        {
            await StopV2DefaultRecordingAsync();
            _vm.UpdateRecordingTitle(t => Title = t);
            UpdateStatusDashboard();
            UpdatePreviewOverlayStats();
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"STOP_RECORD_CLICK_ERROR {ex.GetType().Name}: {ex.Message}");
            ShowPreviewStatus($"Stop recording failed: {ex.Message}");
        }
        finally
        {
            // ── Exit post-stop processing ─────────────────────────────────────
            _isPostStopProcessing = false;
            _stopRecordingGuard.Release();
            UpdateActionButtons(); // restores correct enabled state
            if (_vm.State.RunState != Core.AppRunState.Recording) StopElapsedTimer();
        }
    }

    private void StartElapsedTimer()
    {
        _v2RecordingStopwatch.Restart();
        ElapsedValue.Text = "00:00:00";
        SessionTimeValue.Text = "00:00:00";
        _elapsedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _elapsedTimer.Tick -= ElapsedTimer_Tick;
        _elapsedTimer.Tick += ElapsedTimer_Tick;
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer() => _elapsedTimer?.Stop();

    private string GetV2ElapsedString()
    {
        if (_vm.State.RunState != Core.AppRunState.Recording) return "00:00:00";
        return _v2RecordingStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
    }

    private void ElapsedTimer_Tick(object? sender, EventArgs e)
    {
        // During post-stop processing, keep elapsed frozen and skip status updates.
        if (_isPostStopProcessing)
        {
            var frozen = _postStopFrozenElapsed.ToString(@"hh\:mm\:ss");
            ElapsedValue.Text     = frozen;
            SessionTimeValue.Text = frozen;
            return;
        }
        var elapsed = GetV2ElapsedString();
        ElapsedValue.Text = elapsed;
        SessionTimeValue.Text = elapsed;
        _vm.UpdateRecordingTitle(t => Title = t);
        UpdateStatusDashboard();
    }

    private IReadOnlyList<CameraDevice> GetAllCamerasForDiagnostics() => _vm.Devices.ToList();

    private IReadOnlyList<CameraDevice> GetSelectedCamerasForDiagnostics()
    {
        var ids = _vm.SelectedDeviceIds
            .Take(_vm.CurrentLayoutCount)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        return ids
            .Select(id => _vm.Devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(d => d != null)
            .Cast<CameraDevice>()
            .ToList();
    }

    private void ShowPage(string page)
    {
        RecordingPagePanel.Visibility = page == "main" ? Visibility.Visible : Visibility.Collapsed;
        VerificationPage.Visibility = page == "verification" ? Visibility.Visible : Visibility.Collapsed;
        HardwareDiagnosticsPage.Visibility = page == "hardware" ? Visibility.Visible : Visibility.Collapsed;
        MainNavControl.SetActivePage(page);
    }

    public async Task ForceCleanupAsync()
    {
        await CleanupForCloseAsync(forceShutdown: false).ConfigureAwait(true);
    }

    private async Task CleanupForCloseAsync(bool forceShutdown)
    {
        StopElapsedTimer();
        StopPreviewStatsTimer();
        _resizeTimer?.Stop();
        HardwareDiagnosticsPage.CancelActiveScan();
        _videoSettingsReapplyCts?.Cancel();
        _videoSettingsReapplyCts?.Dispose();
        _videoSettingsReapplyCts = null;
        _windowCts.Dispose();

        foreach (var image in _previewImages)
            image.Source = null;

        // V2 cleanup — stop all active recording slots then all preview slots
        await StopV2AllSlotsRecordingAsync();
        await _v2Engine.StopAllSlotsPreviewAsync();
        _v2Engine.SlotFrameRendered -= OnV2SlotFrameRendered;
        _v2Engine.SlotFallenBackToWpf -= OnV2SlotFallenBackToWpf;
        _v2Engine.Dispose();
        _backendRegistry.Dispose();

        await _vm.OnAppClosingAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        if (forceShutdown)
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
    }

    // ── VideoEngineV2 lifecycle helpers ───────────────────────────────────────

    private async Task InitV2EngineAsync()
    {
        try
        {
            _backendRegistry.SelectBackend(VideoEngineSettings.RequestedBackendId);
            await _v2Engine.EnumerateDevicesAsync(_windowCts.Token);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"V2_INIT_ERROR {ex.GetType().Name}: {ex.Message}");
            ShowPreviewStatus($"Camera init failed: {ex.Message}", isError: true);
        }
    }

    private void ShowPreviewStatus(string message, bool isError = false)
    {
        Dispatcher.InvokeAsync(() =>
        {
            PreviewStartupStatusText.Text = message;
            PreviewStartupStatusText.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.OrangeRed)
                : (System.Windows.Media.Brush)FindResource("AppSecondaryTextBrush");
            PreviewStartupStatusText.Visibility = string.IsNullOrEmpty(message)
                ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    private readonly System.Windows.Media.Imaging.WriteableBitmap?[] _v2CurrentBitmaps =
        new System.Windows.Media.Imaging.WriteableBitmap?[4];

    // Multi-slot frame rendered handler — routes each slot's preview bitmap to the matching Image control.
    // GPU slots have their own HwndHost so we skip the WriteableBitmap assignment for them.
    private void OnV2SlotFrameRendered(object? sender, int slot)
    {
        if (slot < 0 || slot >= 4) return;
        // GPU renderer presents directly via DXGI — no WriteableBitmap to assign.
        if (_v2Engine.GetSlotGpuPreviewElement(slot) is not null) return;
        var bmp = _v2Engine.GetSlotPreviewBitmap(slot);
        if (_v2CurrentBitmaps[slot] != bmp || !ReferenceEquals(_previewImages[slot].Source, bmp))
        {
            _v2CurrentBitmaps[slot] = bmp;
            _previewImages[slot].Source = bmp;
        }
    }

    private void OnV2SlotFallenBackToWpf(
        int slot,
        System.Windows.Media.Imaging.WriteableBitmap bitmap)
    {
        if (slot < 0 || slot >= _previewImages.Length) return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnV2SlotFallenBackToWpf(slot, bitmap));
            return;
        }

        if (_slotViewboxes[slot] is { } viewbox
            && !ReferenceEquals(_cellBorders[slot].Child, viewbox))
        {
            _cellBorders[slot].Child = viewbox;
        }

        _v2CurrentBitmaps[slot] = bitmap;
        _previewImages[slot].Source = bitmap;
        AppDiagnosticLogger.Runtime($"V2_SLOT_RENDERER_FALLBACK slot={slot} renderer=WPF");
    }

    // ── VideoEngineV2 pipeline methods ────────────────────────────────────────

    /// <summary>
    /// Clears every slot's visible preview surface — restores the placeholder viewbox for GPU
    /// slots (whose HwndHost otherwise keeps showing its last-presented frame indefinitely,
    /// since neither stopping the renderer nor clearing an Image.Source touches that separate
    /// native HWND) and clears the WriteableBitmap source for CPU-fallback slots.
    /// </summary>
    private void ResetAllSlotPreviewSurfaces()
    {
        for (var i = 0; i < 4; i++)
        {
            _v2CurrentBitmaps[i] = null;
            _previewImages[i].Source = null;
            // If a GPU panel was placed in the cell, restore the original viewbox
            if (_slotViewboxes[i] is { } vb && _cellBorders[i] is not null
                && !ReferenceEquals(_cellBorders[i].Child, vb))
            {
                _cellBorders[i].Child = vb;
            }
        }
    }

    /// <summary>
    /// Starts the V2 preview pipeline for all active camera slots.
    /// Sets RunState to Previewing on success.
    /// </summary>
    private async Task<bool> StartV2DefaultPipelineAsync()
    {
        ShowPreviewStatus("");
        _v2Engine.SlotFrameRendered -= OnV2SlotFrameRendered;
        _v2Engine.SlotFrameRendered += OnV2SlotFrameRendered;

        ResetAllSlotPreviewSurfaces();

        // Enumerate devices if needed
        try
        {
            await _v2Engine.EnumerateDevicesAsync(_windowCts.Token);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"V2_DEVICE_ENUM_FAILED {ex.GetType().Name}: {ex.Message}");
            ShowPreviewStatus($"Camera enumeration failed: {ex.Message}", isError: true);
            return false;
        }

        if (_v2Engine.DiscoveredDevices.Count == 0)
        {
            ShowPreviewStatus("No cameras found. Check USB connections and restart preview.", isError: true);
            return false;
        }

        // Select and open cameras for each active slot in parallel
        var layout = _vm.State.CameraLayout;
        var openedSlots = 0;
        var tasks = new List<(int slot, Task task)>();

        for (var i = 0; i < layout; i++)
        {
            var slotIndex = i;
            var deviceId = _vm.SelectedDeviceIds[slotIndex];
            if (string.IsNullOrEmpty(deviceId)) continue;

            var t = OpenV2SlotAsync(slotIndex, deviceId);
            tasks.Add((slotIndex, t));
        }

        await Task.WhenAll(tasks.Select(t => t.task));

        for (var i = 0; i < layout; i++)
        {
            var state = _v2Engine.GetSlotPipelineState(i);
            if (state == CameraPipelineState.Previewing)
                openedSlots++;
        }

        if (openedSlots == 0)
        {
            _v2Engine.SlotFrameRendered -= OnV2SlotFrameRendered;
            ShowPreviewStatus("Failed to open any camera. Check device connections.", isError: true);
            return false;
        }

        // Set RunState so the rest of the UI (Start Recording, status) works correctly
        _vm.State.RunState = Core.AppRunState.Previewing;
        AppDiagnosticLogger.Runtime($"V2_DEFAULT_PREVIEW_STARTED slots={openedSlots}/{layout}");
        return true;
    }

    private async Task OpenV2SlotAsync(int slotIndex, string deviceId)
    {
        try
        {
            await _v2Engine.SelectSlotDeviceAsync(slotIndex, deviceId);
            // Pass the user's actually-selected resolution (not the fixed 1280x720 preview
            // default) so the GPU letterbox panel starts at the correct aspect ratio for
            // non-16:9 selections (e.g. 480p/4:3) instead of pillarboxing until the first
            // real frame arrives and corrects it.
            await _v2Engine.PrepareSlotPreviewAsync(
                slotIndex, Dispatcher,
                VideoEngineSettings.DefaultPreferredWidth,
                VideoEngineSettings.DefaultPreferredHeight);
            await _v2Engine.StartSlotPreviewAsync(slotIndex);

            // Wire preview surface: GPU panel (D3D11 HwndHost) or WPF WriteableBitmap.
            var gpuElement = _v2Engine.GetSlotGpuPreviewElement(slotIndex);
            if (gpuElement is not null)
            {
                // Replace viewbox+Image with GPU HwndHost (no airspace issue: labels are in header)
                _cellBorders[slotIndex].Child = gpuElement;
            }
            else
            {
                // CPU path: restore viewbox and bind WriteableBitmap
                if (_slotViewboxes[slotIndex] is { } vb && _cellBorders[slotIndex].Child != vb)
                    _cellBorders[slotIndex].Child = vb;
                _previewImages[slotIndex].Source = _v2Engine.GetSlotPreviewBitmap(slotIndex);
            }
            AppDiagnosticLogger.Runtime($"V2_SLOT_OPENED slot={slotIndex} deviceId={deviceId}");
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"V2_SLOT_OPEN_FAILED slot={slotIndex} {ex.GetType().Name}: {ex.Message}");
            ShowPreviewStatus($"cam{slotIndex + 1} failed to open: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Stops all active V2 preview slots and returns to Idle state.
    /// </summary>
    private async Task StopV2DefaultPipelineAsync()
    {
        // Stop any active recordings first
        await StopV2AllSlotsRecordingAsync();

        _v2Engine.SlotFrameRendered -= OnV2SlotFrameRendered;

        try
        {
            await _v2Engine.StopAllSlotsPreviewAsync();
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"V2_STOP_ALL_PREVIEW_ERROR {ex.GetType().Name}: {ex.Message}");
        }

        ResetAllSlotPreviewSurfaces();

        _vm.State.RunState = Core.AppRunState.Idle;
        AppDiagnosticLogger.Runtime("V2_DEFAULT_PREVIEW_STOPPED");
    }

    /// <summary>
    /// Starts V2 recording for all active camera slots that are currently previewing.
    /// Creates the session folder and per-camera file sets.
    /// </summary>
    private async Task StartV2DefaultRecordingAsync()
    {
        // Clear stale session state from any previous recording.
        Array.Clear(_v2RecordingFileSets, 0, _v2RecordingFileSets.Length);
        _v2SessionFolderPath = null;
        // Without this, freeze counters accumulate for the app process's entire lifetime —
        // a freeze during one recording was found bleeding into every later recording's
        // metadata, falsely reporting a freeze that never happened during that later one.
        _freezeWatchdog.Reset();
        _freezeWatchdog.CurrentAppState = "RecordingStarting";

        // Lock the UI language for this recording session.
        _sessionLanguage = _vm.Language.CurrentLanguage;
        var lockTooltip = _vm.Language["languageLockedDuringRecordingTooltip"];
        HeaderBarControl.SetLanguageLockEnabled(false, lockTooltip);

        var outputFolder = OutputBox.Text;
        var sessionTitle = !string.IsNullOrWhiteSpace(SessionBox.Text) ? SessionBox.Text.Trim() : null;

        SessionFolderPlan plan;
        try
        {
            // Move folder creation off UI thread: Directory.CreateDirectory is synchronous I/O.
            plan = await Task.Run(() => SessionFolderNameGenerator.CreateUniqueSessionFolder(outputFolder, sessionTitle));
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"V2_SESSION_FOLDER_ERROR {ex.GetType().Name}: {ex.Message}");
            ShowPreviewStatus($"Recording failed: could not create session folder — {ex.Message}", isError: true);
            _freezeWatchdog.CurrentAppState = "Previewing";
            return;
        }

        _v2SessionFolderPath = plan.FullPath;
        var layout = _vm.State.CameraLayout;

        // Phase 1: prepare all file sets synchronously (fast, no I/O)
        // then create cam folders in parallel off the UI thread.
        var activeSlots = Enumerable.Range(0, layout)
            .Where(i => !string.IsNullOrEmpty(_vm.SelectedDeviceIds[i])
                     && _v2Engine.GetSlotPipelineState(i) == CameraPipelineState.Previewing)
            .ToList();

        if (activeSlots.Count == 0)
        {
            ShowPreviewStatus("No active previewing cameras to record.", isError: true);
            _freezeWatchdog.CurrentAppState = "Previewing";
            return;
        }

        // Create output directories off UI thread.
        var fileSets = new RecordingFileSet?[4];
        try
        {
            await Task.Run(() =>
            {
                foreach (var i in activeSlots)
                {
                    var camFolder = Path.Combine(plan.FullPath, $"cam{i + 1}");
                    Directory.CreateDirectory(camFolder);
                    fileSets[i] = RecordingFileSet.Create(camFolder, $"cam{i + 1}");
                }
            });
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"V2_SESSION_DIR_ERROR {ex.GetType().Name}: {ex.Message}");
            ShowPreviewStatus($"Recording failed: could not create camera folders — {ex.Message}", isError: true);
            _freezeWatchdog.CurrentAppState = "Previewing";
            return;
        }

        // Start all camera recordings concurrently.
        // Sequential startup was the previous pattern; with 3 cameras each taking ~1-2s
        // to initialize LowLagMediaRecording, that was 3-6s of UI stall.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var startTasks = activeSlots.Select(i => StartOneSlotRecordingAsync(i, fileSets[i]!)).ToList();
        var results = await Task.WhenAll(startTasks);
        sw.Stop();

        var startedSlots = results.Count(r => r);
        // Populate file-set array from the ordered results list.
        for (var idx = 0; idx < activeSlots.Count; idx++)
        {
            var slotIdx = activeSlots[idx];
            _v2RecordingFileSets[slotIdx] = results[idx] ? fileSets[slotIdx] : null;
        }

        if (startedSlots > 0)
        {
            // Throttle preview FPS to free CPU/dispatcher headroom during multi-camera recording.
            // GPU slots are unaffected (render thread is independent); WPF slots benefit from this.
            var recordingPreviewFpsCap = VideoEngineV2.RecommendedPreviewFpsForRecordingCameras(startedSlots);
            _v2Engine.SetAllSlotsPreviewFpsLimit(recordingPreviewFpsCap);
            var rendererSummary = string.Join(", ", activeSlots.Select(i =>
                $"cam{i + 1}={_v2Engine.GetSlotPreviewRenderer(i)}"));
            AppDiagnosticLogger.Runtime(
                $"V2_RECORDING_PREVIEW_FPS_CAP slots={startedSlots} cap={recordingPreviewFpsCap} renderers=[{rendererSummary}] " +
                "note=cap only applies to slots on the WPF/CPU fallback renderer; GPU (D3D11) slots are unthrottled");

            _vm.State.RunState = Core.AppRunState.Recording;
            _freezeWatchdog.CurrentAppState = "Recording";
            UpdateActionButtons();
            AppDiagnosticLogger.Runtime(
                $"V2_DEFAULT_RECORDING_STARTED slots={startedSlots}/{activeSlots.Count} " +
                $"session={plan.FolderName} startMs={sw.ElapsedMilliseconds}");
        }
        else
        {
            _freezeWatchdog.CurrentAppState = "Previewing";
        }
    }

    private async Task<bool> StartOneSlotRecordingAsync(int slot, RecordingFileSet fileSet)
    {
        try
        {
            var slotSw = System.Diagnostics.Stopwatch.StartNew();
            await _v2Engine.StartSlotRecordingAsync(slot, fileSet);
            slotSw.Stop();
            AppDiagnosticLogger.Runtime(
                $"V2_SLOT_RECORDING_STARTED slot={slot} startMs={slotSw.ElapsedMilliseconds}");
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime(
                $"V2_SLOT_RECORDING_START_FAILED slot={slot} {ex.GetType().Name}: {ex.Message}");
            ShowPreviewStatus($"cam{slot + 1} recording failed to start: {ex.Message}", isError: true);
            return false;
        }
    }

    /// <summary>
    /// Stops V2 recording for all active slots, finalises MP4 files, returns to Previewing immediately,
    /// then awaits post-recording metadata while leaving the dispatcher responsive.
    /// </summary>
    private async Task StopV2DefaultRecordingAsync()
    {
        _freezeWatchdog.CurrentAppState = "RecordingStopping";

        // Phase 1: stop cameras and finalise MP4 files.
        var collected = await StopV2AllSlotCamerasAsync();

        // Restore unlimited preview FPS now that recording is done.
        _v2Engine.SetAllSlotsPreviewFpsLimit(0);

        _vm.State.RunState = Core.AppRunState.Previewing;
        _freezeWatchdog.CurrentAppState = "Previewing";
        HeaderBarControl.SetLanguageLockEnabled(true);
        AppDiagnosticLogger.Runtime("V2_DEFAULT_RECORDING_STOPPED");

        // Phases 2-7: preview is already live, but keep this task tracked and awaited so
        // metadata is durable before another recording starts or the app closes.
        if (collected.Count > 0)
        {
            var sessionFolder = _v2SessionFolderPath;
            _postRecordingMetadataTask = RunPostRecordingMetadataAsync(collected, sessionFolder);
            try { await _postRecordingMetadataTask; }
            catch (Exception ex)
            {
                AppDiagnosticLogger.Runtime(
                    $"V2_POST_METADATA_ERROR {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Stops all recording slots and finalises MP4 files. Returns the list of finalized results.
    /// Preview FPS is NOT restored here — caller does that immediately after this returns.
    /// </summary>
    private async Task<List<(int slot, RecordingFileSet fileSet, RecordingFinalizeResult result)>>
        StopV2AllSlotCamerasAsync()
    {
        var stopTasks = new List<Task<(int slot, RecordingFileSet fileSet, RecordingFinalizeResult result)?>>();
        for (var i = 0; i < 4; i++)
        {
            if (_v2RecordingFileSets[i] is null) continue;
            var fileSet = _v2RecordingFileSets[i]!;
            _v2RecordingFileSets[i] = null;
            if (_v2Engine.GetSlotPipelineState(i) == CameraPipelineState.Recording)
                stopTasks.Add(StopV2SlotCameraAsync(i, fileSet));
        }

        var stopped = await Task.WhenAll(stopTasks);
        return stopped.Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x.slot).ToList();
    }

    private async Task<(int slot, RecordingFileSet fileSet, RecordingFinalizeResult result)?>
        StopV2SlotCameraAsync(int slot, RecordingFileSet fileSet)
    {
        try
        {
            var result = await _v2Engine.StopSlotRecordingAsync(slot, fileSet);
            AppDiagnosticLogger.Recording(
                $"V2_SLOT_REC_FINALIZED slot={slot} status={result.Status} " +
                $"frames={result.FramesWritten} hw={result.HardwareEncoderUsed} path={result.FinalVideoPath}");
            if (result.Status != RecordingFinalizeStatus.Success)
                AppDiagnosticLogger.Runtime(
                    $"V2_SLOT_REC_STATUS_WARN slot={slot} status={result.Status} reason={result.FailureReason}");
            return (slot, fileSet, result);
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime(
                $"V2_SLOT_REC_STOP_ERROR slot={slot} {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Called from CleanupForCloseAsync — needs to stop cameras synchronously before app exits.
    /// Runs analysis in-line (not fire-and-forget) so files are written before shutdown.
    /// </summary>
    private async Task StopV2AllSlotsRecordingAsync()
    {
        var collected = await StopV2AllSlotCamerasAsync();
        if (collected.Count > 0)
        {
            _postRecordingMetadataTask = RunPostRecordingMetadataAsync(collected, _v2SessionFolderPath);
            await _postRecordingMetadataTask;
        }
        else
        {
            await _postRecordingMetadataTask;
        }
    }

    /// <summary>
    /// Runs timestamp CSV analysis plus per-camera and session metadata generation.
    /// Call from the UI context because metadata snapshots current UI/session state.
    /// </summary>
    private async Task RunPostRecordingMetadataAsync(
        List<(int slot, RecordingFileSet fileSet, RecordingFinalizeResult result)> collected,
        string? sessionFolder)
    {
        // Phase 2: Read timestamp CSV stats (fast file read — needed for metadata)
        var tsStatsBySlot = new Dictionary<int, TimestampCsvStats?>();
        foreach (var (slot, fileSet, _) in collected)
            tsStatsBySlot[slot] = await ReadTimestampCsvStatsAsync(fileSet.TimestampCsvPath);

        // Phase 3: Compute session-level stats from frame counts and CSV data only
        var emptyFfprobe = new Dictionary<int, Diagnostics.FfprobeResult>();
        var emptyDup     = new Dictionary<int, Diagnostics.DuplicateDetectionResult>();
        var sessionStats = ComputeSessionVerificationStats(collected, tsStatsBySlot, emptyFfprobe, emptyDup);

        // Phase 4: Write per-camera metadata
        foreach (var (slot, fileSet, result) in collected)
        {
            await WriteV2SlotMetadataAsync(slot, fileSet, result,
                tsStatsBySlot.GetValueOrDefault(slot),
                null, null, null,
                sessionStats,
                null,
                tsStatsBySlot);
        }

        // Phase 5: Write session-level metadata
        if (!string.IsNullOrEmpty(sessionFolder))
            await WriteV2SessionMetadataAsync(collected, tsStatsBySlot, emptyFfprobe, emptyDup,
                sessionStats, sessionFolder);

        // CanonicalMetadataJsonPath/CanonicalMetadataTxtPath (the unprefixed "metadata.json"/
        // "metadata.txt" duplicate) are no longer written — see the comments in
        // WriteV2SlotMetadataAsync — so they must not appear in this post-write existence check,
        // or every recording would be incorrectly flagged as missing required files.
        var requiredFiles = collected.SelectMany(x => new[]
        {
            x.fileSet.MetadataJsonPath,
            x.fileSet.MetadataTxtPath,
        }).ToList();
        if (!string.IsNullOrEmpty(sessionFolder))
        {
            requiredFiles.Add(Path.Combine(sessionFolder, "session_metadata.json"));
            requiredFiles.Add(Path.Combine(sessionFolder, "session_metadata.txt"));
        }

        var missing = requiredFiles.Where(path => !File.Exists(path)).ToArray();
        if (missing.Length > 0)
            throw new IOException($"Required metadata output missing: {string.Join(", ", missing.Select(Path.GetFileName))}");

        AppDiagnosticLogger.Runtime(
            $"V2_POST_METADATA_COMPLETE cameras={collected.Count} files={requiredFiles.Count}");
    }

    // ── Session verification stats ────────────────────────────────────────────

    private sealed record SessionVerificationStats(
        int      ActiveCameraCount,
        int[]    ActiveSlots,
        long[]   FrameCounts,
        double[] DurationsS,
        double[] EstimatedFps,
        long     GlobalFrameMin,
        long     GlobalFrameMax,
        long     GlobalFrameSpread,
        double   GlobalDurationMin,
        double   GlobalDurationMax,
        double   GlobalDurationSpreadS,
        string   InterCameraComparison,
        string   InterCameraTimingConfidence,
        string   GlobalSessionResult,
        List<string> GlobalVerificationNotes
    );

    // ── Timestamp CSV analysis ────────────────────────────────────────────────

    private sealed class TimestampCsvStats
    {
        public long   RowCount          { get; init; }
        public double FirstTimestampMs  { get; init; }
        public double LastTimestampMs   { get; init; }
        public double DurationMs        { get; init; }
        public double MeanIntervalMs    { get; init; }
        public double MedianIntervalMs  { get; init; }
        public double MinIntervalMs     { get; init; }
        public double MaxIntervalMs     { get; init; }
        public double P95IntervalMs     { get; init; }
        public double P99IntervalMs     { get; init; }
        public int    GapCount           { get; init; }
        public int    StartupGapCount   { get; init; }
        public int    MidSessionGapCount { get; init; }
        public double IntervalStdMs     { get; init; }
        public double CvPercent         { get; init; }   // coefficient of variation (StdDev/Mean×100)
        public double EstimatedFps      { get; init; }
    }

    private static async Task<TimestampCsvStats?> ReadTimestampCsvStatsAsync(string csvPath)
    {
        if (!File.Exists(csvPath)) return null;
        try
        {
            var csvLines = await File.ReadAllLinesAsync(csvPath);
            var intervals = new List<double>(csvLines.Length);
            double firstTs = double.NaN, lastTs = double.NaN;
            int gapCount = 0, startupGapCount = 0, midGapCount = 0;
            long rowCount = 0;
            const double warmupMs = 10_000.0;

            foreach (var line in csvLines.Skip(1)) // skip header
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(',');
                if (p.Length < 8) continue;
                rowCount++;

                double monoMs = double.NaN;
                if (double.TryParse(p[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedMono))
                {
                    monoMs = parsedMono;
                    if (double.IsNaN(firstTs)) firstTs = monoMs;
                    lastTs = monoMs;
                }

                if (long.TryParse(p[0], out var frameIdx) && frameIdx > 0 &&
                    double.TryParse(p[5], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var intMs) &&
                    intMs > 0)
                {
                    intervals.Add(intMs);
                }

                if (p[7] == "1")
                {
                    gapCount++;
                    double relMs = !double.IsNaN(firstTs) && !double.IsNaN(monoMs) ? monoMs - firstTs : warmupMs;
                    if (relMs < warmupMs) startupGapCount++;
                    else                 midGapCount++;
                }
            }

            if (rowCount == 0) return new TimestampCsvStats { RowCount = 0 };

            double durationMs = (!double.IsNaN(firstTs) && !double.IsNaN(lastTs)) ? lastTs - firstTs : 0;
            double estFps = durationMs > 0 && rowCount > 1 ? (rowCount - 1) / (durationMs / 1000.0) : 0;

            double mean = 0, median = 0, min = 0, max = 0, p95 = 0, p99 = 0, std = 0, cv = 0;
            if (intervals.Count > 0)
            {
                intervals.Sort();
                mean   = intervals.Average();
                min    = intervals[0];
                max    = intervals[^1];
                median = intervals[intervals.Count / 2];
                p95    = intervals[Math.Min((int)(intervals.Count * 0.95), intervals.Count - 1)];
                p99    = intervals[Math.Min((int)(intervals.Count * 0.99), intervals.Count - 1)];
                if (intervals.Count > 1)
                {
                    var variance = intervals.Sum(v => (v - mean) * (v - mean)) / (intervals.Count - 1);
                    std = Math.Sqrt(variance);
                    cv  = mean > 0 ? std / mean * 100.0 : 0;
                }
            }

            return new TimestampCsvStats
            {
                RowCount           = rowCount,
                FirstTimestampMs   = double.IsNaN(firstTs) ? 0 : firstTs,
                LastTimestampMs    = double.IsNaN(lastTs)  ? 0 : lastTs,
                DurationMs         = durationMs,
                MeanIntervalMs     = mean,
                MedianIntervalMs   = median,
                MinIntervalMs      = min,
                MaxIntervalMs      = max,
                P95IntervalMs      = p95,
                P99IntervalMs      = p99,
                GapCount           = gapCount,
                StartupGapCount    = startupGapCount,
                MidSessionGapCount = midGapCount,
                IntervalStdMs      = std,
                CvPercent          = cv,
                EstimatedFps       = estFps,
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// Writes metadata.json and metadata.txt for a finished V2 recording slot.
    /// Text language follows _sessionLanguage captured at recording start.
    /// JSON keys remain English for machine compatibility; human-readable values are localized.
    /// </summary>
    private async Task WriteV2SlotMetadataAsync(
        int slot, RecordingFileSet fileSet, RecordingFinalizeResult result,
        TimestampCsvStats? tsStats,
        Diagnostics.FfprobeResult? ffprobe,
        Diagnostics.DuplicateDetectionResult? dup,
        Diagnostics.VisualQualityResult? vq,
        SessionVerificationStats sessionStats,
        Dictionary<int, Diagnostics.FfprobeResult>? allFfprobe = null,
        Dictionary<int, TimestampCsvStats?>? allTsStats = null)
    {
        try
        {
            // ── Raw data ─────────────────────────────────────────────────────
            var controlResults = _v2Engine.GetSlotControlResults(slot);
            var snap           = _v2Engine.GetSlotDiagnosticsSnapshot(slot);
            var device         = _v2Engine.GetSlotDevice(slot);
            var fmt            = _v2Engine.GetSlotFormatResult(slot);
            var slotName       = $"cam{slot + 1}";
            var now            = DateTime.Now;
            var layout         = _vm.State.CameraLayout;
            var isJa           = _sessionLanguage == "ja";
            var sessionFolder  = _v2SessionFolderPath ?? Path.GetDirectoryName(fileSet.CameraFolder) ?? "";
            var health         = snap.HealthSnapshot;
            var ver            = _vm.Version;
            var selFmt         = fmt?.SelectedFormat;
            var ic             = System.Globalization.CultureInfo.InvariantCulture;

            string J(string en, string jp) => isJa ? jp : en;

            // ── Resolve duration ─────────────────────────────────────────────
            // Priority: (1) stopwatch in result, (2) CSV first/last timestamps, (3) health session elapsed
            TimeSpan resolvedDuration;
            string durationSource;
            if (result.Duration > TimeSpan.Zero)
            {
                resolvedDuration = result.Duration;
                durationSource = J("Monotonic stopwatch", "単調時計");
            }
            else if (tsStats?.DurationMs > 0)
            {
                resolvedDuration = TimeSpan.FromMilliseconds(tsStats.DurationMs);
                durationSource = J("Frame timestamps (CSV)", "フレームタイムスタンプ（CSV）");
            }
            else if ((health?.SessionElapsed ?? TimeSpan.Zero) > TimeSpan.Zero)
            {
                resolvedDuration = health!.SessionElapsed;
                durationSource = J("Session elapsed (approximate)", "セッション経過時間（概算）");
            }
            else
            {
                resolvedDuration = TimeSpan.Zero;
                durationSource = J("Unknown", "不明");
            }

            bool durationValid = resolvedDuration > TimeSpan.Zero;
            var stopTime  = now;
            var startTime = durationValid ? now - resolvedDuration : now;

            // ── Control lookups ──────────────────────────────────────────────
            V2ControlApplyResult? FindCtrl(V2CameraControl c) =>
                controlResults.FirstOrDefault(x => x.Control == c);

            var focusCtrl    = FindCtrl(V2CameraControl.Focus);
            var exposureCtrl = FindCtrl(V2CameraControl.Exposure);
            var llcCtrl      = FindCtrl(V2CameraControl.LowLightCompensation);
            var digStabCtrl  = FindCtrl(V2CameraControl.OpticalStabilization);
            var flickerCtrl  = FindCtrl(V2CameraControl.FlickerReduction);

            // ── Control status helpers ───────────────────────────────────────
            string CtrlSupport(V2ControlApplyResult? c) => c?.ReadbackStatus switch
            {
                V2ControlReadbackStatus.Unsupported  => J("Not supported by device/driver", "デバイス/ドライバー非対応"),
                V2ControlReadbackStatus.NotAttempted => J("Unknown", "不明"),
                null                                 => J("Unknown", "不明"),
                _                                    => J("Supported", "対応"),
            };
            string CtrlResult2(V2ControlApplyResult? c) => c?.ReadbackStatus switch
            {
                V2ControlReadbackStatus.Confirmed    => J("Applied", "適用済み"),
                V2ControlReadbackStatus.Mismatch     => J("Applied but readback mismatch", "適用済み（読み取り値不一致）"),
                V2ControlReadbackStatus.Unsupported  => J("Not supported by device/driver", "デバイス/ドライバー非対応"),
                V2ControlReadbackStatus.Failed       => J("Failed", "失敗"),
                V2ControlReadbackStatus.NotAttempted => J("Not attempted", "未試行"),
                null                                 => J("Not attempted", "未試行"),
                _                                    => J("Unknown", "不明"),
            };
            string CtrlReadback(V2ControlApplyResult? c) => c?.ReadbackValue.HasValue == true
                ? c.ReadbackValue.Value.ToString("F4", ic)
                : J("Unavailable", "取得不可");
            // CameraControlManagerV2 (capture/video_engine_v2/) sets WarningMessage from a small set
            // of fixed, app-authored English strings when a control isn't supported/attempted — but
            // also from `error` (a raw COM/WinRT exception message) in several places, which has no
            // meaningful translation. Only the fixed strings are covered here; unrecognized text
            // (including any driver error message) passes through untranslated rather than guessing.
            string? LocalizeControlWarning(string? warning) => warning switch
            {
                null => null,
                "BacklightCompensation not supported on this device." =>
                    J(warning, "BacklightCompensationはこのデバイスでサポートされていません。"),
                "OpticalImageStabilizationControl not supported on this device." =>
                    J(warning, "OpticalImageStabilizationControlはこのデバイスでサポートされていません。"),
                "WhiteBalanceControl not supported on this device." =>
                    J(warning, "WhiteBalanceControlはこのデバイスでサポートされていません。"),
                "Flicker reduction disabled in VideoEngineSettings." =>
                    J(warning, "フリッカー低減はVideoEngineSettingsで無効化されています。"),
                "Control not applied in this build." =>
                    J(warning, "この製品版ではこのコントロールは適用されません。"),
                "CameraControlManagerV2 not attached to an open camera session." =>
                    J(warning, "CameraControlManagerV2が開いているカメラセッションに接続されていません。"),
                _ when warning.StartsWith("Skipped — exposure control unsupported", StringComparison.Ordinal) =>
                    J(warning, "スキップ — このデバイスでは露出制御が非対応のため、低照度補正を無効化しても画像が暗くなるだけで再現性の向上にはなりません。"),
                _ when warning.StartsWith("Flicker reduction is not exposed", StringComparison.Ordinal) =>
                    J(warning, "フリッカー低減はこのシステムのWinRTカメラ制御APIでは公開されていません（VideoDeviceController.FlickerReductionControlはインストール済みのWindows SDKメタデータに存在しません）。MultiCamAppから適用することはできません。"),
                _ => warning,
            };
            bool IsConfirmedApplied(V2ControlApplyResult? c) =>
                c?.ReadbackStatus == V2ControlReadbackStatus.Confirmed && c.Applied;
            bool IsConfirmedOff(V2ControlApplyResult? c) =>
                IsConfirmedApplied(c) && (c!.ReadbackValue ?? 1.0) < 0.5;

            // ── Motion blur risk ─────────────────────────────────────────────
            bool exposureFixed = IsConfirmedApplied(exposureCtrl);
            bool llcOff        = IsConfirmedOff(llcCtrl);

            string blurLevel, blurNote;
            if (!exposureFixed)
            {
                blurLevel = J("Unknown", "不明");
                blurNote  = J(
                    "Exposure/shutter state could not be confirmed by the camera driver. Motion blur may occur during fast movement even when FPS is stable.",
                    "カメラドライバーによる露出/シャッター状態の確認ができませんでした。FPSが安定していても高速な動きでモーションブラーが発生する可能性があります。");
            }
            else if (llcOff)
            {
                blurLevel = J("Low", "低");
                blurNote  = J(
                    "Fixed exposure confirmed. Low-light compensation confirmed off. Blur risk is minimal if shutter speed is appropriate for the scene.",
                    "固定露出確認済み。低照度補正オフ確認済み。シャッタースピードが適切であればブラーリスクは最小限です。");
            }
            else
            {
                blurLevel = J("Medium", "中");
                blurNote  = J(
                    "Fixed exposure confirmed, but low-light compensation state is uncertain. If LLC is active, it may lengthen shutter time, increasing motion blur risk.",
                    "固定露出確認済みですが、低照度補正の状態が不確実です。LLCが有効の場合シャッター時間が延長されモーションブラーリスクが高まる可能性があります。");
            }

            var llcConfirmedOffStr = llcOff
                ? J("Yes", "はい")
                : llcCtrl?.ReadbackStatus == V2ControlReadbackStatus.Confirmed
                    ? J("No (readback non-zero)", "いいえ（読み取り値が非ゼロ）")
                    : J("Unknown", "不明");

            // ── File / finalization facts ────────────────────────────────────
            bool finalMp4Exists  = File.Exists(fileSet.FinalVideoPath);
            bool tempStillExists = File.Exists(fileSet.TempVideoPath);
            bool csvExists       = File.Exists(fileSet.TimestampCsvPath);
            long csvRows         = tsStats?.RowCount ?? result.TimestampCsvRows;
            // Use recording-relative counter when available (excludes preview frames before recording started).
            long recordingFrames = result.ResolveRecordingRelativeFrames(csvRows);
            bool framesOk        = recordingFrames > 0;
            // PreviewInclusive backends log extra rows before/after the recording boundary — frames
            // arrive and are logged to CSV during the ~1-1.8s async Start Recording encoder-setup
            // window (Previewing/StartingRecording state) but are only submitted to the encoder once
            // the state reaches exactly Recording. This surplus (csvRows > recordingFrames) is
            // therefore expected on every clean recording and does NOT indicate lost/dropped frames —
            // confirmed via ffprobe/exact-MD5 audits showing the real MP4 matches recordingFrames
            // exactly in every normal recording, even when this surplus is large. That is the common
            // case, not a hard guarantee: PostFinalizeFrameCountMismatch below is an independent
            // post-finalize recount of the real file specifically because a rare divergence between
            // the pipeline's own counter and the actual container is still possible (seen in practice
            // once — real file with MORE frames than the pipeline had tracked, see CHANGELOG v1.2.109).
            // Only a deficit (fewer CSV rows than frames actually submitted) reflects a genuine
            // problem, so it keeps the tight tolerance; surplus gets a generous allowance sized to the
            // measured frame rate instead of a flat percentage, since a fixed ~1-1.8s pre-roll is a
            // much bigger fraction of a higher-resolution session's total frame count (whose encoder
            // setup takes longer).
            bool isPreviewInclusive = result.FrameCounterScope == "PreviewInclusive"
                && result.FramesSubmittedSinceRecordingStart > 0;
            long tolerance       = isPreviewInclusive
                ? Math.Max(20L, recordingFrames / 50L)
                : Math.Max(5L, recordingFrames / 100L);
            double measuredFpsForTolerance = tsStats?.EstimatedFps > 0 ? tsStats.EstimatedFps
                : selFmt?.NominalFps > 0 ? selFmt.NominalFps : 30.0;
            long surplusTolerance = isPreviewInclusive
                ? Math.Max(tolerance, (long)Math.Ceiling(measuredFpsForTolerance * 3.0))
                : tolerance;
            long csvVsRecordingDiff = csvRows - recordingFrames;
            bool framesMatchCsv  = csvRows > 0 && (csvVsRecordingDiff >= 0
                ? csvVsRecordingDiff <= surplusTolerance
                : -csvVsRecordingDiff <= tolerance);
            // Whether the preview-inclusive counter (FramesWritten) diverges from the CSV/recording-relative count.
            bool previewInclusiveDiffers = result.FrameCounterScope == "PreviewInclusive"
                && result.FramesSubmittedSinceRecordingStart > 0
                && Math.Abs(result.FramesWritten - csvRows) > Math.Max(5L, result.FramesWritten / 100L);

            long fileSizeBytes = 0;
            if (finalMp4Exists)
            {
                try { fileSizeBytes = new FileInfo(fileSet.FinalVideoPath).Length; } catch { }
            }
            string fileSizeStr = fileSizeBytes >= 1_073_741_824L ? $"{fileSizeBytes / 1_073_741_824.0:F2} GB"
                               : fileSizeBytes >= 1_048_576L     ? $"{fileSizeBytes / 1_048_576.0:F1} MB"
                               : fileSizeBytes > 0               ? $"{fileSizeBytes / 1024.0:F0} KB"
                               : J("Unknown", "不明");

            // ── Verification ─────────────────────────────────────────────────
            var verNotes = new List<string>();
            bool isFail = false, isWarn = false;

            if (!framesOk)
            { isFail = true; verNotes.Add(J("No frames were written.", "フレームが書き込まれませんでした。")); }
            if (framesOk && !durationValid)
            { isFail = true; verNotes.Add(J("Duration could not be determined even though frames were written.", "フレームが書き込まれましたが録画時間を特定できませんでした。")); }
            if (framesOk && !finalMp4Exists)
            { isFail = true; verNotes.Add(J("Final MP4 does not exist after recording stopped.", "録画停止後に最終MP4が存在しません。")); }
            if (framesOk && csvExists && csvRows == 0)
            { isFail = true; verNotes.Add(J("Timestamp CSV exists but is empty.", "タイムスタンプCSVが存在しますが空です。")); }

            if (!isFail)
            {
                if (!exposureFixed)
                { isWarn = true; verNotes.Add(J("Exposure state could not be confirmed by driver.", "ドライバーによる露出状態の確認ができませんでした。")); }
                if (!llcOff)
                { isWarn = true; verNotes.Add(J("Low-light compensation state could not be confirmed off.", "低照度補正のオフ確認ができませんでした。")); }
                if (csvExists && csvRows > 0 && !framesMatchCsv)
                { isWarn = true; verNotes.Add(J($"Timestamp CSV rows ({csvRows}) do not closely match recorded frames ({recordingFrames}).", $"タイムスタンプCSVの行数（{csvRows}）と録画フレーム数（{recordingFrames}）が一致しません。")); }
                // The comment above recordingFrames claims the real MP4 "always" matches it exactly —
                // true in every normal recording, but PostFinalizeFrameCountMismatch exists precisely
                // because it isn't a hard guarantee: an independent post-finalize ffprobe recount can
                // still find the final container has a different frame count than the pipeline's own
                // encoder-submission counter (seen in practice: real file with MORE frames than the
                // pipeline tracked, i.e. frames the timestamp CSV never got an entry for). That's a
                // genuine scientific-accuracy gap for those frames, not merely cosmetic, so it warrants
                // the same WARN treatment as a CSV/frame mismatch above rather than staying buried in
                // frameIntegrity.postFinalizeFrameCountMismatch where no verdict ever reads it.
                if (result.PostFinalizeFrameCountMismatch && result.PostFinalizeProbedFrameCount is { } probedForNote)
                {
                    isWarn = true;
                    verNotes.Add(J(
                        $"Post-recording frame verification found a mismatch: encoder tracked {result.FramesSubmittedSinceRecordingStart} frames submitted, but the final file actually contains {probedForNote} (diff={probedForNote - result.FramesSubmittedSinceRecordingStart}). Recorded content is valid and playable, but some frames have no corresponding Timestamp CSV entry.",
                        $"録画後フレーム検証で不一致を検出: エンコーダーは{result.FramesSubmittedSinceRecordingStart}フレーム送信と記録しましたが、最終ファイルには実際{probedForNote}フレーム含まれています（差={probedForNote - result.FramesSubmittedSinceRecordingStart}）。録画内容は有効で再生可能ですが、一部のフレームに対応するタイムスタンプCSVの記録がありません。"));
                }
                // Informational only — preview-inclusive counter diverges from recording content but CSV and recording-relative count agree.
                if (!isFail && framesMatchCsv && previewInclusiveDiffers)
                { verNotes.Add(J($"Preview-inclusive frame counter ({result.FramesWritten}) differs from recording-only timestamp/MP4 frames; recorded content is valid.", $"プレビュー込みフレームカウンター（{result.FramesWritten}）は録画専用タイムスタンプ/MP4フレーム数と異なりますが、録画内容は有効です。")); }
                if (fmt?.Kind is not null && fmt.Kind != V2FormatSelectionKind.ExactMatch)
                { isWarn = true; verNotes.Add(J($"Camera format fallback used: {fmt.FallbackReason}", $"カメラフォーマットフォールバック使用: {fmt.FallbackReason}")); }
                if (result.Duration == TimeSpan.Zero && durationValid)
                { isWarn = true; verNotes.Add(J("Duration was estimated from frame timestamps or session elapsed, not a direct recording stopwatch.", "録画時間はフレームタイムスタンプまたはセッション経過時間から推定されました。")); }
                if (!csvExists)
                { isWarn = true; verNotes.Add(J("Timestamp CSV was not written.", "タイムスタンプCSVが書き込まれませんでした。")); }
                if (ffprobe?.Available != true)
                {
                    // Only escalate to warning if ffprobe was attempted and failed; if it was simply not run
                    // (analysis removed in v1.2.22-alpha), treat it as informational only.
                    var ffprobeMsg = ffprobe?.ErrorMessage;
                    bool ffprobeFailed = !string.IsNullOrEmpty(ffprobeMsg)
                        && !string.Equals(ffprobeMsg, "not run", StringComparison.OrdinalIgnoreCase);
                    if (ffprobeFailed) isWarn = true;
                    verNotes.Add(J($"ffprobe container audit not available: {ffprobeMsg ?? "not run"}", $"ffprobeコンテナ監査利用不可: {ffprobeMsg ?? "未実行"}"));
                }
            }

            if (!verNotes.Any())
                verNotes.Add(J("All checks passed.", "すべてのチェックを通過しました。"));

            string sessionResult       = isFail ? "FAIL" : isWarn ? "PASS_WITH_WARNING" : "PASS";
            bool hasCsvData            = tsStats is not null && tsStats.RowCount > 0;
            bool hasFfprobeData        = ffprobe?.Available == true;
            string timingConf          = !durationValid ? "FAIL"
                : hasCsvData || hasFfprobeData ? "PASS"
                : "PASS_WITH_WARNING";
            string finalizationStatus  = finalMp4Exists ? "PASS" : framesOk ? "FAIL" : "PASS";
            string timestampStatus     = !csvExists ? "PASS_WITH_WARNING" : csvRows == 0 ? "FAIL" : framesMatchCsv ? "PASS" : "PASS_WITH_WARNING";
            string metadataConsistency = isFail ? "FAIL" : isWarn ? "PASS_WITH_WARNING" : "PASS";
            string dupStatus           = dup?.Implemented == true
                ? (dup.NearIdenticalFrames == 0 ? "PASS" : "PASS_WITH_WARNING")
                : "PASS_WITH_WARNING";

            // ── Format strings ───────────────────────────────────────────────
            var resStr        = selFmt is not null ? $"{selFmt.Width}x{selFmt.Height}" : J("Unknown", "不明");
            var reqFpsStr     = selFmt?.NominalFps > 0 ? selFmt.NominalFps.ToString("F0", ic) : J("Unknown", "不明");
            // Previously hardcoded to "1080p"/"1920x1080" regardless of what was actually
            // requested — every session's metadata claimed 1080p even for 720p/360p recordings.
            // VideoEngineSettings.DefaultPreferredWidth/Height is the same source OpenAsync itself
            // reads from when no explicit formatRequest is passed (the normal preview/record path).
            var requestedResolutionWxH =
                $"{VideoEngineSettings.DefaultPreferredWidth}x{VideoEngineSettings.DefaultPreferredHeight}";
            var requestedResolutionLabel = CaptureResolutionPreset.FormatDisplayLabel(
                null, VideoEngineSettings.DefaultPreferredWidth, VideoEngineSettings.DefaultPreferredHeight);
            // Same bug pattern as resolution above, for FPS: the "Requested FPS"/"Requested frame
            // rate" fields (in [Video Settings]/[Camera], NOT [Timing Models]) previously reused
            // the same selFmt-derived value as "Selected camera format FPS"/"Writer/container FPS"
            // — meaning all three fields always showed the identical number, and a user who
            // requested 60/100/120 fps on a camera that fell back to 30fps would see "Requested
            // FPS: 30", hiding that a fallback happened. The [Timing Models] section's own
            // "Requested FPS" intentionally keeps using the negotiated `requestedFps` variable —
            // it feeds directly into the adjacent nominal-duration math (frames / requestedFps)
            // and must stay self-consistent with that calculation, not the true original request.
            var trueRequestedFpsDisplay = VideoEngineSettings.DefaultPreferredFps.ToString("F0", ic);
            var gpuStr        = snap.Direct3DAvailability switch
            {
                V2CapabilityAvailability.Available   => J("Available",     "利用可能"),
                V2CapabilityAvailability.Unavailable => J("Not available", "利用不可"),
                _                                    => J("Unknown",       "不明"),
            };
            var hwEncoderStr  = result.HardwareEncoderUsed ? J("Used", "使用") : J("Not used", "未使用");
            var fallbackStr   = result.HardwareEncoderUsed ? J("No", "いいえ") : J("Yes (software encoder)", "はい（ソフトウェアエンコーダー）");
            var fmtFallbackUsed = fmt?.Kind switch
            {
                V2FormatSelectionKind.ExactMatch          => J("No", "いいえ"),
                V2FormatSelectionKind.PriorityFallback    => J("Yes (priority fallback)", "はい（優先フォールバック）"),
                V2FormatSelectionKind.ClosestFallback     => J("Yes (closest fallback)", "はい（最近接フォールバック）"),
                V2FormatSelectionKind.NoFormatsAvailable  => J("Yes (no formats available)", "はい（利用可能フォーマットなし）"),
                _ => J("Unknown", "不明"),
            };
            var fmtFallbackReason = fmt?.FallbackReason ?? J("None", "なし");
            var activeSlotList = string.Join(", ", Enumerable.Range(0, layout)
                .Where(i => !string.IsNullOrEmpty(_vm.SelectedDeviceIds[i]))
                .Select(i => $"cam{i + 1}"));
            const int previewFpsLimit = 0; // no throttle — preview runs at full camera rate
            var durationStr        = durationValid ? $"{resolvedDuration.TotalSeconds:F2} s" : J("Unknown", "不明");
            var tsDurStr           = tsStats?.DurationMs > 0 ? $"{tsStats.DurationMs / 1000.0:F2} s" : J("Unknown", "不明");
            var tsEstFpsStr        = tsStats?.EstimatedFps > 0 ? tsStats.EstimatedFps.ToString("F2", ic) : J("Unknown", "不明");
            string FmtMs(double ms) => ms.ToString("F2", ic) + " ms";
            string NA() => J("Not available", "利用不可");
            var ffprobeAvgFpsStr   = hasFfprobeData ? ffprobe!.AvgFpsNumeric.ToString("F2", ic) : NA();
            var ffprobeRFpsStr     = hasFfprobeData ? (ffprobe!.RFrameRate  ?? NA()) : NA();
            var ffprobeDurStr      = hasFfprobeData ? $"{ffprobe!.ContainerDurationS:F2} s" : NA();
            var ffprobeNbFrStr     = hasFfprobeData && ffprobe!.NbFrames.HasValue ? ffprobe.NbFrames.Value.ToString() : NA();
            var ffprobeBrStr       = hasFfprobeData && ffprobe!.BitRateBps.HasValue ? $"{ffprobe.BitRateBps.Value / 1000} kbps" : NA();
            var ffprobeCfrStr      = hasFfprobeData ? (ffprobe!.ConstantFrameRate ? J("Yes", "はい") : J("No", "いいえ")) : NA();
            var ffprobeCodecStr    = hasFfprobeData ? (ffprobe!.Codec       ?? NA()) : NA();
            var ffprobePixFmtStr   = hasFfprobeData ? (ffprobe!.PixelFormat ?? NA()) : NA();
            var ffprobeResStr      = hasFfprobeData && (ffprobe!.Width > 0 || ffprobe.Height > 0)
                ? $"{ffprobe.Width}x{ffprobe.Height}" : NA();

            // ── Timing reference camera ──────────────────────────────────────
            var (timingRefSlot, timingRefReason, cfrStatusMap) = ComputeTimingReferenceCamera(
                sessionStats.ActiveSlots,
                allFfprobe ?? new Dictionary<int, Diagnostics.FfprobeResult>(),
                allTsStats);
            var timingRefName = $"cam{timingRefSlot + 1}";

            // ── VFR driver behavior detection ────────────────────────────────
            bool isDriverVfr = hasFfprobeData
                && !ffprobe!.ConstantFrameRate
                && ParseRFrameRateNumerator(ffprobe.RFrameRate) > 40
                && ffprobe.AvgFpsNumeric > 0
                && ffprobe.AvgFpsNumeric < 35;
            string vfrBehavior = isDriverVfr ? "DriverVfr"
                : (hasFfprobeData && !ffprobe!.ConstantFrameRate) ? "VFR" : "CFR";
            string vfrNote = isDriverVfr
                ? J($"Camera driver reports r_frame_rate={ffprobe!.RFrameRate} but avg delivery is {ffprobe.AvgFpsNumeric:F2} fps. This is a driver-level VFR artifact, not a recording failure. Timestamp CSV is the authoritative timing source.",
                    $"カメラドライバーがr_frame_rate={ffprobe!.RFrameRate}を報告していますが平均{ffprobe.AvgFpsNumeric:F2}fpsで配信されています。これはドライバーレベルのVFRアーティファクトであり録画失敗ではありません。タイムスタンプCSVが信頼できるタイミングソースです。")
                : "";

            // ── Startup settling analysis ─────────────────────────────────────
            int startupGaps = tsStats?.StartupGapCount ?? 0;
            int midGaps     = tsStats?.MidSessionGapCount ?? 0;
            bool stableAfterWarmup = midGaps == 0 && tsStats is not null && tsStats.RowCount > 0;
            string settlingVerdict = tsStats is null ? J("Not available", "利用不可")
                : (startupGaps == 0 && midGaps == 0) ? J("PASS", "合格")
                : stableAfterWarmup ? J("PASS_WITH_WARNING (gaps in startup warmup only)", "要注意合格（ウォームアップ期間のみのギャップ）")
                : J("PASS_WITH_WARNING (gaps detected mid-session)", "要注意合格（セッション中のギャップを検出）");

            // ── Bitrate profile ───────────────────────────────────────────────
            var activeBitrateProfile = Capture.VideoEngineV2.VideoEngineSettings.BitrateProfile;
            int activeBitrateKbps    = Capture.VideoEngineV2.VideoEngineSettings.TargetBitrateKbps;

            // ── Timing classification ─────────────────────────────────────────
            double requestedFps = selFmt?.NominalFps ?? 30.0;
            var (timingLabels, timingPrimaryLabel, timingClassNote) =
                ClassifyTimingBehavior(tsStats, ffprobe, requestedFps, isJa);

            // ── Timing models ─────────────────────────────────────────────────
            // Use recording-relative frames (excludes preview-inclusive overhead) for the frame-count model.
            double frameCountDurationS = requestedFps > 0 ? recordingFrames / requestedFps : 0;
            double appTsDurationS      = (tsStats?.DurationMs ?? 0) / 1000.0;
            double containerDurationS  = hasFfprobeData ? ffprobe!.ContainerDurationS : 0;
            double internalClockS      = result.Duration.TotalSeconds;
            double containerVsAppDiffS = (containerDurationS > 0 && appTsDurationS > 0)
                ? containerDurationS - appTsDurationS : 0;
            double frameCountVsAppDiffS = (frameCountDurationS > 0 && appTsDurationS > 0)
                ? frameCountDurationS - appTsDurationS : 0;
            double measuredAvgFps      = tsStats?.EstimatedFps ?? 0;

            // ── Exposure / FPS safety warning ─────────────────────────────────
            bool fpsBelowRequested = measuredAvgFps > 0 && measuredAvgFps < requestedFps * 0.9;
            bool exposureStateUnknown = !exposureFixed;
            string fpsSafetyWarning = fpsBelowRequested
                ? J(exposureStateUnknown
                    ? $"Measured FPS ({measuredAvgFps:F2}) is lower than requested FPS ({requestedFps:F0}). This may be caused by camera driver timing, auto exposure, long exposure, low-light compensation, USB bandwidth, or device firmware behavior."
                    : $"Measured FPS ({measuredAvgFps:F2}) is lower than requested FPS ({requestedFps:F0}). Exposure is fixed; check camera driver timing or USB bandwidth.",
                  exposureStateUnknown
                    ? $"測定FPS（{measuredAvgFps:F2}）が要求FPS（{requestedFps:F0}）を下回っています。これはカメラドライバーのタイミング、自動露出、長露光、低照度補正、USB帯域幅またはファームウェアの動作が原因の可能性があります。"
                    : $"測定FPS（{measuredAvgFps:F2}）が要求FPS（{requestedFps:F0}）を下回っています。露出は固定されています。カメラドライバーのタイミングまたはUSB帯域幅を確認してください。")
                : "";

            // ── Build TXT ────────────────────────────────────────────────────
            var lines = new List<string>(160);

            void L(string label, string value) => lines.Add($"- {label}: {value}");
            void Sec(string name) { lines.Add(""); lines.Add(name); }

            lines.Add(J("MULTICAMAPP RECORDING SUMMARY", "MULTICAMAPP 録画サマリー"));
            lines.Add($"{J("Generated", "生成日時")}: {now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"{J("App version", "アプリバージョン")}: {ver.Version}");
            lines.Add($"{J("Build", "ビルド")}: {ver.Build}");
            lines.Add($"{J("Release stage", "リリースステージ")}: {ver.Stage}");

            Sec(J("[Session]", "[セッション]"));
            L(J("Session name", "セッション名"),     SessionBox.Text);
            L(J("Session folder", "セッションフォルダ"), Path.GetFileName(sessionFolder));
            L(J("Recording date", "録画日"),         now.ToString("yyyy-MM-dd"));
            L(J("Recording start time", "録画開始時刻"), startTime.ToString("HH:mm:ss"));
            L(J("Recording stop time", "録画停止時刻"),  stopTime.ToString("HH:mm:ss"));
            L(J("Active cameras", "アクティブカメラ数"), layout.ToString());
            L(J("Active slots", "アクティブスロット"),   activeSlotList);
            L(J("Engine", "エンジン"),               "VideoEngineV2");
            L(J("Metadata language", "メタデータ言語"), isJa ? "Japanese" : "English");

            Sec(J("[Recording Engine]", "[録画エンジン]"));
            L(J("Recording engine", "録画エンジン"),       "VideoEngineV2");
            L(J("Backend", "バックエンド"),               "MediaFoundation");
            L(J("Renderer", "レンダラー"),                _v2Engine.GetSlotPreviewRenderer(slot) is PreviewRendererType.Direct3D or PreviewRendererType.Direct3D11
                                                              ? "Direct3D11 (GPU)"
                                                              : "WPF (software)");
            L(J("Encoder", "エンコーダー"),               "MediaFoundation H.264");
            L(J("Hardware encoder", "ハードウェアエンコーダー"), hwEncoderStr);
            L(J("GPU acceleration", "GPU アクセラレーション"), gpuStr);
            L(J("Fallback mode used", "フォールバックモード使用"), fallbackStr);
            L(J("Preview FPS independent from recording FPS", "プレビューFPSは録画FPSから独立"), J("Yes", "はい"));

            Sec(J("[Video Settings]", "[動画設定]"));
            L(J("Requested resolution", "要求解像度"),    requestedResolutionLabel);
            L(J("Requested frame rate", "要求フレームレート"), trueRequestedFpsDisplay + " fps");
            L(J("Preview FPS mode during recording", "録画中プレビューFPSモード"), J("Balanced", "バランス"));
            L(J("Preview target FPS during recording", "録画中プレビュー目標FPS"), previewFpsLimit + " fps");
            L(J("Recording timing mode", "録画タイミングモード"), J("Original real-frame capture", "オリジナルリアルフレームキャプチャ"));
            L(J("Frame timing mode", "フレームタイミングモード"),
                J("Variable (real per-frame timestamps)", "可変（実際のフレームごとのタイムスタンプ）"));

            Sec(J($"[Camera — {slotName}]", $"[カメラ — {slotName}]"));
            L(J("Device", "デバイス"),                   device?.FriendlyName ?? J("Unknown", "不明"));
            L(J("Requested resolution", "要求解像度"),    requestedResolutionWxH);
            L(J("Selected resolution", "選択解像度"),     resStr);
            L(J("Requested FPS", "要求FPS"),             trueRequestedFpsDisplay);
            L(J("Selected camera format FPS", "選択カメラフォーマットFPS"), reqFpsStr);
            L(J("Writer/container FPS", "ライター/コンテナFPS"), reqFpsStr);
            L(J("ffprobe average FPS", "ffprobe 平均FPS"), ffprobeAvgFpsStr);
            L(J("ffprobe raw/r_frame_rate", "ffprobe 生FPS"), ffprobeRFpsStr);
            L(J("Constant frame rate", "定数フレームレート"), ffprobeCfrStr);
            L(J("Format fallback used", "フォーマットフォールバック使用"), fmtFallbackUsed);
            L(J("Fallback reason", "フォールバック理由"), fmtFallbackReason);

            Sec(J($"[Camera Controls — {slotName}]", $"[カメラ制御 — {slotName}]"));

            // Focus
            L(J("Focus control", "フォーカス制御"),                 CtrlSupport(focusCtrl));
            lines.Add("");
            L(J("Autofocus control", "オートフォーカス制御"),        CtrlSupport(focusCtrl));
            L(J("Autofocus requested", "オートフォーカス要求"),       J("Off", "オフ"));
            L(J("Autofocus result", "オートフォーカス結果"),          CtrlResult2(focusCtrl));
            lines.Add("");
            L(J("Manual focus requested", "マニュアルフォーカス要求"),   J("No", "いいえ"));
            L(J("Manual focus value requested", "マニュアルフォーカス値"), J("None", "なし"));
            L(J("Manual focus result", "マニュアルフォーカス結果"),       J("Not requested", "未要求"));
            L(J("Manual focus readback", "マニュアルフォーカス読み取り"),  J("Unavailable", "取得不可"));
            lines.Add("");
            L(J("Default focus restore requested", "フォーカスデフォルト復元要求"), J("Yes (at session open)", "はい（セッション開始時）"));
            L(J("Default focus restore result", "フォーカスデフォルト復元結果"),    CtrlResult2(focusCtrl));
            lines.Add("");

            // Exposure
            L(J("Exposure control", "露出制御"),              CtrlSupport(exposureCtrl));
            lines.Add("");
            L(J("Auto exposure control", "自動露出制御"),      CtrlSupport(exposureCtrl));
            L(J("Auto exposure requested", "自動露出要求"),     J("Off", "オフ"));
            L(J("Auto exposure result", "自動露出結果"),        CtrlResult2(exposureCtrl));
            lines.Add("");
            L(J("Manual exposure requested", "マニュアル露出要求"),   J("No", "いいえ"));
            L(J("Manual exposure value requested", "マニュアル露出値"), J("None", "なし"));
            L(J("Manual exposure result", "マニュアル露出結果"),       J("Not requested", "未要求"));
            L(J("Manual exposure readback", "マニュアル露出読み取り"),  J("Unavailable", "取得不可"));
            lines.Add("");
            L(J("Default exposure restore requested", "露出デフォルト復元要求"), J("Yes (at session open)", "はい（セッション開始時）"));
            L(J("Default exposure restore result", "露出デフォルト復元結果"),    CtrlResult2(exposureCtrl));
            lines.Add("");

            // LLC
            L(J("Low-light compensation control", "低照度補正制御"),                CtrlSupport(llcCtrl));
            L(J("Low-light compensation disable requested", "低照度補正無効要求"),   J("Yes", "はい"));
            L(J("Low-light compensation result", "低照度補正結果"),                  CtrlResult2(llcCtrl));
            L(J("Low-light compensation readback", "低照度補正読み取り"),             CtrlReadback(llcCtrl));
            L(J("Low-light compensation confirmed off", "低照度補正オフ確認"),        llcConfirmedOffStr);
            if (!llcOff && llcCtrl is not null)
                lines.Add(isJa
                    ? $"  * 警告: 低照度補正無効要求が{(llcCtrl.Applied ? "適用されました" : "失敗しました")}; 読み取り値のみでは確認状態として扱いません。"
                    : $"  * Warning: Low-light compensation disable {(llcCtrl.Applied ? "applied" : "failed")}; readback value alone is not treated as confirmed state.");
            lines.Add("");

            // Digital stabilization
            L(J("Digital stabilization control", "デジタル手ぶれ補正制御"), CtrlSupport(digStabCtrl));
            L(J("Digital stabilization result", "デジタル手ぶれ補正結果"),  CtrlResult2(digStabCtrl));
            lines.Add("");

            // Flicker reduction
            L(J("Flicker reduction control", "フリッカー低減制御"), CtrlSupport(flickerCtrl));
            L(J("Flicker reduction result", "フリッカー低減結果"),  CtrlResult2(flickerCtrl));
            if (flickerCtrl?.WarningMessage is not null)
            {
                // WarningMessage comes from the backend (CameraControlManagerV2) as a fixed
                // English string — unlike LLC's warning above, it isn't parameterized, so a
                // direct known-string translation is safe here instead of emitting it raw.
                var flickerMsg = isJa && flickerCtrl.WarningMessage.StartsWith("Flicker reduction is not exposed", StringComparison.Ordinal)
                    ? "フリッカー低減はこのシステムのWinRTカメラ制御APIでは公開されていません（VideoDeviceController.FlickerReductionControlはインストール済みのWindows SDKメタデータに存在しません）。MultiCamAppから適用することはできません。"
                    : flickerCtrl.WarningMessage;
                lines.Add(isJa ? $"  * 警告: {flickerMsg}" : $"  * Warning: {flickerMsg}");
            }
            lines.Add("");
            lines.Add(J("- Camera control readback limitations:", "- カメラ制御読み取り制限:"));
            lines.Add(J(
                "  * Exposure, gain, brightness, focus, and low-light readback depend on camera driver support.",
                "  * 露出・ゲイン・輝度・フォーカス・低照度補正の読み取りはカメラドライバーサポートに依存します。"));
            lines.Add(J(
                "  * Unknown/Unavailable means the current Windows camera API path did not expose the value.",
                "  * 不明/取得不可はWindows Camera APIパスで値が公開されなかったことを意味します。"));
            lines.Add(J(
                "  * Unknown does not mean the setting failed; it means readback confirmation was not available.",
                "  * 不明は設定が失敗したことではなく、読み取り確認が利用できなかったことを意味します。"));

            // ── Environmental Lock section ────────────────────────────────────
            Sec(J($"[Environmental Lock — {slotName}]", $"[環境ロック — {slotName}]"));
            var lockRes = slot == 0 ? _lastEnvLockResult : null; // lock is per-slot; currently only slot 0 has the UI
            L(J("Environmental lock active at recording start", "録画開始時の環境ロック状態"),
                _envLocked ? J("ACTIVE — hardware parameters frozen for dataset purity", "有効 — データセット純粋性のためハードウェアパラメーターを固定") : J("NOT ACTIVE — auto modes may have adapted", "無効 — 自動モードが適応した可能性あり"));
            if (lockRes is not null)
            {
                L(J("Focus hardware locked", "フォーカスハードウェアロック"),       lockRes.FocusLocked    ? J($"Yes (at {lockRes.FocusLockedAt} steps)", $"はい（{lockRes.FocusLockedAt}ステップ）") : J("No/unsupported", "いいえ/非対応"));
                L(J("Exposure hardware locked", "露出ハードウェアロック"),          lockRes.ExposureLocked ? J($"Yes (at {lockRes.ExposureLockedAtS * 1000:F1} ms shutter)", $"はい（シャッター {lockRes.ExposureLockedAtS * 1000:F1} ms）") : J("No/unsupported", "いいえ/非対応"));
                L(J("White balance hardware locked", "ホワイトバランスハードウェアロック"), lockRes.WhiteBalanceLocked ? J($"Yes (at {lockRes.WhiteBalanceLockedAtK} K)", $"はい（{lockRes.WhiteBalanceLockedAtK} K）") : J("No/unsupported", "いいえ/非対応"));
                L(J("ISO/gain hardware locked", "ISO/ゲインハードウェアロック"),    lockRes.IsoLocked ? J("Yes (fixed)", "はい（固定）") : J("No/unsupported", "いいえ/非対応"));
                if (lockRes.Warning is not null)
                    L(J("Lock warning", "ロック警告"), lockRes.Warning);
            }

            Sec(J($"[Recording — {slotName}]", $"[録画 — {slotName}]"));
            L(J("Recording started", "録画開始"),             framesOk      ? J("Yes", "はい") : J("No", "いいえ"));
            L(J("Recording finalized", "録画ファイル確定"),    finalMp4Exists ? J("Yes", "はい") : J("No", "いいえ"));
            L(J("Temporary file used", "一時ファイル使用"),    J("Yes", "はい"));
            L(J("Temporary file renamed to final MP4", "一時ファイルを最終MP4にリネーム"),
                fileSet.IsFinalized ? J("Yes", "はい") :
                tempStillExists     ? J("No (temp still exists)", "いいえ（一時ファイルが残存）") :
                                      J("No (temp missing)", "いいえ（一時ファイルが見つからない）"));
            L(J("Output file", "出力ファイル"),   Path.GetFileName(fileSet.FinalVideoPath));
            L(J("File size", "ファイルサイズ"),   fileSizeStr);
            L(J("Writer status", "ライター状態"), J(result.Status.ToString(), result.Status switch
            {
                Recording.Writers.RecordingFinalizeStatus.Success             => "成功",
                Recording.Writers.RecordingFinalizeStatus.SuccessWithWarnings => "成功（警告あり）",
                Recording.Writers.RecordingFinalizeStatus.EncoderFailed       => "エンコーダー失敗",
                Recording.Writers.RecordingFinalizeStatus.RenameFailed        => "リネーム失敗",
                Recording.Writers.RecordingFinalizeStatus.Cancelled           => "キャンセル",
                _ => result.Status.ToString(),
            }));
            L(J("Failure reason", "失敗理由"),    result.FailureReason ?? J("None", "なし"));

            Sec(J($"[Timing — {slotName}]", $"[タイミング — {slotName}]"));
            L(J("Frames written (preview-inclusive)", "書き込みフレーム数（プレビュー込み）"), result.FramesWritten.ToString());
            if (result.FramesSubmittedSinceRecordingStart > 0)
            {
                L(J("Frames submitted since recording start", "録画開始からのフレーム送信数"), result.FramesSubmittedSinceRecordingStart.ToString());
                L(J("Frame counter scope", "フレームカウンタースコープ"),
                    J("PreviewInclusive (Frames written includes frames before recording started)",
                      "プレビュー込み（録画開始前のフレームを含む）"));
            }
            // WriteSample only throws on a failure HRESULT — a success return only means Media
            // Foundation accepted the sample into its internal queue, not that it survived into the
            // final container. This independent ffprobe recount (run once, right after finalize)
            // catches the rare case where frames are silently dropped downstream of WriteSample
            // with no exception ever raised — see RecordingFinalizeResult.PostFinalizeProbedFrameCount.
            if (result.PostFinalizeProbedFrameCount is { } probedCount)
            {
                L(J("Post-recording frame verification", "録画後フレーム検証"),
                    result.PostFinalizeFrameCountMismatch
                        ? J($"MISMATCH — encoder reported {result.FramesSubmittedSinceRecordingStart} frames submitted, but only {probedCount} confirmed in the final file (diff={result.FramesSubmittedSinceRecordingStart - probedCount})",
                            $"不一致 — エンコーダーは{result.FramesSubmittedSinceRecordingStart}フレーム送信と報告しましたが、最終ファイルでは{probedCount}フレームしか確認できませんでした（差={result.FramesSubmittedSinceRecordingStart - probedCount}）")
                        : J("Match — all submitted frames confirmed in the final file", "一致 — 送信された全フレームが最終ファイルで確認されました"));
            }
            L(J("Timestamp CSV", "タイムスタンプCSV"),             csvExists ? J("Written", "書き込み済み") : J("Not written", "未書き込み"));
            L(J("Timestamp CSV file", "タイムスタンプCSVファイル"), csvExists ? Path.GetFileName(fileSet.TimestampCsvPath) : J("None", "なし"));
            L(J("Timestamp rows", "タイムスタンプ行数"),            csvRows > 0 ? csvRows.ToString() : J("Unknown", "不明"));
            L(J("Timestamp rows match recorded frames", "タイムスタンプ行数と録画フレーム数の一致"),
                csvRows == 0 ? J("Unknown", "不明") : framesMatchCsv
                    ? J($"Yes (CSV={csvRows}, recording={recordingFrames})", $"はい（CSV={csvRows}、録画={recordingFrames}）")
                    : J($"No (CSV={csvRows}, recording={recordingFrames}, diff={csvRows - recordingFrames:+#;-#;0})", $"いいえ（CSV={csvRows}、録画={recordingFrames}）"));
            L(J("Monotonic duration", "単調時間"),          result.Duration > TimeSpan.Zero ? $"{result.Duration.TotalSeconds:F2} s" : J("Unknown", "不明"));
            L(J("Frame timestamp duration", "フレームタイムスタンプ時間"), tsDurStr);
            L(J("Container duration (ffprobe)", "コンテナ時間 (ffprobe)"), ffprobeDurStr);
            L(J("Duration source used", "使用した時間ソース"), durationSource);
            L(J("Duration status", "時間状態"),             timingConf);

            bool hasTs = tsStats is not null && tsStats.RowCount > 1;
            L(J("First frame timestamp", "最初のフレームタイムスタンプ"),  hasTs ? FmtMs(tsStats!.FirstTimestampMs) : J("Unknown", "不明"));
            L(J("Last frame timestamp", "最後のフレームタイムスタンプ"),    hasTs ? FmtMs(tsStats!.LastTimestampMs)  : J("Unknown", "不明"));
            L(J("Mean frame interval", "平均フレーム間隔"),                 hasTs ? FmtMs(tsStats!.MeanIntervalMs)   : J("Unknown", "不明"));
            L(J("Median frame interval", "中央値フレーム間隔"),              hasTs ? FmtMs(tsStats!.MedianIntervalMs) : J("Unknown", "不明"));
            L(J("Min frame interval", "最小フレーム間隔"),                  hasTs ? FmtMs(tsStats!.MinIntervalMs)    : J("Unknown", "不明"));
            L(J("Max frame interval", "最大フレーム間隔"),                  hasTs ? FmtMs(tsStats!.MaxIntervalMs)    : J("Unknown", "不明"));
            L(J("P95 frame interval", "P95フレーム間隔"),                   hasTs ? FmtMs(tsStats!.P95IntervalMs)    : J("Unknown", "不明"));
            L(J("P99 frame interval", "P99フレーム間隔"),                   hasTs ? FmtMs(tsStats!.P99IntervalMs)    : J("Unknown", "不明"));
            L(J("Timestamp gap count", "タイムスタンプギャップ数"),          hasTs ? tsStats!.GapCount.ToString()     : J("Unknown", "不明"));
            L(J("Estimated FPS from timestamps", "タイムスタンプからの推定FPS"), hasTs ? tsEstFpsStr : J("Unknown", "不明"));

            Sec(J($"[Timing Reference]", $"[タイミング基準]"));
            L(J("Reference camera", "基準カメラ"), timingRefName);
            L(J("Selection reason", "選択理由"), timingRefReason);
            foreach (var (s, status) in cfrStatusMap.OrderBy(x => x.Key))
                L(J($"CFR status (cam{s + 1})", $"CFR状態（cam{s + 1}）"), status);
            L(J("Authoritative timing source", "信頼できるタイミングソース"), J("Timestamp CSV (per-frame monotonic timestamps)", "タイムスタンプCSV（フレームごとの単調タイムスタンプ）"));

            if (isDriverVfr)
            {
                Sec(J($"[VFR Driver Behavior — {slotName}]", $"[VFRドライバー動作 — {slotName}]"));
                L(J("VFR behavior", "VFR動作"), vfrBehavior);
                L(J("r_frame_rate", "r_frame_rate"), ffprobe!.RFrameRate ?? NA());
                L(J("Average FPS delivered", "実際の平均FPS"), ffprobeAvgFpsStr);
                L(J("Constant frame rate", "定数フレームレート"), J("No (driver VFR)", "いいえ（ドライバーVFR）"));
                L(J("Timestamp CSV authoritative", "タイムスタンプCSVが信頼できる"), J("Yes", "はい"));
                lines.Add($"  * {vfrNote}");
            }

            Sec(J($"[Startup Settling — {slotName}]", $"[スタートアップ整定 — {slotName}]"));
            L(J("Warmup period", "ウォームアップ期間"), J("10 seconds (first 10s excluded from analysis)", "10秒（最初の10秒は分析から除外）"));
            L(J("Analysis safe interval", "分析安全区間"), J("After 10s from recording start", "録画開始から10秒後"));
            L(J("Startup gaps (first 10s)", "スタートアップギャップ（最初の10秒）"), tsStats is not null ? startupGaps.ToString() : NA());
            L(J("Mid-session gaps (after 10s)", "セッション中ギャップ（10秒後）"), tsStats is not null ? midGaps.ToString() : NA());
            L(J("Stable after warmup", "ウォームアップ後安定"), tsStats is not null ? J(stableAfterWarmup ? "Yes" : "No", stableAfterWarmup ? "はい" : "いいえ") : NA());
            L(J("Settling verdict", "整定判定"), settlingVerdict);
            if (startupGaps > 0 && stableAfterWarmup)
                lines.Add(J(
                    "  * Note: the camera/driver exhibits minor frame jitter during the first ~10s. Data after the warmup period is stable.",
                    "  * 注: カメラ/ドライバーは最初の約10秒間にわずかなフレームジッターを示します。ウォームアップ後のデータは安定しています。"));

            Sec(J($"[Timing Models — {slotName}]", $"[タイミングモデル — {slotName}]"));
            L(J("Requested FPS", "要求FPS"),                      $"{requestedFps:F0} fps");
            L(J("Container r_frame_rate", "コンテナ r_frame_rate"), hasFfprobeData ? (ffprobe!.RFrameRate ?? NA()) : NA());
            L(J("Container avg_frame_rate", "コンテナ avg_frame_rate"), ffprobeAvgFpsStr + " fps");
            L(J("Measured avg FPS from timestamps", "タイムスタンプからの平均FPS"), hasTs ? $"{measuredAvgFps:F2} fps" : NA());
            L(J("Frame-count duration (nominal)", "フレーム数ベース時間（名目）"),
                frameCountDurationS > 0 ? $"{frameCountDurationS:F3} s ({result.FramesWritten} frames / {requestedFps:F0} fps) — nominal estimate only" : NA());
            L(J("Container duration (player interpretation)", "コンテナ時間（プレーヤー解釈）"),
                containerDurationS > 0 ? $"{containerDurationS:F3} s — as interpreted by MP4 container / media player" : NA());
            L(J("App timestamp duration (primary audit)", "アプリタイムスタンプ時間（一次監査）"),
                appTsDurationS > 0 ? $"{appTsDurationS:F3} s — per-frame monotonic CSV; primary audit source" : NA());
            L(J("Internal clock duration (lifecycle timing)", "内部クロック時間（ライフサイクルタイミング）"),
                internalClockS > 0 ? $"{internalClockS:F3} s — recording stopwatch (start→stop)" : NA());
            L(J("Hardware camera presentation timestamp", "ハードウェアカメラプレゼンテーションタイムスタンプ"),
                J("Not used — app-side monotonic timestamp used instead", "未使用 — アプリ側の単調タイムスタンプを使用"));
            if (containerDurationS > 0 && appTsDurationS > 0)
                L(J("Container vs app timestamp diff", "コンテナ vs タイムスタンプ差"), $"{containerVsAppDiffS:+0.000;-0.000;0.000} s");
            if (frameCountDurationS > 0 && appTsDurationS > 0)
                L(J("Frame-count vs app timestamp diff", "フレーム数 vs タイムスタンプ差"), $"{frameCountVsAppDiffS:+0.000;-0.000;0.000} s");
            if (hasTs)
            {
                L(J("Interval std dev", "間隔標準偏差"), $"{tsStats!.IntervalStdMs:F2} ms");
                L(J("Interval CV%", "変動係数"), $"{tsStats.CvPercent:F1}%");
                L(J("Max frame gap", "最大フレームギャップ"), $"{tsStats.MaxIntervalMs:F2} ms");
            }
            if (!string.IsNullOrEmpty(fpsSafetyWarning))
                lines.Add($"  * {fpsSafetyWarning}");

            Sec(J($"[Timing Classification — {slotName}]", $"[タイミング分類 — {slotName}]"));
            L(J("Primary classification", "主分類"), timingPrimaryLabel);
            L(J("All classifications", "全分類"), string.Join(", ", timingLabels));
            lines.Add($"  * {timingClassNote}");

            Sec(J($"[Timestamp Source — {slotName}]", $"[タイムスタンプソース — {slotName}]"));
            L(J("Timestamp source", "タイムスタンプソース"), J("AppMonotonicClock", "アプリ単調クロック"));
            L(J("Clock implementation", "クロック実装"), "Stopwatch.GetTimestamp / Stopwatch.Frequency");
            L(J("Timestamp origin", "タイムスタンプ基点"), J("Recording start", "録画開始"));
            L(J("Timestamp capture point", "タイムスタンプ取得タイミング"),
                J("CSV row write time on background frame thread", "バックグラウンドフレームスレッドのCSV書込時刻"));
            L(J("Hardware presentation timestamp available", "ハードウェアプレゼンテーションタイムスタンプ利用可能"), J("No", "いいえ"));
            lines.Add(J(
                "  * Note: These timestamps are application-side monotonic timestamps, not hardware camera presentation timestamps.",
                "  * 注: これらのタイムスタンプはアプリ側の単調タイムスタンプであり、ハードウェアカメラのプレゼンテーションタイムスタンプではありません。"));

            Sec(J($"[Bitrate Profile — {slotName}]", $"[ビットレートプロファイル — {slotName}]"));
            L(J("Bitrate profile", "ビットレートプロファイル"), activeBitrateProfile.ToString());
            L(J("Target bitrate", "目標ビットレート"), $"{activeBitrateKbps} kbps");
            L(J("Actual bitrate (ffprobe)", "実際のビットレート（ffprobe）"), ffprobeBrStr);

            // v1.2.0-alpha backend section
            var slotRenderer = _v2Engine.GetSlotPreviewRenderer(slot);
            var beMeta = _backendRegistry.BuildMetadata(
                result,
                measuredAvgFps,
                health?.LiveFps ?? 0,
                requestedFps,
                slotRenderer);
            Sec(J($"[Recording Backend — {slotName}]", $"[録画バックエンド — {slotName}]"));
            L(J("Backend",                    "バックエンド"),                       beMeta.RecordingBackend);
            L(J("Backend version",            "バックエンドバージョン"),             beMeta.BackendVersion);
            L(J("Backend mode",               "バックエンドモード"),                 beMeta.BackendMode);
            L(J("Fallback used",              "フォールバック使用"),                  beMeta.BackendFallbackUsed ? J("Yes", "はい") : J("No", "いいえ"));
            if (beMeta.BackendFallbackUsed)
                L(J("Fallback reason",        "フォールバック理由"),                  beMeta.BackendFallbackReason);
            L(J("Capture API",                "キャプチャAPI"),                      beMeta.CaptureApi);
            L(J("Preview API",                "プレビューAPI"),                      beMeta.PreviewApi);
            L(J("Encoder API",                "エンコーダAPI"),                      beMeta.EncoderApi);
            L(J("Hardware encoder used",      "ハードウェアエンコーダ使用"),          J(beMeta.HardwareEncoderUsed,
                beMeta.HardwareEncoderUsed.StartsWith("Yes", StringComparison.Ordinal)
                    ? "はい" + beMeta.HardwareEncoderUsed["Yes".Length..]
                    : beMeta.HardwareEncoderUsed.StartsWith("No", StringComparison.Ordinal)
                        ? "いいえ" + beMeta.HardwareEncoderUsed["No".Length..]
                        : beMeta.HardwareEncoderUsed));
            L(J("Hardware encoder evidence",  "ハードウェアエンコーダ根拠"),          beMeta.HardwareEncoderEvidence);
            L(J("Color tagging applied",      "カラータグ付け適用"),                  beMeta.ColorTaggingApplied ? J("Yes", "はい") : J("No", "いいえ"));
            if (beMeta.ColorTaggingApplied)
            {
                L(J("Color primaries",        "色域（プライマリ）"),                  beMeta.ColorPrimaries);
                L(J("Color transfer function","色伝達関数"),                          beMeta.ColorTransferFunction);
                L(J("Color matrix",           "色マトリックス"),                      beMeta.ColorMatrix);
                L(J("Color range",            "色範囲"),                              beMeta.ColorRange);
            }
            L(J("Preview independent from recording", "プレビュー独立性"),           beMeta.PreviewIndependentFromRecording ? J("Yes", "はい") : J("No", "いいえ"));
            L(J("Preview target FPS",         "プレビュー目標FPS"),                  beMeta.PreviewTargetFps.ToString("F1", ic));
            L(J("Preview measured FPS",       "プレビュー実測FPS"),                  beMeta.PreviewMeasuredFps > 0 ? beMeta.PreviewMeasuredFps.ToString("F2", ic) : J("Unknown", "不明"));
            L(J("Recording measured real FPS","録画実測FPS"),                       beMeta.RecordingMeasuredRealFps > 0 ? beMeta.RecordingMeasuredRealFps.ToString("F2", ic) : J("Unknown", "不明"));
            L(J("Timestamp source",            "タイムスタンプソース"),               beMeta.TimestampSource);
            L(J("Timestamp CSV status",        "タイムスタンプCSVステータス"),        J(beMeta.TimestampCsvStatus, beMeta.TimestampCsvStatus switch
            {
                "Written"  => "書き込み済み",
                "Skipped"  => "スキップ",
                _          => beMeta.TimestampCsvStatus,
            }));

            Sec(J($"[ffprobe Audit — {slotName}]", $"[ffprobe 監査 — {slotName}]"));
            L(J("ffprobe status", "ffprobe ステータス"),
                hasFfprobeData ? J("Success", "成功") : J($"Not available: {ffprobe?.ErrorMessage}", $"利用不可: {ffprobe?.ErrorMessage}"));
            L(J("ffprobe codec", "ffprobe コーデック"),                 ffprobeCodecStr);
            L(J("ffprobe pixel format", "ffprobe ピクセルフォーマット"),  ffprobePixFmtStr);
            L(J("ffprobe resolution", "ffprobe 解像度"),                ffprobeResStr);
            L(J("ffprobe average FPS", "ffprobe 平均FPS"),              ffprobeAvgFpsStr);
            L(J("ffprobe raw r_frame_rate", "ffprobe 生FPS"),            ffprobeRFpsStr);
            L(J("ffprobe container duration", "ffprobe コンテナ時間"),   ffprobeDurStr);
            L(J("ffprobe frame count", "ffprobe フレーム数"),            ffprobeNbFrStr);
            L(J("ffprobe bitrate", "ffprobe ビットレート"),               ffprobeBrStr);
            L(J("ffprobe constant frame rate", "ffprobe 定数フレームレート"), ffprobeCfrStr);

            Sec(J($"[Frame Quality — {slotName}]", $"[フレーム品質 — {slotName}]"));
            var dupImpl = dup?.Implemented == true;
            L(J("Duplicate-frame detection", "重複フレーム検出"),               dupImpl ? J("Implemented", "実装済み") : J("Not available", "利用不可"));
            L(J("Frames sampled for duplicate check", "重複チェックサンプル数"), dupImpl ? dup!.FramesSampled.ToString()        : NA());
            L(J("Comparisons performed", "比較実施数"),                          dupImpl ? dup!.ComparisonsPerformed.ToString()   : NA());
            L(J("Visual near-identical frames", "視覚的ほぼ同一フレーム"),       dupImpl ? dup!.NearIdenticalFrames.ToString()    : NA());
            L(J("Visual near-identical frame rate", "視覚的ほぼ同一フレーム率"), dupImpl ? dup!.NearIdenticalRate.ToString("F3", ic) : NA());
            L(J("Mean grayscale absolute diff", "グレースケール平均絶対差"),      dupImpl ? dup!.MeanGrayDiff.ToString("F2", ic)   : NA());
            L(J("Software duplicate-frame evidence", "ソフトウェア重複フレームの証拠"), dup?.SoftwareEvidenceLevel ?? NA());
            L(J("App-created duplicate frames detected", "アプリ生成重複フレーム検出"),
                dupImpl && dup!.AppCreatedDuplicates.HasValue
                    ? (dup.AppCreatedDuplicates.Value ? J("Yes", "はい") : J("No", "いいえ"))
                    : J("Unknown", "不明"));
            L(J("Duplicate detection note", "重複検出ノート"), dupImpl ? dup!.Note : NA());
            L(J("Writer queue drops", "ライタキュードロップ数"),
                J("Not separately counted (WriteSample failures are logged individually, not aggregated)",
                  "個別集計なし（WriteSample失敗は個別にログ記録、集計はされません）"));
            L(J("Preview dropped frames", "プレビュードロップフレーム数"), J("N/A (Realtime mode — not detectable)", "N/A（リアルタイムモード — 検出不可）"));
            L(J("Writer queue max depth", "ライタキュー最大深度"), J("Not tracked", "非追跡"));
            L(J("Writer max latency", "ライタ最大レイテンシ"),    J("Not tracked", "非追跡"));

            Sec(J($"[Motion Blur Risk — {slotName}]", $"[モーションブラーリスク — {slotName}]"));
            L(J("Motion blur risk", "モーションブラーリスク"), blurLevel);
            lines.Add(J("- Motion blur evidence:", "- モーションブラー根拠:"));
            lines.Add($"  * {J("Auto exposure active", "自動露出アクティブ")}: {(exposureFixed ? J("No", "いいえ") : J("Unknown", "不明"))}");
            lines.Add($"  * {J("Manual exposure confirmed", "マニュアル露出確認済み")}: {(exposureFixed ? J("Yes", "はい") : J("No", "いいえ"))}");
            lines.Add($"  * {J("Low-light compensation confirmed off", "低照度補正オフ確認済み")}: {llcConfirmedOffStr}");
            lines.Add($"  * {J("Capture FPS stable", "キャプチャFPS安定性")}: {(hasTs && tsStats!.GapCount == 0 ? J("Yes", "はい") : hasTs ? J("No (gaps detected)", "いいえ（ギャップ検出）") : J("Unknown", "不明"))}");
            lines.Add($"  * {J("Timestamp intervals stable", "タイムスタンプ間隔安定性")}: {(hasTs && tsStats!.GapCount == 0 ? J("Yes", "はい") : hasTs ? J("No", "いいえ") : J("Unknown", "不明"))}");
            L(J("Motion blur note", "モーションブラーノート"), blurNote);

            Sec(J($"[Visual Quality — {slotName}]", $"[映像品質 — {slotName}]"));
            var vqImpl = vq?.Implemented == true;
            L(J("Visual quality analysis", "映像品質分析"),       vqImpl ? J("Implemented", "実装済み") : J("Not available", "利用不可"));
            L(J("Frames sampled for quality", "品質サンプル数"),   vqImpl ? vq!.FramesSampled.ToString() : NA());
            L(J("Blur score (Laplacian var)", "ブラースコア"),     vqImpl ? vq!.BlurScore.ToString("F2", ic) : NA());
            L(J("Blur level", "ブラーレベル"),                     vqImpl ? vq!.BlurLevel : NA());
            L(J("Overexposed pixels (%)", "過露出ピクセル(%)"),    vqImpl ? vq!.OverexposedPercent.ToString("F2", ic) : NA());
            L(J("Underexposed pixels (%)", "低露出ピクセル(%)"),   vqImpl ? vq!.UnderexposedPercent.ToString("F2", ic) : NA());
            L(J("Brightness mean (0-255)", "輝度平均"),            vqImpl ? vq!.BrightnessMean.ToString("F1", ic) : NA());
            L(J("Brightness instability (std)", "輝度不安定性"),   vqImpl ? $"{vq!.BrightnessStd:F1} ({vq.BrightnessInstability})" : NA());
            L(J("Uneven lighting score", "不均一照明スコア"),       vqImpl ? $"{vq!.UnevenLightingScore:F1} ({vq.UnevenLightingLevel})" : NA());
            L(J("Visual quality verdict", "映像品質判定"),         vq?.Verdict ?? NA());
            lines.Add(J("- Visual quality notes:", "- 映像品質ノート:"));
            if (vqImpl)
                foreach (var n in vq!.Notes) lines.Add($"  * {n}");
            else
                lines.Add($"  * {vq?.ErrorMessage ?? J("Not available", "利用不可")}");

            Sec(J("[Session Verification]", "[セッション検証]"));
            L(J("Session result (this camera)", "セッション結果（このカメラ）"), sessionResult);
            L(J("Global session result", "グローバルセッション結果"),             sessionStats.GlobalSessionResult);
            L(J("Timing confidence", "タイミング信頼度"),                          timingConf);
            L(J("Inter-camera timing confidence", "カメラ間タイミング信頼度"),     sessionStats.InterCameraTimingConfidence);
            L(J("Active cameras verified", "検証済みアクティブカメラ"),            activeSlotList);
            L(J("Frame count range (all cameras)", "フレーム数範囲（全カメラ）"),  $"{sessionStats.GlobalFrameMin} - {sessionStats.GlobalFrameMax}");
            L(J("Frame count spread (all cameras)", "フレーム数差（全カメラ）"),   sessionStats.GlobalFrameSpread.ToString());
            L(J("Inter-camera comparison", "カメラ間比較"),                        sessionStats.InterCameraComparison);
            L(J("Metadata consistency", "メタデータ一貫性"),                       metadataConsistency);
            L(J("Finalization status", "確定状態"),                                finalizationStatus);
            L(J("Timestamp status", "タイムスタンプ状態"),                         timestampStatus);
            L(J("Duplicate-frame status", "重複フレーム状態"),                     dupStatus);
            lines.Add(J("- Notes:", "- ノート:"));
            foreach (var n in verNotes)
                lines.Add($"  * {n}");

            Sec(J("[UI Diagnostics]", "[UI 診断]"));
            L(J("UI freeze detected", "UIフリーズ検出"),     _freezeWatchdog.AnyFreezeDetected ? J("Yes", "はい") : J("No", "いいえ"));
            L(J("UI freeze count", "UIフリーズ回数"),        _freezeWatchdog.FreezeCount.ToString());
            L(J("Max UI freeze", "最大UIフリーズ時間"),          _freezeWatchdog.MaxFreezeMs + " ms");
            L(J("Freeze during state", "フリーズ発生時の状態"),    _freezeWatchdog.LastFreezeState);
            L(J("Preview FPS limit during recording", "録画中のプレビューFPS制限"), previewFpsLimit + " fps");
            L(J("Active camera count", "アクティブカメラ数"),    layout.ToString());

            // ── Write TXT ────────────────────────────────────────────────────
            // Only the slot-prefixed file is written now — verification/RecordingSessionDiscovery.
            // FindCameraMetadataFile (2026-07-09) looks for "{slot}_metadata.txt" directly, so the
            // unprefixed "metadata.txt" duplicate this used to also write (solely so the legacy
            // scanner, which only recognized that exact name, could find anything at all) is no
            // longer needed for new recordings. Verified against a real session with the duplicate
            // removed: Scan/Verify still find and correctly audit the file via the prefixed name
            // alone. Older recordings that still have both copies on disk are unaffected either way.
            var txtPath = string.IsNullOrEmpty(fileSet.MetadataTxtPath)
                ? Path.Combine(fileSet.CameraFolder, $"{slotName}_metadata.txt")
                : fileSet.MetadataTxtPath;
            await File.WriteAllLinesAsync(txtPath, lines, System.Text.Encoding.UTF8);

            // ── Build and write JSON ─────────────────────────────────────────
            var jsonPath = string.IsNullOrEmpty(fileSet.MetadataJsonPath)
                ? Path.Combine(fileSet.CameraFolder, $"{slotName}_metadata.json")
                : fileSet.MetadataJsonPath;

            var jsonObj = new
            {
                schemaVersion    = "1.3.0", // videoSettings.constantFrameRateTarget replaced by frameTimingMode (v1.2.65 VFR migration)
                appVersion       = new { version = ver.Version, build = ver.Build, stage = ver.Stage, releaseDate = ver.ReleaseDate },
                metadataLanguage = _sessionLanguage,
                session = new
                {
                    name              = SessionBox.Text,
                    folder            = Path.GetFileName(sessionFolder),
                    date              = now.ToString("yyyy-MM-dd"),
                    startTime         = startTime.ToString("HH:mm:ss"),
                    stopTime          = stopTime.ToString("HH:mm:ss"),
                    activeCameraCount = layout,
                    activeSlots       = Enumerable.Range(0, layout)
                        .Where(i => !string.IsNullOrEmpty(_vm.SelectedDeviceIds[i]))
                        .Select(i => $"cam{i + 1}").ToList(),
                },
                recordingEngine = new
                {
                    engine                       = "VideoEngineV2",
                    backend                      = "MediaFoundation",
                    renderer                     = "WPF",
                    encoder                      = "MediaFoundation H.264",
                    hardwareEncoderUsed          = result.HardwareEncoderUsed,
                    encoderDescription           = result.EncoderDescription,
                    gpuAcceleration              = snap.Direct3DAvailability.ToString(),
                    fallbackModeUsed             = !result.HardwareEncoderUsed,
                    previewFpsIndependentFromRecording = true,
                },
                videoSettings = new
                {
                    requestedResolution           = requestedResolutionLabel,
                    requestedFps                  = VideoEngineSettings.DefaultPreferredFps,
                    previewFpsModeDuringRecording  = "Balanced",
                    previewTargetFpsDuringRecording = previewFpsLimit,
                    recordingTimingMode            = "OriginalRealFrameCapture",
                    frameTimingMode                = "Variable", // real per-frame timestamps (IMFSinkWriter), not fabricated CFR
                },
                cameras = new[] { new
                {
                    slot                  = slotName,
                    device                = device?.FriendlyName ?? "",
                    requestedResolution   = requestedResolutionWxH,
                    selectedResolution    = resStr,
                    requestedFps          = VideoEngineSettings.DefaultPreferredFps,
                    selectedFormatFps     = selFmt?.NominalFps ?? 0,
                    writerFps             = selFmt?.NominalFps ?? 0,
                    ffprobeAvgFps         = hasFfprobeData ? (double?)ffprobe!.AvgFpsNumeric : null,
                    ffprobeRFrameRate     = hasFfprobeData ? ffprobe!.RFrameRate : null,
                    ffprobeConstantFr     = hasFfprobeData ? (bool?)ffprobe!.ConstantFrameRate : null,
                    formatFallbackUsed    = fmt?.Kind != V2FormatSelectionKind.ExactMatch,
                    fallbackReason        = fmt?.FallbackReason,
                }},
                controls = new
                {
                    focus = new { supportStatus = CtrlSupport(focusCtrl), autofocusDisableRequested = true, result = CtrlResult2(focusCtrl), readback = CtrlReadback(focusCtrl), confirmed = IsConfirmedApplied(focusCtrl), warning = LocalizeControlWarning(focusCtrl?.WarningMessage) },
                    exposure = new { supportStatus = CtrlSupport(exposureCtrl), autoExposureDisableRequested = VideoEngineSettings.DisableAutoExposure, result = CtrlResult2(exposureCtrl), readback = CtrlReadback(exposureCtrl), confirmedFixed = exposureFixed, warning = LocalizeControlWarning(exposureCtrl?.WarningMessage) },
                    lowLightCompensation = new { supportStatus = CtrlSupport(llcCtrl), disableRequested = true, result = CtrlResult2(llcCtrl), readback = CtrlReadback(llcCtrl), confirmedOff = llcOff, warning = LocalizeControlWarning(llcCtrl?.WarningMessage) },
                    digitalStabilization = new { supportStatus = CtrlSupport(digStabCtrl), disableRequested = true, result = CtrlResult2(digStabCtrl), readback = CtrlReadback(digStabCtrl), warning = LocalizeControlWarning(digStabCtrl?.WarningMessage) },
                    flickerReduction = new { supportStatus = CtrlSupport(flickerCtrl), result = CtrlResult2(flickerCtrl), warning = LocalizeControlWarning(flickerCtrl?.WarningMessage) },
                },
                recording = new
                {
                    started           = framesOk,
                    finalized         = finalMp4Exists,
                    temporaryFileUsed = true,
                    temporaryFileRenamed = fileSet.IsFinalized,
                    tempStillExists,
                    outputFile        = Path.GetFileName(fileSet.FinalVideoPath),
                    outputFilePath    = fileSet.FinalVideoPath,
                    fileSizeBytes,
                    writerStatus      = result.Status.ToString(),
                    failureReason     = result.FailureReason,
                },
                timing = new
                {
                    framesWritten            = recordingFrames,
                    previewInclusiveFramesDelivered = result.FramesWritten,
                    containerEncodedFrames   = hasFfprobeData ? ffprobe!.NbFrames : null,
                    timestampCsvWritten      = csvExists,
                    timestampCsvFile         = csvExists ? Path.GetFileName(fileSet.TimestampCsvPath) : (string?)null,
                    timestampCsvRows         = csvRows,
                    timestampRowsMatchFrames = csvRows == 0 ? (bool?)null : (bool?)framesMatchCsv,
                    monotonicDurationS       = result.Duration.TotalSeconds,
                    frameTimestampDurationS  = tsStats?.DurationMs / 1000.0,
                    containerDurationS       = hasFfprobeData ? (double?)ffprobe!.ContainerDurationS : null,
                    durationSourceUsed       = durationSource,
                    resolvedDurationS        = resolvedDuration.TotalSeconds,
                    durationValid,
                    timingConfidence         = timingConf,
                    firstFrameTimestampMs    = tsStats?.FirstTimestampMs,
                    lastFrameTimestampMs     = tsStats?.LastTimestampMs,
                    meanFrameIntervalMs      = tsStats?.MeanIntervalMs,
                    medianFrameIntervalMs    = tsStats?.MedianIntervalMs,
                    minFrameIntervalMs       = tsStats?.MinIntervalMs,
                    maxFrameIntervalMs       = tsStats?.MaxIntervalMs,
                    p95FrameIntervalMs       = tsStats?.P95IntervalMs,
                    p99FrameIntervalMs       = tsStats?.P99IntervalMs,
                    timestampGapCount        = tsStats?.GapCount,
                    estimatedFpsFromTimestamps = tsStats?.EstimatedFps,
                },
                timingReference = new
                {
                    referenceCamera = timingRefName,
                    reason          = timingRefReason,
                    startAfterWarmupSeconds = 10,
                    authoritativeSource = "TimestampCsv",
                    cameras = cfrStatusMap.OrderBy(x => x.Key).Select(x => new { slot = $"cam{x.Key + 1}", cfrStatus = x.Value }).ToList(),
                },
                vfrDriverBehavior = new
                {
                    driverVfr                        = isDriverVfr,
                    behavior                         = vfrBehavior,
                    rFrameRate                       = hasFfprobeData ? ffprobe!.RFrameRate : null,
                    avgFpsNumeric                    = hasFfprobeData ? (double?)ffprobe!.AvgFpsNumeric : null,
                    constantFrameRate                = hasFfprobeData ? (bool?)ffprobe!.ConstantFrameRate : null,
                    timestampCsvIsAuthoritativeSource = true,
                    note                             = isDriverVfr ? vfrNote : null,
                },
                startupSettling = new
                {
                    startAfterWarmupSeconds = 10,
                    analysisSafeInterval    = "after 10s from recording start",
                    startupGapCount         = tsStats?.StartupGapCount,
                    midSessionGapCount      = tsStats?.MidSessionGapCount,
                    stableAfterWarmup,
                    verdict = settlingVerdict,
                },
                bitrateProfile = new
                {
                    profile            = activeBitrateProfile.ToString(),
                    targetBitrateKbps  = activeBitrateKbps,
                    ffprobeBitrateKbps = hasFfprobeData && ffprobe!.BitRateBps.HasValue ? (int?)(ffprobe.BitRateBps.Value / 1000) : null,
                },
                timingModels = new
                {
                    requestedFps            = requestedFps,
                    hardwarePresentationTimestampUsed = false,
                    frameCountTiming = new
                    {
                        durationS     = frameCountDurationS,
                        framesWritten = recordingFrames,
                        basis         = "nominal — frames_written / requested_fps; use for playback estimate only",
                    },
                    containerTiming = new
                    {
                        durationS         = containerDurationS > 0 ? (double?)containerDurationS : null,
                        rFrameRate        = hasFfprobeData ? ffprobe!.RFrameRate : null,
                        avgFrameRate      = hasFfprobeData ? ffprobe!.AvgFrameRate : null,
                        avgFpsNumeric     = hasFfprobeData ? (double?)ffprobe!.AvgFpsNumeric : null,
                        constantFrameRate = hasFfprobeData ? (bool?)ffprobe!.ConstantFrameRate : null,
                        basis             = "player/container interpretation via ffprobe",
                    },
                    appTimestampTiming = new
                    {
                        durationS      = appTsDurationS > 0 ? (double?)appTsDurationS : null,
                        measuredAvgFps = measuredAvgFps > 0 ? (double?)measuredAvgFps : null,
                        intervalMeanMs = tsStats?.MeanIntervalMs,
                        intervalStdMs  = tsStats?.IntervalStdMs,
                        intervalCvPct  = tsStats?.CvPercent,
                        maxGapMs       = tsStats?.MaxIntervalMs,
                        basis          = "primary audit — per-frame app monotonic clock at CSV write time on background frame thread",
                    },
                    internalClockTiming = new
                    {
                        durationS = internalClockS > 0 ? (double?)internalClockS : null,
                        basis     = "recording lifecycle — stopwatch elapsed from StartRecordingAsync to StopRecordingAsync",
                    },
                    containerVsAppTimestampDiffS  = (containerDurationS > 0 && appTsDurationS > 0) ? (double?)containerVsAppDiffS : null,
                    frameCountVsAppTimestampDiffS = (frameCountDurationS > 0 && appTsDurationS > 0) ? (double?)frameCountVsAppDiffS : null,
                    fpsSafetyWarning = !string.IsNullOrEmpty(fpsSafetyWarning) ? fpsSafetyWarning : null,
                },
                timingClassification = new
                {
                    primaryLabel  = timingPrimaryLabel,
                    allLabels     = timingLabels,
                    note          = timingClassNote,
                },
                frameIntegrity = new
                {
                    framesWritten         = recordingFrames,
                    previewInclusiveFramesDelivered = result.FramesWritten,
                    containerEncodedFrames = hasFfprobeData ? ffprobe!.NbFrames : null,
                    timestampCsvRows      = csvRows,
                    csvRowsMatchFrames    = csvRows > 0 ? (bool?)framesMatchCsv : null,
                    csvRowsDiff           = csvRows > 0 ? (long?)(csvRows - recordingFrames) : null,
                    duplicateFramesDetected = dup?.NearIdenticalFrames,
                    placeholderFrames     = (int?)null,
                    writerDrops           = (long?)null,
                    integrityVerdict      = result.PostFinalizeFrameCountMismatch
                        ? "WARN_POST_FINALIZE_MISMATCH"
                        : framesMatchCsv
                            ? "PASS_WITHIN_TOLERANCE" : csvRows > 0 ? "WARN_CSV_MISMATCH" : "CSV_UNAVAILABLE",
                    // Independent ffprobe recount of the just-finalized file — catches frames
                    // silently dropped by Media Foundation after WriteSample returned success (see
                    // RecordingFinalizeResult.PostFinalizeProbedFrameCount doc comment). Null when
                    // the check could not run; absence does not imply frames matched. Checked first
                    // above because it's an independent, authoritative recount of the real file —
                    // csvRowsMatchFrames only compares two of the app's own internal counters against
                    // each other, so it can (and did, see CHANGELOG v1.2.109) show PASS_WITHIN_TOLERANCE
                    // even while the real file's frame count silently diverges from both of them.
                    postFinalizeProbedFrameCount = result.PostFinalizeProbedFrameCount,
                    postFinalizeFrameCountMismatch = result.PostFinalizeProbedFrameCount.HasValue
                        ? (bool?)result.PostFinalizeFrameCountMismatch : null,
                },
                timestampSource = new
                {
                    source                                 = "AppMonotonicClock",
                    clockImplementation                    = "Stopwatch.GetTimestamp / Stopwatch.Frequency",
                    origin                                 = "RecordingStart",
                    capturePoint                           = "CsvWriteTimeOnBackgroundFrameThread",
                    hardwarePresentationTimestampAvailable = false,
                    note                                   = "Application-side monotonic timestamp; not a hardware camera presentation timestamp.",
                },
                cameraControlReadbackLimitations = new
                {
                    dependsOnDriverSupport = true,
                    unknownMeans           = "Value not exposed through current camera API path",
                    doesNotFailRecording   = true,
                },
                // v1.2.0-alpha backend metadata
                backendInfo = BuildBackendMetadataJson(slot, result, tsStats, health),
                ffprobeAudit = new
                {
                    available          = hasFfprobeData,
                    errorMessage       = ffprobe?.ErrorMessage,
                    codec              = hasFfprobeData ? ffprobe!.Codec        : null,
                    pixelFormat        = hasFfprobeData ? ffprobe!.PixelFormat  : null,
                    width              = hasFfprobeData ? (int?)ffprobe!.Width  : null,
                    height             = hasFfprobeData ? (int?)ffprobe!.Height : null,
                    avgFrameRate       = hasFfprobeData ? ffprobe!.AvgFrameRate : null,
                    rFrameRate         = hasFfprobeData ? ffprobe!.RFrameRate   : null,
                    avgFpsNumeric      = hasFfprobeData ? (double?)ffprobe!.AvgFpsNumeric      : null,
                    containerDurationS = hasFfprobeData ? (double?)ffprobe!.ContainerDurationS : null,
                    nbFrames           = hasFfprobeData ? ffprobe!.NbFrames  : null,
                    bitRateBps         = hasFfprobeData ? ffprobe!.BitRateBps : null,
                    fileSizeBytes      = hasFfprobeData ? (long?)ffprobe!.FileSizeBytes : null,
                    constantFrameRate  = hasFfprobeData ? (bool?)ffprobe!.ConstantFrameRate : null,
                },
                frameQuality = new
                {
                    duplicateFrameDetection         = dupImpl ? "Implemented" : "NotAvailable",
                    framesSampledForDuplicateCheck   = dup?.FramesSampled ?? 0,
                    comparisonsPerformed             = dup?.ComparisonsPerformed ?? 0,
                    visualNearIdenticalFrames        = dupImpl ? (int?)dup!.NearIdenticalFrames  : null,
                    nearIdenticalRate                = dupImpl ? (double?)dup!.NearIdenticalRate  : null,
                    meanGrayscaleDiff                = dupImpl ? (double?)dup!.MeanGrayDiff       : null,
                    softwareDuplicateFrameEvidence   = dup?.SoftwareEvidenceLevel ?? "Unknown",
                    appCreatedDuplicateFramesDetected = dup?.AppCreatedDuplicates,
                    duplicateDetectionNote           = dup?.Note,
                    writerQueueDrops                 = (long?)null,
                    writerQueueDropsNote             = "Not separately counted — WriteSample failures are logged individually, not aggregated",
                    previewDroppedFrames             = (long?)null,
                    previewDroppedFramesNote         = "N/A — MediaFrameReader.Realtime drops silently, no callback",
                    writerQueueMaxDepth              = (long?)null,
                    writerMaxLatencyMs               = (double?)null,
                },
                motionBlurRisk = new
                {
                    level                = blurLevel,
                    exposureConfirmedFixed = exposureFixed,
                    llcConfirmedOff      = llcOff,
                    autoExposureActive   = !exposureFixed,
                    note                 = blurNote,
                },
                visualQuality = new
                {
                    implemented           = vq?.Implemented ?? false,
                    framesSampled         = vq?.FramesSampled ?? 0,
                    blurScore             = vq?.BlurScore,
                    blurLevel             = vq?.BlurLevel,
                    overexposedPercent    = vq?.OverexposedPercent,
                    underexposedPercent   = vq?.UnderexposedPercent,
                    brightnessMean        = vq?.BrightnessMean,
                    brightnessStd         = vq?.BrightnessStd,
                    brightnessInstability = vq?.BrightnessInstability,
                    unevenLightingScore   = vq?.UnevenLightingScore,
                    unevenLightingLevel   = vq?.UnevenLightingLevel,
                    verdict               = vq?.Verdict ?? "N/A",
                    notes                 = vq?.Notes ?? new List<string>(),
                    errorMessage          = vq?.ErrorMessage,
                },
                verification = new
                {
                    sessionResult,
                    globalSessionResult        = sessionStats.GlobalSessionResult,
                    timingConfidence           = timingConf,
                    interCameraTimingConfidence = sessionStats.InterCameraTimingConfidence,
                    finalizationStatus,
                    timestampStatus,
                    metadataConsistency,
                    duplicateFrameStatus = dupStatus,
                    activeCamerasVerified = activeSlotList,
                    frameCountRange      = new { min = sessionStats.GlobalFrameMin, max = sessionStats.GlobalFrameMax },
                    frameCountSpread     = sessionStats.GlobalFrameSpread,
                    interCameraComparison = sessionStats.InterCameraComparison,
                    notes = verNotes,
                    globalVerificationNotes = sessionStats.GlobalVerificationNotes,
                },
                environmentalLock = new
                {
                    activeAtRecordingStart = _envLocked,
                    focusLocked            = lockRes?.FocusLocked ?? false,
                    focusLockedAtSteps     = lockRes?.FocusLockedAt,
                    exposureLocked         = lockRes?.ExposureLocked ?? false,
                    exposureLockedAtSeconds = lockRes?.ExposureLockedAtS,
                    whiteBalanceLocked     = lockRes?.WhiteBalanceLocked ?? false,
                    whiteBalanceLockedAtK  = lockRes?.WhiteBalanceLockedAtK,
                    isoLocked              = lockRes?.IsoLocked ?? false,
                    lockWarning            = lockRes?.Warning,
                    note = _envLocked
                        ? "All hardware parameters were frozen at recording start; camera auto modes were disabled for dataset purity."
                        : "No environmental lock was active; camera auto modes may have adapted luminance, white balance, or gain during recording.",
                },
                uiDiagnostics = new
                {
                    uiFreezeDetected     = _freezeWatchdog.AnyFreezeDetected,
                    uiFreezeCount        = _freezeWatchdog.FreezeCount,
                    maxUiFreezeMs        = _freezeWatchdog.MaxFreezeMs,
                    uiFreezeDuringState  = _freezeWatchdog.LastFreezeState,
                    previewFpsThrottled  = previewFpsLimit,
                    activeCameraCount    = layout,
                    suspectedFreezeCause = _freezeWatchdog.AnyFreezeDetected
                        ? J("UI dispatcher saturation from multi-camera preview rendering", "マルチカメラプレビュー描画によるUIディスパッチャー飽和")
                        : J("None detected", "検出なし"),
                },
            };

            var jsonText = System.Text.Json.JsonSerializer.Serialize(jsonObj,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var jsonEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            // See the matching comment on the TXT write above — the unprefixed "metadata.json"
            // duplicate is no longer needed now that the verification scanner looks for the
            // slot-prefixed name directly.
            await File.WriteAllTextAsync(jsonPath, jsonText, jsonEncoding);

            // Include the resolved-duration calculation result (source + value + estimated FPS) —
            // previously only visible by opening the JSON/txt file after the fact; this is exactly
            // the kind of "why does this session show X" question system-wide investigation needs
            // answered from the live log without cross-referencing output files.
            AppDiagnosticLogger.Runtime(
                $"V2_METADATA_WRITTEN slot={slot} lang={_sessionLanguage} result={sessionResult} " +
                $"durationSource=\"{durationSource}\" durationS={resolvedDuration.TotalSeconds:F2} " +
                $"estimatedFps={tsStats?.EstimatedFps:F2} meanIntervalMs={tsStats?.MeanIntervalMs:F2} " +
                $"txt={Path.GetFileName(txtPath)}");
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"V2_METADATA_WRITE_ERROR slot={slot} {ex.GetType().Name}: {ex.Message}");
        }
    }

    private SessionVerificationStats ComputeSessionVerificationStats(
        List<(int slot, RecordingFileSet fileSet, RecordingFinalizeResult result)> collected,
        Dictionary<int, TimestampCsvStats?> tsStatsBySlot,
        Dictionary<int, Diagnostics.FfprobeResult> ffprobeBySlot,
        Dictionary<int, Diagnostics.DuplicateDetectionResult> dupBySlot)
    {
        if (!collected.Any())
        {
            AppDiagnosticLogger.Runtime("V2_SESSION_STATS_COMPUTED cameras=0 result=FAIL notes=[No active cameras.]");
            return new SessionVerificationStats(0, Array.Empty<int>(), Array.Empty<long>(),
                Array.Empty<double>(), Array.Empty<double>(), 0, 0, 0, 0, 0, 0,
                "Not applicable", "Not applicable", "FAIL",
                new List<string> { "No active cameras." });
        }

        var slots  = collected.Select(s => s.slot).ToArray();
        var frames = collected.Select(s =>
        {
            var ts = tsStatsBySlot.GetValueOrDefault(s.slot);
            return s.result.ResolveRecordingRelativeFrames(ts?.RowCount ?? 0);
        }).ToArray();
        var durs   = collected.Select(s => s.result.Duration.TotalSeconds).ToArray();
        var estFps = collected.Select((s, i) =>
        {
            var ts = tsStatsBySlot.GetValueOrDefault(s.slot);
            if (ts?.EstimatedFps > 0) return ts.EstimatedFps;
            return durs[i] > 0 && frames[i] > 0 ? frames[i] / durs[i] : 0.0;
        }).ToArray();

        long globalMin    = frames.Length > 0 ? frames.Min() : 0;
        long globalMax    = frames.Length > 0 ? frames.Max() : 0;
        long globalSpread = globalMax - globalMin;
        double durMin     = durs.Length > 0 ? durs.Min() : 0;
        double durMax     = durs.Length > 0 ? durs.Max() : 0;
        double durSpread  = durMax - durMin;

        var notes  = new List<string>();
        bool isFail = false, isWarn = false;
        var isJa = _sessionLanguage == "ja";
        string N(string en, string ja) => isJa ? ja : en;

        if (frames.Any(f => f == 0))
        { isFail = true; notes.Add(N("One or more cameras recorded zero frames.", "1台以上のカメラで録画フレーム数が0でした。")); }
        if (tsStatsBySlot.Values.Any(t => t is null || t.RowCount == 0))
        { isWarn = true; notes.Add(N("Timestamp CSV missing or empty for one or more cameras.", "1台以上のカメラでタイムスタンプCSVが存在しないか空でした。")); }
        if (ffprobeBySlot.Values.Any(f => !f.Available))
        { isWarn = true; notes.Add(N("ffprobe container audit not available for one or more cameras.", "1台以上のカメラでffprobeコンテナ監査が利用できませんでした。")); }
        if (collected.Count > 1 && globalSpread > Math.Max(5L, globalMin / 100L))
        {
            isWarn = true;
            notes.Add(N(
                $"Frame count spread across cameras is {globalSpread} ({globalMin}-{globalMax}). Expected for independent cameras; verify timestamp CSV gap data.",
                $"カメラ間のフレーム数差は{globalSpread}（{globalMin}～{globalMax}）です。独立したカメラでは想定内ですが、タイムスタンプCSVのギャップデータを確認してください。"));
        }
        if (collected.Count > 1 && durSpread > 2.0)
        { isWarn = true; notes.Add(N($"Duration spread is {durSpread:F1}s across cameras.", $"カメラ間の時間差は{durSpread:F1}秒です。")); }

        if (!notes.Any()) notes.Add(N("Session verification passed all checks.", "セッション検証はすべてのチェックに合格しました。"));

        string interComp = collected.Count <= 1
            ? N("Not applicable — one active camera", "該当なし — アクティブカメラは1台のみ")
            : N(
                $"Compared {collected.Count} cameras. Frame range: {globalMin}-{globalMax} (spread: {globalSpread}). Duration range: {durMin:F2}-{durMax:F2}s (spread: {durSpread:F1}s).",
                $"{collected.Count}台のカメラを比較しました。フレーム範囲: {globalMin}～{globalMax}（差: {globalSpread}）。時間範囲: {durMin:F2}～{durMax:F2}秒（差: {durSpread:F1}秒）。");

        string interConf = collected.Count <= 1 ? "Not applicable"
            : isFail ? "FAIL" : isWarn ? "PASS_WITH_WARNING" : "PASS";

        var globalResult = isFail ? "FAIL" : isWarn ? "PASS_WITH_WARNING" : "PASS";

        // This is the exact decision logic behind the session's PASS/PASS_WITH_WARNING/FAIL
        // verdict — previously silent, meaning the only way to see WHY a session got flagged
        // was to open its metadata files and manually re-derive the frame/duration spread math.
        AppDiagnosticLogger.Runtime(
            $"V2_SESSION_STATS_COMPUTED cameras={collected.Count} result={globalResult} " +
            $"frameRange={globalMin}-{globalMax} frameSpread={globalSpread} " +
            $"durationRange={durMin:F2}-{durMax:F2}s durationSpread={durSpread:F2}s " +
            $"interCameraConfidence={interConf} notes=[{string.Join(" | ", notes)}]");

        return new SessionVerificationStats(
            collected.Count, slots, frames, durs, estFps,
            globalMin, globalMax, globalSpread,
            durMin, durMax, durSpread,
            interComp, interConf,
            globalResult,
            notes);
    }

    private static (int refSlot, string reason, Dictionary<int, string> cfrStatusMap)
        ComputeTimingReferenceCamera(
            int[] activeSlots,
            Dictionary<int, Diagnostics.FfprobeResult> ffprobeBySlot,
            Dictionary<int, TimestampCsvStats?>? tsStatsBySlot = null)
    {
        var cfrStatusMap = new Dictionary<int, string>();
        foreach (var s in activeSlots)
        {
            var fp = ffprobeBySlot.GetValueOrDefault(s);
            var ts = tsStatsBySlot?.GetValueOrDefault(s);
            if (fp?.Available != true)
                cfrStatusMap[s] = "Unknown (ffprobe not available)";
            else if (fp.ConstantFrameRate)
            {
                var jitterNote = ts?.IntervalStdMs > 0 ? $", jitter σ={ts.IntervalStdMs:F1} ms" : "";
                cfrStatusMap[s] = $"CFR ({fp.AvgFpsNumeric:F2} fps avg{jitterNote})";
            }
            else
            {
                var rFps = ParseRFrameRateNumerator(fp.RFrameRate);
                cfrStatusMap[s] = rFps > 40 && fp.AvgFpsNumeric < 35
                    ? $"VFR — DriverVfr (r_frame_rate={fp.RFrameRate}, avg {fp.AvgFpsNumeric:F2} fps)"
                    : $"VFR ({fp.AvgFpsNumeric:F2} fps avg)";
            }
        }

        // Score each slot: lower is better
        // Prefer: no mid-session gaps, low jitter (StdDev), measured FPS close to 30, CFR
        double ScoreSlot(int s)
        {
            var fp = ffprobeBySlot.GetValueOrDefault(s);
            var ts = tsStatsBySlot?.GetValueOrDefault(s);
            double score = 0;
            if (fp?.ConstantFrameRate != true) score += 100;
            score += (ts?.MidSessionGapCount ?? 1) * 50;
            score += (ts?.IntervalStdMs ?? 10);
            double fpsDiff = fp?.Available == true ? Math.Abs(fp.AvgFpsNumeric - 30.0) : 5.0;
            score += fpsDiff * 5;
            return score;
        }

        var ranked = activeSlots.OrderBy(ScoreSlot).ToArray();
        if (ranked.Length == 0)
            return (0, "No active cameras", cfrStatusMap);

        var best = ranked[0];
        var bestFp = ffprobeBySlot.GetValueOrDefault(best);
        var bestTs = tsStatsBySlot?.GetValueOrDefault(best);

        var reasonParts = new List<string>();
        if (bestFp?.ConstantFrameRate == true)        reasonParts.Add("CFR");
        if (bestTs?.MidSessionGapCount == 0)          reasonParts.Add("no mid-session gaps");
        if (bestTs?.IntervalStdMs > 0)                reasonParts.Add($"jitter σ={bestTs.IntervalStdMs:F1} ms");
        if (bestFp?.Available == true)                reasonParts.Add($"avg {bestFp.AvgFpsNumeric:F2} fps");

        string reason = reasonParts.Count > 0
            ? string.Join(", ", reasonParts)
            : "first active camera (no timing data)";

        return (best, reason, cfrStatusMap);
    }

    private static (List<string> labels, string primary, string note) ClassifyTimingBehavior(
        TimestampCsvStats? ts, Diagnostics.FfprobeResult? fp, double requestedFps, bool isJa = false)
    {
        var labels = new List<string>();
        var notes  = new List<string>();
        string J2(string en, string jp) => isJa ? jp : en;

        bool hasTsData = ts is not null && ts.RowCount > 1;
        double measuredFps = hasTsData ? ts!.EstimatedFps : (fp?.AvgFpsNumeric ?? 0);
        double rFpsNum     = ParseRFrameRateNumerator(fp?.RFrameRate);
        bool   isCfr       = fp?.ConstantFrameRate ?? false;

        // VFR_DRIVER_BEHAVIOR: r_frame_rate much higher than actual delivery
        if (fp?.Available == true && rFpsNum > 40 && fp.AvgFpsNumeric < 35)
        {
            labels.Add("VFR_DRIVER_BEHAVIOR");
            notes.Add(J2(
                $"Driver r_frame_rate={fp.RFrameRate} but avg delivery {fp.AvgFpsNumeric:F2} fps — DriverVfr.",
                $"ドライバーのr_frame_rate={fp.RFrameRate}ですが、平均配信は{fp.AvgFpsNumeric:F2} fps — ドライバーVFR。"));
        }

        if (hasTsData)
        {
            // FPS_MISMATCH_DETECTED: measured FPS differs from requested FPS
            if (Math.Abs(measuredFps - requestedFps) >= 1.5)
            {
                labels.Add("FPS_MISMATCH_DETECTED");
                notes.Add(J2(
                    $"Measured {measuredFps:F2} fps vs requested {requestedFps:F0} fps.",
                    $"測定{measuredFps:F2} fps 対 要求{requestedFps:F0} fps。"));
            }

            // Startup-only vs mid-session gaps
            if (ts!.MidSessionGapCount > 0)
            {
                labels.Add("MID_SESSION_GAPS_DETECTED");
                notes.Add(J2(
                    $"{ts.MidSessionGapCount} gap(s) detected after the first 10 s warmup period.",
                    $"最初の10秒のウォームアップ期間後に{ts.MidSessionGapCount}件のギャップを検出しました。"));
            }
            else if (ts.StartupGapCount > 0)
            {
                labels.Add("STARTUP_SETTLING_ONLY");
                notes.Add(J2(
                    $"{ts.StartupGapCount} gap(s) in startup warmup only; session stable after 10 s.",
                    $"起動ウォームアップ中のみ{ts.StartupGapCount}件のギャップ；10秒後はセッションが安定しています。"));
            }

            // POSSIBLE_EXPOSURE_LIMITED_FPS: FPS low but VFR/DRIVER label not already set
            if (measuredFps > 0 && measuredFps < requestedFps * 0.9
                && !labels.Contains("VFR_DRIVER_BEHAVIOR"))
            {
                labels.Add("POSSIBLE_EXPOSURE_LIMITED_FPS");
                notes.Add(J2(
                    $"Measured FPS {measuredFps:F2} < 90% of requested {requestedFps:F0}; may be exposure/LLC/driver limited.",
                    $"測定FPS {measuredFps:F2} が要求{requestedFps:F0}の90%未満です；露出／低照度補正／ドライバーによる制限の可能性があります。"));
            }
        }

        // CFR_LIKE: no issues above and jitter is low
        bool cfrLike = labels.Count == 0
            || (labels.Count == 1 && labels[0] == "STARTUP_SETTLING_ONLY");
        if (cfrLike && isCfr)
        {
            labels.Insert(0, "CFR_LIKE");
            notes.Insert(0, J2(
                "Stable interval delivery consistent with CFR behavior.",
                "安定した間隔での配信であり、CFR（固定フレームレート）動作と一致しています。"));
        }

        string primary = labels.Count > 0 ? labels[0] : (isCfr ? "CFR_LIKE" : "VFR");
        if (!labels.Contains(primary)) labels.Insert(0, primary);
        string note = notes.Count > 0
            ? string.Join(" ", notes)
            : J2("No timing issues detected.", "タイミングの問題は検出されませんでした。");
        return (labels, primary, note);
    }

    // v1.2.22-alpha — backend metadata JSON builder (V2 Stable only)
    private object BuildBackendMetadataJson(
        int slot,
        RecordingFinalizeResult result,
        TimestampCsvStats? tsStats,
        CameraHealthSnapshot? health)
    {
        var bm = _backendRegistry.BuildMetadata(
            result,
            measuredRealFps: tsStats?.EstimatedFps ?? 0,
            previewMeasuredFps: health?.LiveFps ?? 0,
            previewTargetFps: VideoEngineSettings.DefaultPreferredFps,
            previewRenderer: _v2Engine.GetSlotPreviewRenderer(slot));

        var initDiag = _backendRegistry.GetActiveDiagnostics();
        return new
        {
            recordingBackend              = bm.RecordingBackend,
            backendVersion                = bm.BackendVersion,
            backendMode                   = bm.BackendMode,
            backendFallbackUsed           = bm.BackendFallbackUsed,
            backendFallbackReason         = bm.BackendFallbackReason,
            captureApi                    = bm.CaptureApi,
            previewApi                    = bm.PreviewApi,
            encoderApi                    = bm.EncoderApi,
            hardwareEncoderUsed           = bm.HardwareEncoderUsed,
            hardwareEncoderEvidence       = bm.HardwareEncoderEvidence,
            colorTaggingApplied           = bm.ColorTaggingApplied,
            colorPrimaries                = bm.ColorPrimaries,
            colorTransferFunction         = bm.ColorTransferFunction,
            colorMatrix                   = bm.ColorMatrix,
            colorRange                    = bm.ColorRange,
            previewIndependentFromRecording = bm.PreviewIndependentFromRecording,
            previewTargetFps              = bm.PreviewTargetFps,
            previewMeasuredFps            = bm.PreviewMeasuredFps,
            recordingMeasuredRealFps      = bm.RecordingMeasuredRealFps,
            timestampSource               = bm.TimestampSource,
            timestampCsvStatus            = bm.TimestampCsvStatus,
            v2FramesSubmittedSinceRecordingStart = bm.V2FramesSubmittedSinceRecordingStart,
            v2FrameCounterScope           = bm.V2FrameCounterScope,
            v2FramesWrittenDuringRecording = bm.V2FramesWrittenDuringRecording,
            noArtificialFramePadding      = bm.NoArtificialFramePadding,
            noDuplicateFramePadding       = bm.NoDuplicateFramePadding,
            noPlaceholderFrames           = bm.NoPlaceholderFrames,
            measuredFpsDiffFromRequested  = bm.MeasuredFpsDiffFromRequested,
            realFpsStabilityStatus        = bm.RealFpsStabilityStatus,
            initDiagnostics = new
            {
                backendId             = initDiag.BackendId,
                initSucceeded         = initDiag.InitSucceeded,
                initSummary           = initDiag.InitSummary,
                mediaFoundationStatus = initDiag.MediaFoundationStatus,
                direct3D11Status      = initDiag.Direct3D11Status,
                encoderStatus         = initDiag.EncoderStatus,
                previewRendererStatus = initDiag.PreviewRendererStatus,
                watchdogStatus        = initDiag.WatchdogStatus,
                storageStatus         = initDiag.StorageStatus,
            },
        };
    }

    private static double ParseRFrameRateNumerator(string? rFrameRate)
    {
        if (string.IsNullOrEmpty(rFrameRate)) return 0;
        var parts = rFrameRate.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var den) &&
            den > 0)
            return num / den;
        return 0;
    }

    private async Task WriteV2SessionMetadataAsync(
        List<(int slot, RecordingFileSet fileSet, RecordingFinalizeResult result)> collected,
        Dictionary<int, TimestampCsvStats?> tsStatsBySlot,
        Dictionary<int, Diagnostics.FfprobeResult> ffprobeBySlot,
        Dictionary<int, Diagnostics.DuplicateDetectionResult> dupBySlot,
        SessionVerificationStats sessionStats,
        string? sessionFolderOverride = null)
    {
        try
        {
            var sessionFolder = sessionFolderOverride ?? _v2SessionFolderPath!;
            var now           = DateTime.Now;
            var isJa          = _sessionLanguage == "ja";
            var ver           = _vm.Version;
            var ic            = System.Globalization.CultureInfo.InvariantCulture;

            string J(string en, string jp) => isJa ? jp : en;
            string NA() => J("Not available", "利用不可");

            var lines = new List<string>(140);
            void L(string label, string value) => lines.Add($"- {label}: {value}");
            void Sec(string name) { lines.Add(""); lines.Add(name); }

            lines.Add(J("MULTICAMAPP SESSION METADATA", "MULTICAMAPP セッションメタデータ"));
            lines.Add($"{J("Generated", "生成日時")}: {now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"{J("App version", "アプリバージョン")}: {ver.Version}");
            lines.Add($"{J("Build", "ビルド")}: {ver.Build}");
            lines.Add($"{J("Release stage", "リリースステージ")}: {ver.Stage}");

            Sec(J("[Session Summary]", "[セッションサマリー]"));
            L(J("Session name", "セッション名"),       SessionBox.Text);
            L(J("Session folder", "セッションフォルダ"), Path.GetFileName(sessionFolder));
            L(J("Recording date", "録画日"),           now.ToString("yyyy-MM-dd"));
            L(J("Recording stop time", "録画停止時刻"), now.ToString("HH:mm:ss"));
            L(J("Active cameras", "アクティブカメラ数"), collected.Count.ToString());
            L(J("Active slots", "アクティブスロット"),
                string.Join(", ", collected.Select(s => $"cam{s.slot + 1}")));
            L(J("Engine", "エンジン"), "VideoEngineV2");
            L(J("Metadata language", "メタデータ言語"), isJa ? "Japanese" : "English");

            Sec(J("[Inter-Camera Comparison]", "[カメラ間比較]"));
            L(J("Camera count", "カメラ数"), collected.Count.ToString());
            for (int i = 0; i < collected.Count; i++)
            {
                var s     = collected[i];
                var sName = $"cam{s.slot + 1}";
                L(J($"Frame count ({sName})",    $"フレーム数（{sName}）"),      sessionStats.FrameCounts[i].ToString());
                L(J($"Duration ({sName})",        $"時間（{sName}）"),            sessionStats.DurationsS[i] > 0 ? $"{sessionStats.DurationsS[i]:F2} s" : NA());
                L(J($"Estimated FPS ({sName})",   $"推定FPS（{sName}）"),         sessionStats.EstimatedFps[i] > 0 ? sessionStats.EstimatedFps[i].ToString("F2", ic) : NA());
                var ts = tsStatsBySlot.GetValueOrDefault(s.slot);
                L(J($"CSV rows ({sName})",         $"CSV行数（{sName}）"),         ts?.RowCount > 0 ? ts.RowCount.ToString() : NA());
                var fp = ffprobeBySlot.GetValueOrDefault(s.slot);
                L(J($"ffprobe duration ({sName})", $"ffprobe 時間（{sName}）"),   fp?.Available == true ? $"{fp.ContainerDurationS:F2} s" : NA());
                var dp = dupBySlot.GetValueOrDefault(s.slot);
                L(J($"Dup evidence ({sName})",     $"重複証拠（{sName}）"),        dp?.SoftwareEvidenceLevel ?? NA());
            }
            lines.Add("");
            L(J("Global frame count range",   "グローバルフレーム数範囲"),   $"{sessionStats.GlobalFrameMin} - {sessionStats.GlobalFrameMax}");
            L(J("Global frame count spread",  "グローバルフレーム数差"),     sessionStats.GlobalFrameSpread.ToString());
            L(J("Global duration range",      "グローバル時間範囲"),         $"{sessionStats.GlobalDurationMin:F2} - {sessionStats.GlobalDurationMax:F2} s");
            L(J("Global duration spread",     "グローバル時間差"),           $"{sessionStats.GlobalDurationSpreadS:F1} s");
            L(J("Inter-camera timing confidence", "カメラ間タイミング信頼度"), sessionStats.InterCameraTimingConfidence);

            Sec(J("[Session Verification]", "[セッション検証]"));
            L(J("Global session result", "グローバルセッション結果"), sessionStats.GlobalSessionResult);
            lines.Add(J("- Verification notes:", "- 検証ノート:"));
            foreach (var n in sessionStats.GlobalVerificationNotes)
                lines.Add($"  * {n}");

            var sessionTxtPath = Path.Combine(sessionFolder, "session_metadata.txt");
            await File.WriteAllLinesAsync(sessionTxtPath, lines, System.Text.Encoding.UTF8);

            // ── Session JSON ─────────────────────────────────────────────────
            var sessionJsonObj = new
            {
                schemaVersion    = "1.2.0",
                appVersion       = new { version = ver.Version, build = ver.Build, stage = ver.Stage, releaseDate = ver.ReleaseDate },
                metadataLanguage = _sessionLanguage,
                generatedAt      = now.ToString("yyyy-MM-dd HH:mm:ss"),
                session = new
                {
                    name        = SessionBox.Text,
                    folder      = Path.GetFileName(sessionFolder),
                    date        = now.ToString("yyyy-MM-dd"),
                    stopTime    = now.ToString("HH:mm:ss"),
                    activeCount = collected.Count,
                    activeSlots = collected.Select(s => $"cam{s.slot + 1}").ToList(),
                },
                cameras = collected.Select((s, i) =>
                {
                    var ts = tsStatsBySlot.GetValueOrDefault(s.slot);
                    var fp = ffprobeBySlot.GetValueOrDefault(s.slot);
                    var dp = dupBySlot.GetValueOrDefault(s.slot);
                    return new
                    {
                        slot             = $"cam{s.slot + 1}",
                        framesWritten    = sessionStats.FrameCounts[i],
                        durationS        = sessionStats.DurationsS[i],
                        estimatedFps     = sessionStats.EstimatedFps[i],
                        csvRows          = ts?.RowCount ?? 0,
                        csvGapCount      = ts?.GapCount,
                        ffprobeDurationS = fp?.Available == true ? (double?)fp.ContainerDurationS : null,
                        ffprobeAvgFps    = fp?.Available == true ? (double?)fp.AvgFpsNumeric : null,
                        ffprobeNbFrames  = fp?.Available == true ? fp.NbFrames : null,
                        dupEvidence      = dp?.SoftwareEvidenceLevel,
                        dupNearIdentical = dp?.Implemented == true ? (int?)dp.NearIdenticalFrames : null,
                    };
                }).ToList(),
                interCameraComparison = new
                {
                    globalFrameMin        = sessionStats.GlobalFrameMin,
                    globalFrameMax        = sessionStats.GlobalFrameMax,
                    globalFrameSpread     = sessionStats.GlobalFrameSpread,
                    globalDurationMinS    = sessionStats.GlobalDurationMin,
                    globalDurationMaxS    = sessionStats.GlobalDurationMax,
                    globalDurationSpreadS = sessionStats.GlobalDurationSpreadS,
                    interCameraTimingConf = sessionStats.InterCameraTimingConfidence,
                    summary               = sessionStats.InterCameraComparison,
                },
                sessionVerification = new
                {
                    globalResult = sessionStats.GlobalSessionResult,
                    notes        = sessionStats.GlobalVerificationNotes,
                },
            };

            var sessionJsonPath = Path.Combine(sessionFolder, "session_metadata.json");
            await File.WriteAllTextAsync(sessionJsonPath,
                System.Text.Json.JsonSerializer.Serialize(sessionJsonObj,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            AppDiagnosticLogger.Runtime($"V2_SESSION_METADATA_WRITTEN folder={Path.GetFileName(sessionFolder)} result={sessionStats.GlobalSessionResult}");
        }
        catch (Exception ex)
        {
            AppDiagnosticLogger.Runtime($"V2_SESSION_METADATA_WRITE_ERROR {ex.GetType().Name}: {ex.Message}");
        }
    }
}

