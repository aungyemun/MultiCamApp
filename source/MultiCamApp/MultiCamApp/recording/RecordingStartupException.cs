namespace MultiCamApp.Recording;

public enum RecordingStartupFailureKind
{
    WriterCreation,
    FirstFrameTimeout,
    Other
}

/// <summary>Raised when a selected camera cannot start recording; triggers safe rollback.</summary>
public sealed class RecordingStartupException : Exception
{
    public string SlotName { get; }
    public RecordingStartupFailureKind Kind { get; }

    public RecordingStartupException(string slotName, RecordingStartupFailureKind kind, string message)
        : base(message)
    {
        SlotName = slotName;
        Kind = kind;
    }

    public RecordingStartupException(string slotName, Exception inner)
        : base($"{slotName} failed to start recording.", inner)
    {
        SlotName = slotName;
        Kind = RecordingStartupFailureKind.Other;
    }
}
