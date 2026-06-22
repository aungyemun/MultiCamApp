using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MultiCamApp.Capture;
using MultiCamApp.Recording;
using MultiCamApp.Ui;
using MultiCamApp.Utils;

namespace MultiCamApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _previewUiBusy;
    private bool _recordUiBusy;
    private readonly System.Windows.Controls.Image[] _previewImages = new System.Windows.Controls.Image[4];
    private readonly TextBlock[] _statLabels = new TextBlock[4];
    private readonly TextBlock[] _slotLabels = new TextBlock[4];
    private readonly Border[] _cellBorders = new Border[4];
    private readonly Border[] _cardBorders = new Border[4];
    private readonly TextBlock[] _headerStats = new TextBlock[4];
    private readonly TextBlock[] _deviceFooters = new TextBlock[4];
    private readonly System.Windows.Shapes.Ellipse[] _activeDots = new System.Windows.Shapes.Ellipse[4];
    private readonly Border[] _cells = new Border[4];
    private readonly System.Windows.Controls.ComboBox[] _deviceBoxes;
    private readonly StackPanel[] _camBlocks;
    private readonly ResponsiveLayoutManager _layoutManager = new();
    private DispatcherTimer? _elapsedTimer;
    private DispatcherTimer? _resizeTimer;
    private int _pendingLayoutCount = 1;
    private bool _populatingVideoSettings;
    private bool _populatingDeviceBoxes;
    private bool _syncingLayoutRadios;
    private bool _syncingFocusControls;
    private bool _syncingExposureControls;
    private bool _shutdownCleanupStarted;
    private bool _shutdownCleanupCompleted;
    private CancellationTokenSource? _videoSettingsReapplyCts;
    private Task? _videoSettingsReapplyTask;
    public MainWindow()
    {
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
        ResolutionBox.SelectedIndex = 2;

        FpsBox.Items.Clear();
        foreach (var fps in new[] { 15, 24, 30, 60 })
            FpsBox.Items.Add(new ComboBoxItem { Content = $"{fps} fps", Tag = fps });
        FpsBox.SelectedIndex = 2;
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
        _vm.Config.ReapplyFocusBeforeRecording = ReapplyFocusBeforeRecordingCheckBox.IsChecked == true;
        if (double.TryParse(ManualFocusValueBox.Text, out var manualFocus))
            _vm.Config.ManualFocusValue = manualFocus;
        _vm.Config.AutoExposureEnabled = AdvancedAutoExposureCheckBox.IsChecked == true;
        _vm.Config.ReapplyExposureBeforeRecording = ReapplyExposureBeforeRecordingCheckBox.IsChecked == true;
        _vm.Config.DisableLowLightCompensation = DisableLowLightCompensationCheckBox.IsChecked == true;
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

            HighStabilityRecordingModeCheckBox.IsChecked = _vm.Config.HighStabilityRecordingMode;
            SyncRecordingPreviewFpsModeFromConfig();
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

            var deviceFooter = new TextBlock
            {
                Foreground = PreviewPanelTheme.FooterForeground,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var footerBar = new Border
            {
                Background = PreviewPanelTheme.FooterBarBackground,
                BorderBrush = PreviewPanelTheme.InnerVideoBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Child = deviceFooter
            };

            var cardInner = new Grid();
            cardInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cardInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(headerGrid, 0);
            Grid.SetRow(videoBorder, 1);
            Grid.SetRow(footerBar, 2);
            cardInner.Children.Add(headerGrid);
            cardInner.Children.Add(videoBorder);
            cardInner.Children.Add(footerBar);

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
            _cardBorders[i] = cardBorder;
            _headerStats[i] = stats;
            _deviceFooters[i] = deviceFooter;
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

    private void SyncRecordingPreviewFpsModeFromConfig()
    {
        var mode = _vm.Config.RecordingPreviewFpsMode;
        foreach (ComboBoxItem item in RecordingPreviewFpsModeBox.Items)
        {
            if (item.Tag is string tag && string.Equals(tag, mode, StringComparison.OrdinalIgnoreCase))
            {
                RecordingPreviewFpsModeBox.SelectedItem = item;
                return;
            }
        }
        RecordingPreviewFpsModeBox.SelectedIndex = 1; // Balanced default
    }

    private void HighStabilityRecordingModeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _vm.Config.HighStabilityRecordingMode = HighStabilityRecordingModeCheckBox.IsChecked == true;
    }

    private void RecordingPreviewFpsModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecordingPreviewFpsModeBox.SelectedItem is ComboBoxItem item && item.Tag is string mode)
            _vm.Config.RecordingPreviewFpsMode = mode;
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

    private void ReapplyFocusBeforeRecordingCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var slot = GetCurrentFocusTargetSlot();
        var value = ReapplyFocusBeforeRecordingCheckBox.IsChecked == true;
        if (slot >= 0) _vm.Config.ReapplyFocusBeforeRecordingPerCamera[slot] = value;
        else _vm.Config.ReapplyFocusBeforeRecording = value;
    }

    private async void ApplyFocusSettingButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyVideoSettingsToViewModel();
        ApplyFocusSettingButton.IsEnabled = false;
        try
        {
            var target = FocusCameraTargetBox.SelectedItem is ComboBoxItem item && item.Tag is int slotIndex
                ? slotIndex
                : (int?)null;
            await _vm.ApplyFocusSettingsAsync(target);
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
        var selectedTag = FocusCameraTargetBox.SelectedItem is ComboBoxItem selected && selected.Tag is int tag
            ? tag : 0;
        FocusCameraTargetBox.Items.Clear();
        for (var i = 0; i < 4; i++)
            FocusCameraTargetBox.Items.Add(new ComboBoxItem { Content = _vm.GetCameraSlotLabel(i), Tag = i });
        FocusCameraTargetBox.Items.Add(new ComboBoxItem { Content = "All selected cameras (advanced)", Tag = -1 });
        // Default to first camera (slot 0), not "All cameras"
        var targetIndex = selectedTag >= 0 ? selectedTag : 0;
        FocusCameraTargetBox.SelectedIndex = Math.Clamp(targetIndex, 0, FocusCameraTargetBox.Items.Count - 1);
    }

    private void LoadPerCameraFocusToUi()
    {
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) return;
        _syncingFocusControls = true;
        try
        {
            AdvancedAutoFocusCheckBox.IsChecked = _vm.Config.AutoFocusEnabledPerCamera[slot];
            ReapplyFocusBeforeRecordingCheckBox.IsChecked = _vm.Config.ReapplyFocusBeforeRecordingPerCamera[slot];
            var val = _vm.Config.ManualFocusValuesPerCamera[slot] ?? ManualFocusSlider.Value;
            ManualFocusSlider.Value = Math.Clamp(val, ManualFocusSlider.Minimum, ManualFocusSlider.Maximum);
            ManualFocusValueBox.Text = ManualFocusSlider.Value.ToString("F0");
        }
        finally { _syncingFocusControls = false; }
    }

    private void LoadPerCameraExposureToUi()
    {
        var slot = GetCurrentFocusTargetSlot();
        if (slot < 0) return;
        _syncingExposureControls = true;
        try
        {
            AdvancedAutoExposureCheckBox.IsChecked = _vm.Config.AutoExposureEnabledPerCamera[slot];
            DisableLowLightCompensationCheckBox.IsChecked = _vm.Config.DisableLowLightCompensationPerCamera[slot];
            ReapplyExposureBeforeRecordingCheckBox.IsChecked = _vm.Config.ReapplyExposureBeforeRecordingPerCamera[slot];
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
        var camLabel = slot >= 0 ? $"CAM{slot + 1}" : "All Cameras";
        if (ApplyFocusSettingButton != null)
            ApplyFocusSettingButton.Content = $"Apply Focus to {camLabel}";
        if (ApplyExposureSettingButton != null)
            ApplyExposureSettingButton.Content = $"Apply Exposure to {camLabel}";
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
        ManualFocusAvailabilityText.Text = autoEnabled
            ? "Manual focus is disabled while autofocus is enabled."
            : status?.ManualFocusSupported == false
                ? "Manual focus unavailable for this camera/driver."
                : "Manual focus is best-effort; unsupported cameras may ignore this value.";
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

    private void ReapplyExposureBeforeRecordingCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var value = ReapplyExposureBeforeRecordingCheckBox.IsChecked == true;
        var slot = GetCurrentFocusTargetSlot();
        if (slot >= 0) _vm.Config.ReapplyExposureBeforeRecordingPerCamera[slot] = value;
        else _vm.Config.ReapplyExposureBeforeRecording = value;
    }

    private void DisableLowLightCompensationCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var value = DisableLowLightCompensationCheckBox.IsChecked == true;
        var slot = GetCurrentFocusTargetSlot();
        if (slot >= 0) _vm.Config.DisableLowLightCompensationPerCamera[slot] = value;
        else _vm.Config.DisableLowLightCompensation = value;
    }

    private async void ApplyExposureSettingButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyVideoSettingsToViewModel();
        ApplyExposureSettingButton.IsEnabled = false;
        try
        {
            var targetSlot = GetCurrentFocusTargetSlot();
            var target = targetSlot >= 0 ? targetSlot : (int?)null;
            var results = await _vm.ApplyExposureSettingsAsync(target);
            var warned = results.Any(r => !string.IsNullOrEmpty(r.ExposureWarning));
            var autoExposure = targetSlot >= 0
                ? _vm.Config.AutoExposureEnabledPerCamera[targetSlot]
                : _vm.Config.AutoExposureEnabled;
            ExposureStatusText.Text = warned
                ? "Exposure: applied (unconfirmed — check camera/vendor settings if brightness is inconsistent)"
                : autoExposure
                    ? "Exposure: auto exposure applied"
                    : "Exposure: manual exposure applied";
            RefreshTexts();
            UpdateActionButtons();
        }
        finally
        {
            ApplyExposureSettingButton.IsEnabled = true;
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
            ReapplyExposureBeforeRecordingCheckBox.IsChecked = _vm.Config.ReapplyExposureBeforeRecording;
            DisableLowLightCompensationCheckBox.IsChecked = _vm.Config.DisableLowLightCompensation;
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
            await _vm.ReapplyCaptureSettingsToActivePreviewAsync();
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

    private void UpdateActionButtons()
    {
        UpdateUiStateFromCurrentState();
    }

    private void UpdateUiStateFromCurrentState()
    {
        var hasDevice = _vm.HasSelectedDeviceForActiveLayout();
        var previewing = _vm.State.RunState == Core.AppRunState.Previewing;
        var opening = _vm.PreviewStartInProgress;
        var recording = _vm.State.RunState == Core.AppRunState.Recording;
        var allPreviewReady = _vm.AllActiveSlotsPreviewReady();

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
            // Starting recording: disable all workflow buttons.
            StartPreviewBtn.IsEnabled = false;
            StopPreviewBtn.IsEnabled = false;
            StartRecordBtn.IsEnabled = false;
            StopRecordBtn.IsEnabled = false;
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
            _deviceFooters[i].Text = previewActive && active ? p.Pipeline.DeviceName : "";
            _activeDots[i].Fill = active && previewActive && p.Pipeline.Status is "Previewing" or "Recording"
                ? PreviewPanelTheme.ActiveDot
                : new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
        }
    }

    private void UpdateStatusDashboard()
    {
        var L = _vm.Language;
        StatusValue.Text = GetNeutralStatusText();
        ElapsedValue.Text = _vm.ElapsedDisplay;
        SessionTimeValue.Text = _vm.ElapsedDisplay;

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
            var (ready, required) = _vm.CountPreviewReadySlots();
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
        if (_vm.StatusDisplay.StartsWith("Stopping", StringComparison.OrdinalIgnoreCase))
            return "Stopping";

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
        MainNavControl.ApplyLabels(L["main"], L["verifyNav"], "Hardware Diagnostics");
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
        try
        {
            var started = await _vm.StartPreviewAsync(i => _previewImages[i], userClicked: true, uiBefore: uiBefore);
            if (started)
                StartPreviewStatsTimer();
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
        try
        {
            await _vm.StopPreviewAsync();
            foreach (var c in _previewImages) c.Source = null;
        }
        finally
        {
            UpdatePreviewOverlayStats();
            UpdateStatusDashboard();
            UpdateActionButtons();
        }

        _vm.FinalizePreviewSessionLog(new MainViewModel.UiButtonStates(
            StartPreviewBtn.IsEnabled,
            StopPreviewBtn.IsEnabled,
            StartRecordBtn.IsEnabled,
            StopRecordBtn.IsEnabled));
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
        if (_videoSettingsReapplyTask is { IsCompleted: false } pendingReapply)
        {
            VideoSettingsHint.Text = _vm.Language["videoSettingsApplying"];
            await pendingReapply;
        }
        if (!ConfirmPreRecordSettingsOrCancel())
            return;
        if (!ConfirmPreRecordStorageOrCancel())
            return;
        _recordUiBusy = true;
        UpdateUiStateFromCurrentState();
        try
        {
            await _vm.StartRecordingAsync(userClicked: true, uiBefore: uiBefore);
            if (_vm.State.IsRecording)
                StartElapsedTimer();
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
        await _vm.StopRecordingAsync();
        _vm.UpdateRecordingTitle(t => Title = t);
        UpdateStatusDashboard();
        UpdateActionButtons();
        UpdatePreviewOverlayStats();

        _vm.FinalizeRecordingSessionLog(new MainViewModel.UiButtonStates(
            StartPreviewBtn.IsEnabled,
            StopPreviewBtn.IsEnabled,
            StartRecordBtn.IsEnabled,
            StopRecordBtn.IsEnabled));
        if (_vm.State.RunState != Core.AppRunState.Recording) StopElapsedTimer();
    }

    private void StartElapsedTimer()
    {
        _elapsedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _elapsedTimer.Tick -= ElapsedTimer_Tick;
        _elapsedTimer.Tick += ElapsedTimer_Tick;
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer() => _elapsedTimer?.Stop();

    private void ElapsedTimer_Tick(object? sender, EventArgs e)
    {
        _vm.UpdateElapsed();
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

        foreach (var image in _previewImages)
            image.Source = null;

        await _vm.OnAppClosingAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        if (forceShutdown)
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
    }
}
