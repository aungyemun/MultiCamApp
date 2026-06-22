using System.Runtime.InteropServices;

namespace MultiCamApp.Capture;

/// <summary>Minimal DirectShow COM types for video input device enumeration.</summary>
internal static class DirectShowInterop
{
    internal static readonly Guid VideoInputDeviceCategory =
        new("860BB310-5D01-11d0-BD3B-00A0C911CE86");

    [ComImport]
    [Guid("298B22AB-0C5C-11CF-A4B8-00455519BC2B")]
    internal class CreateDevEnumClass { }

    [ComImport]
    [Guid("298B22AB-0C5C-11CF-A4B8-00455519BC2B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator([In] ref Guid type, out IEnumMoniker enumMoniker, int flags);
    }

    [ComImport]
    [Guid("B196B28F-BAB4-101A-B69C-00AA00341D07")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyBag
    {
        void Read([In, MarshalAs(UnmanagedType.LPWStr)] string name, [Out] out object? value, IntPtr errorLog);
        void Write([In, MarshalAs(UnmanagedType.LPWStr)] string name, [In] ref object value);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyBag2
    {
        void Read([In] uint count, [In] ref object name, IntPtr errorLog, [Out] out object? value, IntPtr status);
    }

    [ComImport]
    [Guid("00000102-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumMoniker
    {
        [PreserveSig]
        int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IMoniker[]? monikers, IntPtr fetched);

        void Skip(int count);
        void Reset();
        void Clone(out IEnumMoniker enumMoniker);
    }

    [ComImport]
    [Guid("0000000f-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMoniker
    {
        void BindToObject(IntPtr pbc, IntPtr pmkToLeft, ref Guid riidResult, [MarshalAs(UnmanagedType.Interface)] out object result);
        void BindToStorage(IntPtr pbc, IntPtr pmkToLeft, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object obj);
        void Reduce(IntPtr pbc, int dwReduceHowFar, ref IMoniker ppmkToLeft, out IMoniker ppmkReduced);
        void ComposeWith(IMoniker pmkRight, bool fOnlyIfNotGeneric, out IMoniker ppmkComposite);
        void Enum(bool fForward, out IEnumMoniker ppenumMoniker);
        void IsEqual(IMoniker pmkOtherMoniker);
        void Hash(out int pdwHash);
        void IsRunning(IntPtr pbc, IMoniker pmkToLeft, IMoniker pmkNewlyRunning);
        void GetTimeOfLastChange(IntPtr pbc, IMoniker pmkToLeft, out long pfileTime);
        void Inverse(out IMoniker ppmk);
        void CommonPrefixWith(IMoniker pmkOther, out IMoniker ppmkPrefix);
        void RelativePathTo(IMoniker pmkOther, out IMoniker ppmkRelPath);
        void GetDisplayName(IntPtr pbc, IntPtr pmkToLeft, [MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        void ParseDisplayName(IntPtr pbc, IntPtr pmkToLeft, [MarshalAs(UnmanagedType.LPWStr)] string displayName, out int pchEaten, out IMoniker ppmkOut);
        void IsSystemMoniker(out int pdwMksys);
    }
}
