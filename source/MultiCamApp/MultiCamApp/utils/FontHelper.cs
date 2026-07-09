using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiCamApp.Utils;

public static class FontHelper
{
    // "MS Gothic" is a legacy bitmap font (no ClearType/scaling) — replaced with a modern
    // CJK-aware fallback chain. WPF parses a comma-separated FontFamily string as an ordered
    // fallback list, so if "Yu Gothic UI" is unavailable it tries Meiryo, then Segoe UI/Arial.
    private const string JapaneseFontFamily = "Yu Gothic UI, Meiryo UI, Meiryo, Segoe UI, Arial";
    private const string EnglishFontFamily  = "Segoe UI, Arial";

    public static void ApplyLanguageFont(DependencyObject root, string languageCode)
    {
        var family = languageCode == "ja" ? JapaneseFontFamily : EnglishFontFamily;
        ApplyRecursive(root, family);
    }

    private static void ApplyRecursive(DependencyObject obj, string family)
    {
        if (obj is TextBlock tb)
            tb.FontFamily = new FontFamily(family);
        else if (obj is TextBox box)
            box.FontFamily = new FontFamily(family);
        else if (obj is Button btn)
            btn.FontFamily = new FontFamily(family);
        else if (obj is ComboBox cb)
            cb.FontFamily = new FontFamily(family);
        else if (obj is RadioButton rb)
            rb.FontFamily = new FontFamily(family);
        else if (obj is Label lbl)
            lbl.FontFamily = new FontFamily(family);

        if (obj is FrameworkElement fe)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(fe))
            {
                if (child is DependencyObject d)
                    ApplyRecursive(d, family);
            }
        }
    }
}
