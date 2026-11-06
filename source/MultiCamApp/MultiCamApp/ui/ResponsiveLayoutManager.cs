using System.Windows;
using System.Windows.Controls;

namespace MultiCamApp.Ui;

public sealed class ResponsiveLayoutManager
{
    /// <summary>Aspect ratio assumed for camera video when choosing an arrangement (16:9 presets).</summary>
    public double VideoAspect { get; set; } = 16.0 / 9.0;

    private (int Count, bool Stacked)? _lastArrangement;

    /// <summary>
    /// For two cameras, stack them vertically when that shows the video larger than side-by-side.
    /// In the default window the preview area is ~1.5:1, where side-by-side gives each 16:9 video
    /// only ~40 % of its panel; stacking gives ~45 % more visible video area.
    /// </summary>
    public static bool ShouldStackTwo(double hostWidth, double hostHeight, double aspect)
    {
        if (hostWidth <= 0 || hostHeight <= 0 || aspect <= 0) return false;
        static double Fit(double w, double h, double a) { var vw = Math.Min(w, h * a); return vw * (vw / a); }
        return Fit(hostWidth, hostHeight / 2, aspect) > Fit(hostWidth / 2, hostHeight, aspect) * 1.05;
    }

    public void Apply(Grid host, IReadOnlyList<UIElement> cells, int cameraCount, double hostWidth, double hostHeight)
    {
        var stacked = cameraCount == 2 && ShouldStackTwo(hostWidth, hostHeight, VideoAspect);
        // Nothing changed (e.g. a window resize that keeps the arrangement): don't tear down and
        // re-add the cells — they can host the Direct3D preview HwndHost, which would be re-parented.
        if (_lastArrangement == (cameraCount, stacked)
            && host.Children.Count == Math.Min(cameraCount, cells.Count)
            && Enumerable.Range(0, host.Children.Count).All(i => ReferenceEquals(host.Children[i], cells[i])))
            return;
        _lastArrangement = (cameraCount, stacked);

        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();
        if (cameraCount <= 0) return;

        void Place(UIElement cell, int row, int col, int rowSpan = 1, int colSpan = 1)
        {
            host.Children.Add(cell);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            Grid.SetRowSpan(cell, rowSpan);
            Grid.SetColumnSpan(cell, colSpan);
            cell.Visibility = Visibility.Visible;
        }

        for (var i = 0; i < cells.Count; i++)
            cells[i].Visibility = i < cameraCount ? Visibility.Visible : Visibility.Collapsed;

        switch (cameraCount)
        {
            case 1:
                host.RowDefinitions.Add(RowStar());
                Place(cells[0], 0, 0);
                break;
            case 2 when stacked:
                host.RowDefinitions.Add(RowStar());
                host.RowDefinitions.Add(RowStar());
                Place(cells[0], 0, 0);
                Place(cells[1], 1, 0);
                break;
            case 2:
                host.ColumnDefinitions.Add(ColStar());
                host.ColumnDefinitions.Add(ColStar());
                Place(cells[0], 0, 0);
                Place(cells[1], 0, 1);
                break;
            case 3:
                host.RowDefinitions.Add(RowStar());
                host.RowDefinitions.Add(RowStar());
                host.ColumnDefinitions.Add(ColStar());
                host.ColumnDefinitions.Add(ColStar());
                Place(cells[0], 0, 0);
                Place(cells[1], 0, 1);
                Place(cells[2], 1, 0, colSpan: 2);
                break;
            default:
                host.RowDefinitions.Add(RowStar());
                host.RowDefinitions.Add(RowStar());
                host.ColumnDefinitions.Add(ColStar());
                host.ColumnDefinitions.Add(ColStar());
                Place(cells[0], 0, 0);
                Place(cells[1], 0, 1);
                Place(cells[2], 1, 0);
                Place(cells[3], 1, 1);
                break;
        }
    }

    private static RowDefinition RowStar() =>
        new() { Height = new GridLength(1, GridUnitType.Star) };

    private static ColumnDefinition ColStar() =>
        new() { Width = new GridLength(1, GridUnitType.Star) };
}
