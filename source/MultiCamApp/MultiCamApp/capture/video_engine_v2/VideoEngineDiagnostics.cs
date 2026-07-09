////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

// VideoEngineV2 — production backend (default from v1.1.7+).

using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace MultiCamApp.Capture.VideoEngineV2;

/// <summary>
/// Probes and reports VideoEngineV2 environment capabilities and pipeline state.
/// All probes are cached after first call (static fields).
/// </summary>
public sealed class VideoEngineDiagnostics
{
    private static V2CapabilityAvailability? _d3d11Cache;
    private static V2CapabilityAvailability? _mfCache;

    /// <summary>
    /// Probes real Direct3D 11 device-creation capability using the same adapter selection,
    /// driver type, and feature levels <see cref="D3D11SwapChainHost"/> uses for the actual
    /// preview renderer. Device creation has no thread affinity (unlike swap-chain creation),
    /// so this is safe to call from any thread. Cached after first call.
    /// </summary>
    /// <remarks>
    /// Prior to v1.2.77 this only checked for <c>d3d11.dll</c> in System32 — present on every
    /// Windows 7 SP1+ machine regardless of whether the actual GPU/driver can create a usable
    /// device, so it always reported "Available" even on hardware where real device creation
    /// would fail. The real renderer already has its own try/catch WPF fallback so that gap
    /// never caused a functional failure, but it meant diagnostics/logs (relied on throughout
    /// this project's history to judge real GPU capability on a given machine) could overstate
    /// what the hardware actually supports.
    /// </remarks>
    public static V2CapabilityAvailability ProbeDirect3D11()
    {
        if (_d3d11Cache.HasValue) return _d3d11Cache.Value;
        try
        {
            var featureLevels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
            D3D11.D3D11CreateDevice(
                null, DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                featureLevels, out var device, out _, out var context).CheckError();
            device?.Dispose();
            context?.Dispose();
            _d3d11Cache = V2CapabilityAvailability.Available;
        }
        catch { _d3d11Cache = V2CapabilityAvailability.Unavailable; }
        return _d3d11Cache.Value;
    }

    /// <summary>
    /// Probes Media Foundation availability by checking for <c>mfplat.dll</c> in System32.
    /// Cached after first call.
    /// </summary>
    public static V2CapabilityAvailability ProbeMediaFoundation()
    {
        if (_mfCache.HasValue) return _mfCache.Value;
        try
        {
            _mfCache = File.Exists(Path.Combine(Environment.SystemDirectory, "mfplat.dll"))
                ? V2CapabilityAvailability.Available
                : V2CapabilityAvailability.Unavailable;
        }
        catch { _mfCache = V2CapabilityAvailability.Unknown; }
        return _mfCache.Value;
    }

    /// <summary>Builds a full diagnostics snapshot from pipeline and device state.</summary>
    public static VideoEngineDiagnosticsSnapshot BuildSnapshot(
        CameraPipelineV2? pipeline,
        V2CameraDeviceInfo? selectedDevice,
        V2FormatSelectionResult? formatResult,
        VideoEngineBackend backend)
    {
        var health   = pipeline?.GetHealthSnapshot();
        var format   = formatResult?.SelectedFormat;
        var encoder  = pipeline?.ActiveEncoderBackend ?? EncoderBackendType.NotSelected;

        return new VideoEngineDiagnosticsSnapshot
        {
            Backend                     = backend,
            PipelineState               = pipeline?.State ?? CameraPipelineState.Idle,
            HealthSnapshot              = health,
            Direct3DAvailability        = ProbeDirect3D11(),
            MediaFoundationAvailability = ProbeMediaFoundation(),
            SelectedDeviceName          = selectedDevice?.FriendlyName,
            SelectedDeviceId            = selectedDevice?.DeviceId,
            SelectedDeviceIndex         = selectedDevice?.EnumerationIndex ?? -1,
            SelectedDiscoverySource     = selectedDevice?.DiscoverySource,
            SelectedFormat              = format,
            SelectedPixelFormat         = format?.PixelFormat,
            SelectedSubtypeName         = format?.SubtypeName,
            FormatSelectionKind         = formatResult?.Kind,
            FallbackReason              = formatResult?.FallbackReason,
            ActivePreviewRenderer       = pipeline?.ActivePreviewRenderer ?? PreviewRendererType.Wpf,
            ActiveEncoderBackend        = encoder,
            HardwareEncoderUsed         = pipeline?.IsHardwareEncoderActive ?? false,
            IsRecording                 = pipeline?.State == CameraPipelineState.Recording,
            UsbPnpStatus                = DeriveUsbStatus(selectedDevice),
            WindowsStudioEffectsWarning = pipeline?.WindowsStudioEffectsWarning,
            ControlResults              = pipeline?.LastControlResults ?? Array.Empty<V2ControlApplyResult>(),
        };
    }

