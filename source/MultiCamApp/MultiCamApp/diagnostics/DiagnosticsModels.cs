// Diagnostic result models used in per-camera metadata output.
// Analysis implementations (ffprobe, duplicate detection, visual quality) were
// removed in v1.2.22-alpha. These types remain so metadata writers can express
// "not available" fields without requiring a full rewrite of the output schema.

namespace MultiCamApp.Diagnostics;

public sealed class FfprobeResult
{
    public bool    Available          { get; init; }
    public string? ErrorMessage       { get; init; }
    public string? Codec              { get; init; }
    public string? PixelFormat        { get; init; }
    public int     Width              { get; init; }
    public int     Height             { get; init; }
    public string? AvgFrameRate       { get; init; }
    public string? RFrameRate         { get; init; }
    public double  AvgFpsNumeric      { get; init; }
    public double  ContainerDurationS { get; init; }
    public long?   NbFrames           { get; init; }
    public long?   BitRateBps         { get; init; }
    public long    FileSizeBytes      { get; init; }
    public bool    ConstantFrameRate  { get; init; }

    public static FfprobeResult NotAvailable(string reason) =>
        new() { Available = false, ErrorMessage = reason };
}

public sealed class DuplicateDetectionResult
{
    public bool    Implemented           { get; init; }
    public string? ErrorMessage          { get; init; }
    public int     FramesSampled         { get; init; }
    public int     ComparisonsPerformed  { get; init; }
    public int     NearIdenticalFrames   { get; init; }
    public double  NearIdenticalRate     { get; init; }
    public double  MeanGrayDiff          { get; init; }
    public string  SoftwareEvidenceLevel { get; init; } = "Unknown";
    public bool?   AppCreatedDuplicates  { get; init; }
    public string  Note                  { get; init; } = "";

    public static DuplicateDetectionResult NotAvailable(string reason) =>
        new() { Implemented = false, ErrorMessage = reason, SoftwareEvidenceLevel = "N/A", Note = reason };
}

public sealed class VisualQualityResult
{
    public bool         Implemented            { get; init; }
    public string?      ErrorMessage           { get; init; }
    public int          FramesSampled          { get; init; }
    public double       BlurScore              { get; init; }
    public string       BlurLevel              { get; init; } = "Unknown";
    public double       OverexposedPercent     { get; init; }
    public double       UnderexposedPercent    { get; init; }
    public double       BrightnessMean         { get; init; }
    public double       BrightnessStd          { get; init; }
    public string       BrightnessInstability  { get; init; } = "Unknown";
    public double       UnevenLightingScore    { get; init; }
    public string       UnevenLightingLevel    { get; init; } = "Unknown";
    public string       Verdict                { get; init; } = "Unknown";
    public List<string> Notes                  { get; init; } = [];

    public static VisualQualityResult NotAvailable(string reason) => new()
    {
        Implemented  = false,
        ErrorMessage = reason,
        Verdict      = "N/A",
        Notes        = [reason],
    };
}
