////////////////////////////////////////////////////
/// STABLE_CORE_V2
/// Validated in MultiCamApp v2.0.0 build 333 (first stable release).
/// Do not modify without documented regression testing.
/// Protected: VideoEngineV2 recording engine, native metadata, video verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V2 protected component — modification requires regression checklist; do not refactor casually. See docs/STABLE_CORE_V2_FREEZE.md.

namespace MultiCamApp.Verification;

public static class CameraAuditStatus
{
    public const string Pass = "PASS";
    public const string PassOriginalTiming = "PASS_ORIGINAL_TIMING";
    public const string PassOriginalTimingWithNote = "PASS_ORIGINAL_TIMING_WITH_NOTE";
    public const string PassOriginalTimingWithStopBoundaryNote = "PASS_ORIGINAL_TIMING_WITH_STOP_BOUNDARY_NOTE";
    public const string PassWithWarning = "PASS_WITH_WARNING";
    public const string Fail = "FAIL";

    public static string FromVideoResult(VideoVerificationResult video)
    {
        if (video.Verdict == VerificationVerdict.Fail)
            return Fail;

        if (string.Equals(video.ScientificTimingStatus, Fail, StringComparison.OrdinalIgnoreCase))
            return Fail;

        if (string.Equals(video.ScientificTimingStatus, PassWithWarning, StringComparison.OrdinalIgnoreCase))
            return PassWithWarning;

        if (IsPassLevel(video.ScientificTimingStatus))
            return video.ScientificTimingStatus;

        if (video.Verdict == VerificationVerdict.Warning)
            return PassWithWarning;

        return Pass;
    }

    public static VerificationVerdict ToVerdict(string status) => status switch
    {
        Fail => VerificationVerdict.Fail,
        PassWithWarning => VerificationVerdict.Warning,
        _ => VerificationVerdict.Pass
    };

    public static string Display(string status) => status switch
    {
        PassOriginalTiming => PassOriginalTiming,
        PassOriginalTimingWithNote => PassOriginalTimingWithNote,
        PassOriginalTimingWithStopBoundaryNote => PassOriginalTimingWithStopBoundaryNote,
        PassWithWarning => PassWithWarning,
        Fail => Fail,
        _ => Pass
    };

    public static bool IsPassLevel(string? status) =>
        string.Equals(status, Pass, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, PassOriginalTiming, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, PassOriginalTimingWithNote, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, PassOriginalTimingWithStopBoundaryNote, StringComparison.OrdinalIgnoreCase);
}