    private static string DeriveUsbStatus(V2CameraDeviceInfo? device)
    {
        if (device is null) return "No device selected";
        if (device.DeviceId.Contains("USB", StringComparison.OrdinalIgnoreCase))
            return $"USB device at index {device.EnumerationIndex}; ID: {device.DeviceId[..Math.Min(40, device.DeviceId.Length)]}…";
        return $"Device at index {device.EnumerationIndex} (non-USB or unknown bus)";
    }
}

// ── Snapshot ──────────────────────────────────────────────────────────────────

/// <summary>Point-in-time diagnostics snapshot for VideoEngineV2.</summary>
public sealed class VideoEngineDiagnosticsSnapshot
{
    public VideoEngineBackend Backend    { get; init; } = VideoEngineBackend.Legacy;
    public CameraPipelineState PipelineState { get; init; } = CameraPipelineState.Idle;
    public CameraHealthSnapshot? HealthSnapshot { get; init; }

    // Capability probes
    public V2CapabilityAvailability Direct3DAvailability        { get; init; } = V2CapabilityAvailability.Unknown;
    public V2CapabilityAvailability MediaFoundationAvailability { get; init; } = V2CapabilityAvailability.Unknown;

    // Selected camera
    public string? SelectedDeviceName   { get; init; }
    public string? SelectedDeviceId     { get; init; }
    public int SelectedDeviceIndex      { get; init; } = -1;
    public V2DeviceDiscoverySource? SelectedDiscoverySource { get; init; }

    // Selected format
    public V2CaptureFormat? SelectedFormat   { get; init; }
    public V2PixelFormat? SelectedPixelFormat { get; init; }
    public string? SelectedSubtypeName       { get; init; }
    public V2FormatSelectionKind? FormatSelectionKind { get; init; }
    public string? FallbackReason            { get; init; }

    // Renderer and encoder
    public PreviewRendererType ActivePreviewRenderer { get; init; } = PreviewRendererType.Wpf;
    public EncoderBackendType ActiveEncoderBackend   { get; init; } = EncoderBackendType.NotSelected;
    public bool HardwareEncoderUsed { get; init; }
    public bool IsRecording         { get; init; }

    // USB / PnP
    public string? UsbPnpStatus { get; init; }

    // Camera controls
    public string? WindowsStudioEffectsWarning { get; init; }
    public IReadOnlyList<V2ControlApplyResult> ControlResults { get; init; }
        = Array.Empty<V2ControlApplyResult>();

    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    /// <summary>Human-readable summary for logging and diagnostics output.</summary>
    public string ToSummaryString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[VideoEngineV2 Diagnostics — {Timestamp:HH:mm:ss}]");
        sb.AppendLine($"  Backend            : {Backend}");
        sb.AppendLine($"  Pipeline state     : {PipelineState}");
        sb.AppendLine($"  D3D11 available    : {Direct3DAvailability}");
        sb.AppendLine($"  MF available       : {MediaFoundationAvailability}");
        sb.AppendLine($"  Selected device    : {SelectedDeviceName ?? "—"} [{SelectedDeviceIndex}]");
        sb.AppendLine($"  Selected format    : {SelectedFormat?.ToString() ?? "—"}");
        sb.AppendLine($"  Pixel format       : {SelectedPixelFormat?.ToString() ?? SelectedSubtypeName ?? "—"}");
        sb.AppendLine($"  Format selection   : {FormatSelectionKind?.ToString() ?? "—"}");
        if (FallbackReason is not null)
            sb.AppendLine($"  Fallback reason    : {FallbackReason}");
        sb.AppendLine($"  Preview renderer   : {ActivePreviewRenderer}");
        sb.AppendLine($"  Encoder backend    : {ActiveEncoderBackend}");
        sb.AppendLine($"  Hardware encoder   : {(HardwareEncoderUsed ? "yes" : "no/unknown")}");
        sb.AppendLine($"  Recording          : {(IsRecording ? "yes" : "no")}");
        sb.AppendLine($"  USB/PnP status     : {UsbPnpStatus ?? "—"}");
        if (WindowsStudioEffectsWarning is not null)
            sb.AppendLine($"  Studio Effects     : {WindowsStudioEffectsWarning}");
        if (HealthSnapshot is not null)
        {
            sb.AppendLine($"  Live FPS           : {HealthSnapshot.LiveFps:F1}");
            sb.AppendLine($"  Frames delivered   : {HealthSnapshot.FramesDelivered}");
            sb.AppendLine($"  Frames dropped     : N/A (Realtime mode — not detectable)");
        }
        return sb.ToString();
    }
}
