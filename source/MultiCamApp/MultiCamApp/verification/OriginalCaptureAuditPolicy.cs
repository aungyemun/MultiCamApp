using MultiCamApp.Localization;

namespace MultiCamApp.Verification;

public static class OriginalCaptureAuditPolicy
{
    public const double AcceptableMeasuredFpsDifference = 0.5;
    public const double AcceptableWallClockDurationDifferenceSeconds = 0.1;
    public const double AcceptableStartEndOffsetSeconds = 0.1;
    public const double AcceptableStartEndOffsetMs = AcceptableStartEndOffsetSeconds * 1000.0;
    public const double UnstableCaptureIntervalStdMs = 10.0;
    public const long UnstableMaxConsecutiveNoFrame = 2;

    public const string Mode = "OriginalCapture";
    public const string LegacyConstantFrameCountMode = "LegacyConstantFrameCount";

    public const string StableDifferentFpsNote =
        "Frame counts may differ because cameras delivered real frames at different measured FPS.";

    public const string SessionInterpretation =
        "Original Capture Mode: Real frames only; no duplicates/placeholders. Frame counts may differ because cameras delivered real frames at different measured FPS. Use timestamp CSV for timing-sensitive analysis.";

    /// <summary>Localized form of <see cref="StableDifferentFpsNote"/>; falls back to the English constant.</summary>
    public static string GetStableDifferentFpsNote(LanguageManager? language) =>
        language?["verifyNoteFrameCountDifference"] is { Length: > 0 } v ? v : StableDifferentFpsNote;

    /// <summary>Localized form of <see cref="SessionInterpretation"/>; falls back to the English constant.</summary>
    public static string GetSessionInterpretation(LanguageManager? language) =>
        language?["verifyMsgSessionInterpretation"] is { Length: > 0 } v ? v : SessionInterpretation;
}
