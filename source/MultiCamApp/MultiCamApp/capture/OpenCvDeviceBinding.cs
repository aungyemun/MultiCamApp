namespace MultiCamApp.Capture;

public readonly struct OpenCvDeviceBinding
{
    public int Index { get; init; }
    /// <summary>When set, OpenCV opens via DirectShow device name (video=...) instead of index.</summary>
    public string? DirectShowName { get; init; }
    /// <summary>PnP URI for duplicate USB cameras (@device:pnp:...).</summary>
    public string? DirectShowOpenUri { get; init; }

    public bool HasCaptureTarget =>
        !string.IsNullOrWhiteSpace(DirectShowOpenUri)
        || !string.IsNullOrWhiteSpace(DirectShowName)
        || Index >= 0;
}
