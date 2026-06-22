using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using WinBuffer = Windows.Storage.Streams.Buffer;

namespace MultiCamApp.Utils;

public static class PreviewImageHelper
{
    public static void TrySetImage(System.Windows.Controls.Image image, SoftwareBitmap bitmap)
    {
        if (bitmap == null) return;
        var source = CreateBitmapSource(bitmap);
        if (source != null)
            image.Source = source;
    }

    public static void TrySetBitmapFromSoftwareBitmap(Action<BitmapSource> deliver, SoftwareBitmap bitmap)
    {
        var source = CreateBitmapSource(bitmap);
        if (source != null)
            deliver(source);
    }

    public static BitmapSource? CreateBitmapSource(SoftwareBitmap bitmap)
    {
        if (bitmap == null) return null;

        using var converted = SoftwareBitmap.Convert(
            bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var buffer = new WinBuffer((uint)(converted.PixelWidth * converted.PixelHeight * 4));
        converted.CopyToBuffer(buffer);
        var pixels = buffer.ToArray();
        var wb = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            96, 96,
            PixelFormats.Bgra32,
            null,
            pixels,
            converted.PixelWidth * 4);
        wb.Freeze();
        return wb;
    }
}
