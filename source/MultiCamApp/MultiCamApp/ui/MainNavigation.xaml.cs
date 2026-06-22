using System.Windows;
using System.Windows.Controls;

namespace MultiCamApp.Ui;

public partial class MainNavigation : UserControl
{
    public event EventHandler? MainSelected;
    public event EventHandler? VerificationSelected;
    public event EventHandler? HardwareSelected;

    public MainNavigation()
    {
        InitializeComponent();
    }

    public void ApplyLabels(string main, string verification, string hardware = "Hardware Diagnostics")
    {
        MainNavBtn.Content = main;
        VerifyNavBtn.Content = verification;
        HardwareNavBtn.Content = hardware;
    }

    public void SetActivePage(string page)
    {
        MainNavBtn.Style = (Style)FindResource(page == "main" ? "NavTabButtonActiveStyle" : "NavTabButtonStyle");
        VerifyNavBtn.Style = (Style)FindResource(page == "verification" ? "NavTabButtonActiveStyle" : "NavTabButtonStyle");
        HardwareNavBtn.Style = (Style)FindResource(page == "hardware" ? "NavTabButtonActiveStyle" : "NavTabButtonStyle");
    }

    private void MainNavBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActivePage("main");
        MainSelected?.Invoke(this, EventArgs.Empty);
    }

    private void VerifyNavBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActivePage("verification");
        VerificationSelected?.Invoke(this, EventArgs.Empty);
    }

    private void HardwareNavBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActivePage("hardware");
        HardwareSelected?.Invoke(this, EventArgs.Empty);
    }
}
