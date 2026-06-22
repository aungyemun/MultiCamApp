////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Core;

namespace MultiCamApp.Verification;

public sealed class ExpectedSettingsResolver
{
    public (Dictionary<string, ExpectedCameraSettings> BySlot, string Source) Resolve(
        string sessionFolder,
        IReadOnlyList<VideoFileEntry> entries,
        AppConfig? appConfig)
    {
        var bySlot = new Dictionary<string, ExpectedCameraSettings>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<string>();

        var summary = MetadataParser.ParseSessionSummary(sessionFolder);
        if (summary != null)
        {
            sources.Add("session_summary.txt");
            foreach (var cam in summary.Cameras)
            {
                bySlot[cam.CameraSlot] = FromMetadata(cam, "session_summary.txt");
            }
        }

        foreach (var entry in entries)
        {
            var meta = MetadataParser.LoadCameraMetadata(entry.MetadataJsonPath, entry.MetadataPath);
            if (meta == null) continue;
            var slot = string.IsNullOrEmpty(meta.CameraSlot) ? entry.CameraSlot : meta.CameraSlot;
            if (bySlot.TryGetValue(slot, out var existing))
            {
                EnrichFromPerCameraMetadata(existing, meta);
                if (!sources.Contains("metadata"))
                    sources.Add("metadata");
            }
            else
            {
                sources.Add("metadata");
                bySlot[slot] = FromMetadata(meta, "metadata");
            }
        }

        if (appConfig != null && bySlot.Count == 0)
        {
            sources.Add("appsettings.json");
            var w = appConfig.PreferredCaptureWidth;
            var h = appConfig.PreferredCaptureHeight;
            var fps = VerificationCaptureProfile.NormalizeFps(appConfig.PreferFps);
            foreach (var entry in entries)
            {
                bySlot[entry.CameraSlot] = new ExpectedCameraSettings
                {
                    CameraSlot = entry.CameraSlot,
                    Width = w > 0 ? w : null,
                    Height = h > 0 ? h : null,
                    Fps = fps > 0 ? fps : null,
                    Codec = appConfig.RecordingEngine == "opencv" ? "mp4v" : "H264",
                    ContainerFormat = "MP4",
                    Source = "appsettings.json"
                };
            }
        }

        var sourceLabel = sources.Count > 0 ? string.Join(" + ", sources.Distinct()) : "inferred from video";
        return (bySlot, sourceLabel);
    }

    public ExpectedCameraSettings? ResolveForEntry(
        VideoFileEntry entry,
        AppConfig? appConfig)
    {
        var sessionFolder = string.IsNullOrEmpty(entry.SessionFolder)
            ? Path.GetDirectoryName(entry.FullPath) ?? ""
            : entry.SessionFolder;
        var (bySlot, _) = Resolve(sessionFolder, [entry], appConfig);
        return bySlot.TryGetValue(entry.CameraSlot, out var expected) ? expected : null;
    }

    public string ResolveSourceLabel(string sessionFolder, IReadOnlyList<VideoFileEntry> sessionEntries)
    {
        var (_, source) = Resolve(sessionFolder, sessionEntries, null);
        return source;
    }

    private static ExpectedCameraSettings FromMetadata(CameraMetadataRecord meta, string source)
    {
        var (width, height) = VerificationCaptureProfile.ResolveExpectedDimensions(meta);
        return new ExpectedCameraSettings
        {
            CameraSlot = meta.CameraSlot,
            Width = width,
            Height = height,
            Fps = VerificationCaptureProfile.ResolveExpectedFps(meta),
            Codec = meta.Codec ?? meta.VideoSubtype,
            ContainerFormat = meta.ContainerFormat,
            DurationSeconds = meta.DurationSeconds,
            FrameCount = meta.FrameCount > 0 ? meta.FrameCount : null,
            Source = source
        };
    }

    /// <summary>
    /// Per-camera metadata has writer-accurate duration/frames; prefer user-requested resolution/FPS when present.
    /// </summary>
    private static void EnrichFromPerCameraMetadata(ExpectedCameraSettings target, CameraMetadataRecord meta)
    {
        var expectedFps = VerificationCaptureProfile.ResolveExpectedFps(meta);
        if (expectedFps is > 0)
            target.Fps = expectedFps;

        var (width, height) = VerificationCaptureProfile.ResolveExpectedDimensions(meta);
        if (width is > 0 && height is > 0)
        {
            target.Width = width;
            target.Height = height;
        }

        if (!string.IsNullOrEmpty(meta.Codec)) target.Codec = meta.Codec ?? meta.VideoSubtype;
        if (!string.IsNullOrEmpty(meta.ContainerFormat)) target.ContainerFormat = meta.ContainerFormat;

        var writerStatsValid = meta.FrameCount > 0 && meta.DurationSeconds is > 0.5;
        if (writerStatsValid)
        {
            target.DurationSeconds = meta.DurationSeconds;
            target.FrameCount = meta.FrameCount;
            target.Source = "session_summary.txt + metadata";
        }
        else if (meta.DurationSeconds is > 0 && (target.DurationSeconds ?? 0) < 0.5)
        {
            target.DurationSeconds = meta.DurationSeconds;
        }

        if (meta.FrameCount > 0 && target.FrameCount is null or 0)
            target.FrameCount = meta.FrameCount;
    }
}
