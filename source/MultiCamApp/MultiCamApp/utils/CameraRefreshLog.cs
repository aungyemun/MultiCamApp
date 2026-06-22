using System.Text;
using MultiCamApp.Capture;
using MultiCamApp.Core;

namespace MultiCamApp.Utils;

public static class CameraRefreshLog
{
    public static CameraRefreshLogSession Begin() => new();
}

public sealed class CameraRefreshLogSession : IDisposable
{
    private readonly string _path;
    private readonly StringBuilder _sb = new();
    private bool _closed;

    internal CameraRefreshLogSession()
    {
        var dir = PathHelper.LogsFolder();
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"camera_refresh_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        Line($"refreshClicked={DateTime.Now:O}");
        Line($"appVersion={VersionService.Load().Display}");
    }

    public void Line(string text) => _sb.AppendLine($"{DateTime.Now:HH:mm:ss.fff} {PrivacySanitizer.SanitizeForLog(text)}");

    public void WriteSummary(
        int deviceCount,
        IReadOnlyList<CameraDevice> devices,
        IReadOnlyList<string?> previousSelections,
        IReadOnlyList<bool> slotPreserved,
        IReadOnlyList<bool> slotMissing)
    {
        Line($"devicesFound={deviceCount}");
        for (var i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            OpenCvDirectShowIndexCatalog.TryGetIndex(d.Id, out var dshowIndex);
            var dshowText = dshowIndex >= 0 ? dshowIndex.ToString() : "n/a";
            Line($"device[{i}] display=\"{d.DisplayName}\" name=\"{d.Name}\" id={d.Id} enumIndex={d.EnumerationIndex} dshowIndex={dshowText}");
        }

        for (var slot = 0; slot < 4; slot++)
        {
            var prev = slot < previousSelections.Count ? previousSelections[slot] : null;
            var preserved = slot < slotPreserved.Count && slotPreserved[slot];
            var missing = slot < slotMissing.Count && slotMissing[slot];
            if (string.IsNullOrEmpty(prev))
            {
                Line($"cam{slot + 1} previous=<none> preserved=n/a missing=no");
                continue;
            }

            Line($"cam{slot + 1} previousId={prev} preserved={(preserved ? "yes" : "no")} missing={(missing ? "yes" : "no")}");
        }
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        try
        {
            File.WriteAllText(_path, _sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }
}
