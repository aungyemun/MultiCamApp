namespace MultiCamApp.Diagnostics;

public sealed class FileVerifier
{
    public bool ExistsAndNonEmpty(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    public (bool Ok, string Message) QuickCheck(string mp4Path)
    {
        if (!File.Exists(mp4Path))
            return (false, "File not found");
        var len = new FileInfo(mp4Path).Length;
        return len > 0 ? (true, $"OK ({len} bytes)") : (false, "Empty file");
    }
}
