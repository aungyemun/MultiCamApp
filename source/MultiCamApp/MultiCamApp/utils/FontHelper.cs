using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiCamApp.Utils;

public static class FontHelper
{
    public static void ApplyLanguageFont(DependencyObject root, string languageCode)
    {
        var family = languageCode == "ja" ? "MS Gothic" : "Arial";
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
