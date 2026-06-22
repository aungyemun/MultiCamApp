////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using MultiCamApp.Capture;
using MultiCamApp.Core;
using MultiCamApp.Utils;

namespace MultiCamApp.Recording;

public sealed class RecordingController
{
    private readonly LogService _log = new();
    private readonly RecordingSession _session;
    private readonly AppConfig _config;

    public RecordingSession Session => _session;

    public void ApplyVersion(VersionInfo version)
    {
        Session.AppVersionInfo = version;
    }

    public RecordingController(AppConfig config, PrivacyGuardService privacy)
    {
        _config = config;
        _session = new RecordingSession(config, privacy);
    }

    public string PrepareSession(string? outputFolder, string title) =>
        _session.PrepareSession(outputFolder, title);

    public async Task StartAsync(IReadOnlyList<CameraSlotPipeline> slots) =>
        await _session.StartRecordingAsync(slots);

    public async Task StopAsync(IReadOnlyList<CameraSlotPipeline> slots) =>
        await _session.StopRecordingAsync(slots);

    public async Task StopAllRecordingsSafelyAsync(IReadOnlyList<CameraSlotPipeline> slots)
    {
        try
        {
            await _session.StopRecordingAsync(slots);
            _log.Info("recording", "StopAllRecordingsSafely completed");
        }
        catch (Exception ex)
        {
            _log.Error("recording", "StopAllRecordingsSafely error", ex);
        }
    }
}
