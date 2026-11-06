using System.Runtime.InteropServices;
using System.Text;

namespace MultiCamApp.Diagnostics;

/// <summary>
/// 2.1.0: resolves which USB host controller (and which USB hubs) each camera is connected through,
/// by walking the Plug and Play device tree upwards with the Windows Configuration Manager API
/// (cfgmgr32). Only device <i>names</i> are returned (e.g. "Intel(R) USB 3.10 eXtensible Host
/// Controller"), never instance IDs or serial numbers. Read-only; it changes nothing.
/// </summary>
public static class UsbTopologyResolver
{
    public sealed record UsbPath(string Controller, IReadOnlyList<string> Hubs);

    /// <summary>
    /// WinRT camera ID ("\\?\USB#VID_046D&amp;PID_085C&amp;MI_00#7&amp;1a2b&amp;0&amp;0000#{guid}\GLOBAL")
    /// to the PnP instance ID ("USB\VID_046D&amp;PID_085C&amp;MI_00\7&amp;1a2b&amp;0&amp;0000").
    /// Returns null for IDs that are not device interface paths.
    /// </summary>
    public static string? ToInstanceId(string? cameraId)
    {
        if (string.IsNullOrWhiteSpace(cameraId)) return null;
        var s = cameraId.Trim();
        if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) s = s[4..];
        var guid = s.IndexOf("#{", StringComparison.Ordinal);
        if (guid <= 0) return null;
        s = s[..guid].Replace('#', '\\');
        return s.Contains('\\') ? s : null;
    }

    /// <summary>Walks up to 12 parents; null if the device is unknown or not on USB.</summary>
    public static UsbPath? Resolve(string? cameraId)
    {
        var instanceId = ToInstanceId(cameraId);
        if (instanceId is null || !OperatingSystem.IsWindows()) return null;
        try
        {
            if (CM_Locate_DevNodeW(out var node, instanceId, 0) != 0) return null;
            var hubs = new List<string>();
            var onUsb = instanceId.StartsWith("USB", StringComparison.OrdinalIgnoreCase);
            for (var depth = 0; depth < 12; depth++)
            {
                if (CM_Get_Parent(out var parent, node, 0) != 0) break;
                node = parent;
                var id = DeviceId(node);
                var name = DeviceName(node);
                if (id is null) break;
                if (id.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) onUsb = true;
                if (id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                    return onUsb ? new UsbPath(name ?? "USB host controller", hubs) : null;
                // External hubs only: root hubs belong to the controller and say nothing extra.
                if (name is not null && name.Contains("Hub", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Root Hub", StringComparison.OrdinalIgnoreCase))
                    hubs.Add(name);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not Windows / API unavailable: topology stays unknown.
        }
        return null;
    }

    /// <summary>
    /// Pure grouping for the report (unit-testable): cameras that share a host controller, and
    /// cameras that share an external hub (the stronger bandwidth limit).
    /// </summary>
    public static (List<string> notes, List<string> warnings) Summarize(
        IReadOnlyList<(string Camera, UsbPath? Path)> cameras,
        Func<string, string, string> text)
    {
        var notes = new List<string>();
        var warnings = new List<string>();
        var resolved = cameras.Where(c => c.Path is not null).ToList();
        foreach (var g in resolved.GroupBy(c => c.Path!.Controller).Where(g => g.Count() >= 2))
            notes.Add(string.Format(text("hwDiagUsbSharedController",
                "{0} cameras share one USB host controller ({1}): {2}. Its bandwidth is shared between them."),
                g.Count(), g.Key, string.Join(", ", g.Select(c => c.Camera))));
        foreach (var g in resolved.Where(c => c.Path!.Hubs.Count > 0)
                     .GroupBy(c => c.Path!.Controller + "|" + string.Join(">", c.Path!.Hubs))
                     .Where(g => g.Count() >= 2))
            warnings.Add(string.Format(text("hwDiagUsbSharedHub",
                "{0} cameras are connected through the same USB hub ({1}): {2}. For 720p or higher, connect them to separate USB ports."),
                g.Count(), g.First().Path!.Hubs[0], string.Join(", ", g.Select(c => c.Camera))));
        if (resolved.Count == 1)
            notes.Add(string.Format(text("hwDiagUsbSingleCamera", "{0} is connected to {1}."),
                resolved[0].Camera, resolved[0].Path!.Controller));
        else if (resolved.Count > 1 && notes.Count == 0 && warnings.Count == 0)
            notes.Add(text("hwDiagUsbSeparateControllers", "Each selected camera is on its own USB host controller."));
        return (notes, warnings);
    }

    private static string? DeviceId(uint node)
    {
        var buffer = new char[512];
        return CM_Get_Device_IDW(node, buffer, buffer.Length, 0) == 0 ? new string(buffer).TrimEnd('\0') : null;
    }

    private static string? DeviceName(uint node)
    {
        foreach (var prop in new[] { CM_DRP_FRIENDLYNAME, CM_DRP_DEVICEDESC })
        {
            uint length = 1024;
            var buffer = new byte[length];
            if (CM_Get_DevNode_Registry_PropertyW(node, prop, out _, buffer, ref length, 0) == 0 && length > 2)
            {
                var name = Encoding.Unicode.GetString(buffer, 0, (int)length).TrimEnd('\0').Trim();
                if (name.Length > 0) return name;
            }
        }
        return null;
    }

    private const uint CM_DRP_DEVICEDESC = 0x00000001;
    private const uint CM_DRP_FRIENDLYNAME = 0x0000000D;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint devInst, [Out] char[] buffer, int bufferLen, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, uint property, out uint regDataType,
        [Out] byte[] buffer, ref uint length, uint flags);
}
