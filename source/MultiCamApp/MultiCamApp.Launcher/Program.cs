using System.Diagnostics;
using System.Windows.Forms;

namespace MultiCamApp.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!TryResolveApp(out var exePath, out var workDir))
        {
            MessageBox.Show(
                "MultiCamApp application files were not found.\n\n" +
                "Developer copy: run installer\\build_release.bat so dist\\MultiCamApp.exe exists, " +
                "then use this launcher at the project root.\n\n" +
                "End users: install with installer\\setup.exe and run MultiCamApp from the Start menu.",
                "MultiCamApp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                WorkingDirectory = workDir,
                UseShellExecute = true
            });
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not start MultiCamApp:\n{ex.Message}",
                "MultiCamApp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool TryResolveApp(out string exePath, out string workDir)
    {
        foreach (var root in EnumerateSearchRoots())
        {
            var distExe = Path.Combine(root, "dist", "MultiCamApp.exe");
            if (File.Exists(distExe))
            {
                exePath = distExe;
                workDir = Path.GetDirectoryName(distExe)!;
                return true;
            }
        }

        exePath = "";
        workDir = "";
        return false;
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (seen.Add(dir.FullName))
                yield return dir.FullName;
            dir = dir.Parent;
        }
    }
}
