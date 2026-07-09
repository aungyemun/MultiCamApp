using System.Windows.Media;

namespace MultiCamApp.Ui;

/// <summary>Dark coding-dashboard preview styling (UI only — not recorded into video).</summary>
public static class PreviewPanelTheme
{
    public static readonly Brush HostBackground = Brush("#080A0C");
    public static readonly Brush CardBackground = Brush("#0F172A");
    public static readonly Brush HeaderBarBackground = Brush("#111827");
    public static readonly Brush InnerVideoBorder = Brush("#334155");
    public static readonly Brush LabelForeground = Brush("#F9FAFB");
    public static readonly Brush StatsForeground = Brush("#CBD5E1");
    public static readonly Brush ActiveDot = Brush("#22C55E");

    private static readonly Color[] CamBorderColors =
    [
        Color.FromRgb(0x3B, 0x82, 0xF6),
        Color.FromRgb(0xD9, 0x46, 0xEF),
        Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0x06, 0xB6, 0xD4)
    ];

    public static Brush GetCamBorderBrush(int slotIndex) =>
        new SolidColorBrush(CamBorderColors[Math.Clamp(slotIndex, 0, 3)]);

    public static Brush GetCamAccentBrush(int slotIndex) => GetCamBorderBrush(slotIndex);

    public static string SlotLabel(int slotIndex) => $"CAM{slotIndex + 1}";

    private static SolidColorBrush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
