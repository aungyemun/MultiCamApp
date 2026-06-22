namespace MultiCamApp.Core;

/// <summary>Privacy checks; delegates to <see cref="SecurityPolicyService"/>.</summary>
public sealed class PrivacyGuardService
{
    private readonly SecurityPolicyService _policy;
    private readonly AppConfig _config;

    public PrivacyGuardService(AppConfig config)
    {
        _config = config;
        _policy = new SecurityPolicyService(config);
    }

    public SecurityPolicyService Policy => _policy;

    public bool HiddenRecordingAllowed => _config.HiddenRecordingAllowed && !_config.PrivacyMode;

    public bool CanStartPreview(AppRunState state, bool userClicked) =>
        _policy.CanStartPreview(state, userClicked);

    public bool CanStartRecording(AppRunState state, bool userExplicitClick) =>
        _policy.CanStartRecording(state, userExplicitClick);

    public bool ShouldPausePreviewOnMinimize(AppRunState state) =>
        _config.PausePreviewOnMinimize && state == AppRunState.Previewing;

    public bool ShouldContinueRecordingWhenMinimized(AppRunState state, bool userStartedRecording) =>
        state == AppRunState.Recording &&
        userStartedRecording &&
        _config.AllowRecordingWhileMinimizedOnlyIfUserStartedRecording;

    public void ValidateNoHiddenCapture(AppRunState state, bool captureActive, string context) =>
        _policy.ValidateNoHiddenCapture(state, captureActive, context);
}
