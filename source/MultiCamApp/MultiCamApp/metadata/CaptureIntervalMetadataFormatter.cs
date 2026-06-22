////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

namespace MultiCamApp.Metadata;

public static class CaptureIntervalMetadataFormatter
{
    public static string FormatMs(double value, long intervalCount, string? unavailableMessage) =>
        intervalCount >= 1 ? $"{value:F3}" : "Unavailable";

    public static string FormatCount(long intervalCount, string? unavailableMessage) =>
        intervalCount >= 1 ? intervalCount.ToString() : "Unavailable";

    public static string DescribeAvailability(long intervalCount, string? unavailableMessage)
    {
        if (intervalCount >= 1)
            return "";
        return string.IsNullOrWhiteSpace(unavailableMessage)
            ? CaptureTimingSnapshot.UnavailableMessage
            : unavailableMessage;
    }
}
