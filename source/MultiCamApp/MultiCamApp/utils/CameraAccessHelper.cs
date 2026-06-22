using System.Diagnostics;

namespace MultiCamApp.Utils;

public static class CameraAccessHelper
{
    private const int HResultAccessDenied = unchecked((int)0x80070005);

    public static bool IsAccessDenied(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
            return true;
        if (ex.HResult == HResultAccessDenied)
            return true;
        var msg = ex.Message;
        return msg.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("access denied", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("privacy", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("0x80070005", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAccessDeniedMessage(string? message) =>
        !string.IsNullOrEmpty(message) &&
        (message.Contains("access", StringComparison.OrdinalIgnoreCase) &&
         message.Contains("denied", StringComparison.OrdinalIgnoreCase)
         || message.Contains("privacy", StringComparison.OrdinalIgnoreCase)
         || message.Contains("blocked", StringComparison.OrdinalIgnoreCase));

    public static void OpenWindowsCameraPrivacySettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-webcam")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:privacy")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                /* settings URI unavailable */
            }
        }
    }
}
