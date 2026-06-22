////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Runtime.InteropServices;
using System.Diagnostics;
using MultiCamApp.Utils;

namespace MultiCamApp.Capture;

/// <summary>Enumerates DirectShow video capture devices in OpenCV index order.</summary>
public static class DirectShowVideoDeviceEnumerator
{
    private static readonly LogService Log = new();
    private static int _comInit;

    public sealed class DirectShowVideoDevice
    {
        public int OpenCvIndex { get; init; }
        public string FriendlyName { get; init; } = "";
        public string DevicePath { get; init; } = "";
        public string MatchKey { get; init; } = "";
    }

    public static IReadOnlyList<DirectShowVideoDevice> Enumerate()
    {
        var sw = Stopwatch.StartNew();
        EnsureComApartment();
        var list = new List<DirectShowVideoDevice>();
        DirectShowInterop.IEnumMoniker? enumMoniker = null;

        try
        {
            var comType = Type.GetTypeFromProgID("System.DeviceEnum", throwOnError: false)
                          ?? Type.GetTypeFromCLSID(new Guid("298B22AB-0C5C-11CF-A4B8-00455519BC2B"), throwOnError: false);
            if (comType == null)
            {
                Log.Info("camera", "DirectShow System.DeviceEnum not available");
                return list;
            }

            var devEnum = (DirectShowInterop.ICreateDevEnum)Activator.CreateInstance(comType)!;
            var category = DirectShowInterop.VideoInputDeviceCategory;
            var hr = devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0);
            if (hr != 0 || enumMoniker == null)
                return list;

            var monikers = new DirectShowInterop.IMoniker[1];
            var index = 0;
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];
                if (moniker == null) break;

                try
                {
                    var bagGuid = typeof(DirectShowInterop.IPropertyBag).GUID;
                    moniker.BindToStorage(IntPtr.Zero, IntPtr.Zero, ref bagGuid, out var storageObj);
                    var bag = (DirectShowInterop.IPropertyBag)storageObj;

                    bag.Read("FriendlyName", out var nameObj, IntPtr.Zero);
                    bag.Read("DevicePath", out var pathObj, IntPtr.Zero);

                    var friendly = nameObj as string ?? "";
                    var path = pathObj as string ?? "";
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    list.Add(new DirectShowVideoDevice
                    {
                        OpenCvIndex = index++,
                        FriendlyName = friendly,
                        DevicePath = path,
                        MatchKey = DirectShowDeviceMatcher.BuildMatchKey(path)
                    });
                }
                catch (Exception ex)
                {
                    Log.Info("camera", $"DirectShow enum skip: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("camera", "DirectShow enumeration failed", ex);
        }
        finally
        {
            if (enumMoniker != null)
                Marshal.ReleaseComObject(enumMoniker);
        }

        Log.Info("camera",
            $"DirectShow devices ({list.Count}): {string.Join(" | ", list.Select(d => $"[{d.OpenCvIndex}] {d.FriendlyName}"))}");

        if (PreviewStartTrace.IsActive)
        {
            PreviewStartTrace.NotifyDiscoveryTimed(
                "DirectShowVideoDeviceEnumerator.Enumerate",
                $"COM list of all video devices ({list.Count} found); maps selected devices only",
                sw.ElapsedMilliseconds,
                warnRefresh: false);
        }

        return list;
    }

    private static void EnsureComApartment()
    {
        if (Interlocked.Exchange(ref _comInit, 1) == 1) return;
        try { LoadLibrary("quartz.dll"); } catch { /* optional */ }
        CoInitializeEx(IntPtr.Zero, 2 /* COINIT_APARTMENTTHREADED */);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}
