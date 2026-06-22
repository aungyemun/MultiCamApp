using System.Text;
using MultiCamApp.Core;

namespace MultiCamApp.Utils;

public enum CameraOpenFailurePhase
{
    DeviceOpen,
    SetResolution,
    FirstFrame,
    PreviewRender,
    Release,
    Unknown
}

public static class CameraOpenFailureLog
{
    public static void Write(
        int slotIndex,
        string slotName,
        int layoutCount,
        string presetLabel,
        double requestedFps,
        string? deviceName,
        string? deviceId,
        CameraOpenFailurePhase phase,
        PreviewSlotFailureCategory category,
        string message,
        long elapsedMs,
        bool otherCamerasContinued,
        Exception? ex = null)
    {
        try
        {
            var dir = PathHelper.LogsFolder();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"camera_open_failure_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"timestamp={DateTime.Now:O}");
            sb.AppendLine($"appVersion={VersionService.Load().Display}");
            sb.AppendLine($"layout={layoutCount}");
            sb.AppendLine($"preset={presetLabel}");
            sb.AppendLine($"fps={requestedFps:F0}");
            sb.AppendLine($"slot={slotName}");
            sb.AppendLine($"slotIndex={slotIndex}");
            sb.AppendLine($"deviceName={deviceName ?? "?"}");
            sb.AppendLine($"deviceId={deviceId ?? "?"}");
            sb.AppendLine($"failurePhase={phase}");
            sb.AppendLine($"category={category}");
            sb.AppendLine($"message={message}");
            sb.AppendLine($"elapsedMs={elapsedMs}");
            sb.AppendLine($"otherCamerasContinued={otherCamerasContinued}");
            if (ex != null)
            {
                sb.AppendLine($"exceptionType={ex.GetType().FullName}");
                sb.AppendLine($"exceptionMessage={ex.Message}");
                sb.AppendLine(ex.StackTrace);
            }

            File.WriteAllText(path, PrivacySanitizer.SanitizeForLog(sb.ToString()), Encoding.UTF8);
        }
        catch { }
    }
}
