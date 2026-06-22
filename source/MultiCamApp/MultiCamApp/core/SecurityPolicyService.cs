namespace MultiCamApp.Core;

/// <summary>Enforces transparent, security-friendly runtime behavior. Never bypasses AV or Windows privacy.</summary>
public sealed class SecurityPolicyService
{
    private readonly SecuritySettings _security;
    private readonly AppConfig _config;

    public SecurityPolicyService(AppConfig config)
    {
        _config = config;
        _security = config.Security;
        ApplyPolicyToConfig();
    }

    public SecuritySettings Security => _security;

    public bool CameraAccessOnlyAfterUserAction => _security.CameraAccessOnlyAfterUserAction;
    public bool RecordingOnlyAfterUserAction => _security.RecordingOnlyAfterUserAction;
    public bool TelemetryEnabled => _security.TelemetryEnabled;
    public bool NetworkRequired => _security.NetworkRequired;

    private void ApplyPolicyToConfig()
    {
        if (_security.RequireAntivirusExclusion)
            throw new InvalidOperationException(
                "Invalid configuration: RequireAntivirusExclusion must be false. MultiCamApp does not require antivirus exclusions.");

        if (!_security.DisableAntivirusBypass && _config.PrivacyMode)
            _config.PrivacyMode = true;

        if (!_security.HiddenRecordingAllowed)
            _config.HiddenRecordingAllowed = false;

        if (_security.RecordingOnlyAfterUserAction)
            _config.PrivacyMode = true;

        if (_security.ReleaseCameraOnExit)
            _config.ReleaseCamerasOnAppClose = true;

        if (_security.RunAtStartup || _security.InstallBackgroundService || _security.InstallScheduledTask)
            throw new InvalidOperationException(
                "Invalid configuration: startup entries, services, and scheduled tasks are not supported.");
    }

    public bool CanStartPreview(AppRunState state, bool userClicked) =>
        userClicked &&
        (!_security.CameraAccessOnlyAfterUserAction || state == AppRunState.Idle) &&
        (!_config.PrivacyMode || state == AppRunState.Idle);

    public bool CanStartRecording(AppRunState state, bool userClicked) =>
        userClicked &&
        _security.RecordingOnlyAfterUserAction &&
        !_config.HiddenRecordingAllowed &&
        state == AppRunState.Previewing;

    public void ValidateNoHiddenCapture(AppRunState state, bool captureActive, string context)
    {
        if (_config.PrivacyMode && captureActive && state == AppRunState.Idle)
            throw new InvalidOperationException($"Privacy guard blocked hidden capture: {context}");
    }
}
