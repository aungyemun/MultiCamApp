namespace MultiCamApp.Capture;

/// <summary>Per-slot preview lifecycle state (UI and readiness gating).</summary>
public enum PreviewSlotStateKind
{
    Idle,
    Opening,
    PreviewReady,
    FailedUnsupportedPreset,
    FailedDeviceOpen,
    LostConnection
}
