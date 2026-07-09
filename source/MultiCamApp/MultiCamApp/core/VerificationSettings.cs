namespace MultiCamApp.Core;

public sealed class VerificationSettings
{
    public bool Enabled { get; set; } = true;
    public string[] ScanExtensions { get; set; } = [".mp4"];
    public bool RecursiveScan { get; set; } = true;
    public bool UseFfprobe { get; set; } = true;
    public string FfprobePath { get; set; } = "runtime/ffmpeg/ffprobe.exe";
    /// <summary>Full ffmpeg.exe (not just ffprobe.exe) — used only by the on-demand Deep Verify action.</summary>
    public string FfmpegPath { get; set; } = "runtime/ffmpeg/ffmpeg.exe";
    /// <summary>Per-file timeout for the Deep Verify per-frame MD5 hash pass.</summary>
    public int DeepVerifyTimeoutSeconds { get; set; } = 300;
    public double FpsWarningTolerance { get; set; } = 1.5;
    public double FpsFailTolerance { get; set; } = 3.0;
    public double DurationWarningToleranceSeconds { get; set; } = 1.5;
    public double DurationFailToleranceSeconds { get; set; } = 5.0;
    public int FrameWarningTolerance { get; set; } = 3;
    public int FrameFailTolerance { get; set; } = 30;
    public bool AutoVerifyAfterScan { get; set; }
    public bool AllowReportExport { get; set; } = true;
}
