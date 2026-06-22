using System.Windows;
using System.Windows.Controls;

namespace MultiCamApp.Ui;

public sealed class ResponsiveLayoutManager
{
    public void Apply(Grid host, IReadOnlyList<UIElement> cells, int cameraCount, double hostWidth, double hostHeight)
    {
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
