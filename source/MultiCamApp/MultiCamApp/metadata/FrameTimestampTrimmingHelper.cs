////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Globalization;

namespace MultiCamApp.Metadata;

public sealed record FrameTimestampSample(long FrameIndex, double CaptureMonotonicSec);

public static class FrameTimestampTrimmingHelper
{
    public const string OriginalCaptureTimeSource = "PerFrameCaptureTimestamps";
    public const string ScientificTrimStartReference = "firstFrameCaptureMonotonicSec + elapsedStartSec";
    public const string ScientificTrimEndReference = "firstFrameCaptureMonotonicSec + elapsedEndSec";
    public const string ContainerDurationTrimWarning =
        "MP4 container duration is frame-based and differs from real wall-clock duration. For scientific trimming, use per-frame capture timestamps or measured camera FPS.";

    public static string GetTrimWarning(double containerVsWallClockDifferenceSec) =>
        Math.Abs(containerVsWallClockDifferenceSec) > 0.5 ? ContainerDurationTrimWarning : "";

    public static long GetFrameIndexForElapsedTime(string frameTimestampCsvPath, double elapsedSec) =>
        GetFrameIndexForElapsedTime(LoadFrameTimestampSamples(frameTimestampCsvPath), elapsedSec);

    public static long GetFrameIndexForElapsedTime(IReadOnlyList<FrameTimestampSample> samples, double elapsedSec)
    {
        if (samples.Count == 0)
            throw new ArgumentException("At least one frame timestamp sample is required.", nameof(samples));
        if (double.IsNaN(elapsedSec) || double.IsInfinity(elapsedSec))
            throw new ArgumentOutOfRangeException(nameof(elapsedSec), "Elapsed seconds must be finite.");

        var target = samples[0].CaptureMonotonicSec + Math.Max(0, elapsedSec);
        foreach (var sample in samples)
        {
            if (sample.CaptureMonotonicSec >= target)
                return sample.FrameIndex;
        }

        return samples[^1].FrameIndex;
    }

    public static IReadOnlyList<FrameTimestampSample> LoadFrameTimestampSamples(string frameTimestampCsvPath)
    {
        if (string.IsNullOrWhiteSpace(frameTimestampCsvPath))
            throw new ArgumentException("Frame timestamp CSV path is required.", nameof(frameTimestampCsvPath));
        if (!File.Exists(frameTimestampCsvPath))
            throw new FileNotFoundException("Frame timestamp CSV was not found.", frameTimestampCsvPath);

        using var reader = new StreamReader(frameTimestampCsvPath);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidDataException("Frame timestamp CSV has no header.");

        var header = headerLine.Split(',');
        var frameIndexColumn = Array.FindIndex(header, h => string.Equals(h.Trim(), "frameIndex", StringComparison.OrdinalIgnoreCase));
        var captureMonotonicColumn = Array.FindIndex(header, h => string.Equals(h.Trim(), "captureMonotonicSec", StringComparison.OrdinalIgnoreCase));
        if (frameIndexColumn < 0 || captureMonotonicColumn < 0)
            throw new InvalidDataException("Frame timestamp CSV must contain frameIndex and captureMonotonicSec columns.");

        var samples = new List<FrameTimestampSample>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var columns = line.Split(',');
            if (columns.Length <= Math.Max(frameIndexColumn, captureMonotonicColumn))
                continue;

            if (long.TryParse(columns[frameIndexColumn], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frameIndex)
                && double.TryParse(columns[captureMonotonicColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var captureMonotonicSec))
            {
                samples.Add(new FrameTimestampSample(frameIndex, captureMonotonicSec));
            }
        }

        if (samples.Count == 0)
            throw new InvalidDataException("Frame timestamp CSV contains no readable timestamp rows.");

        return samples;
    }
}
