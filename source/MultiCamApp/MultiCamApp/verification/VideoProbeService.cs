////////////////////////////////////////////////////
/// STABLE_CORE_V1
/// Validated in MultiCamApp v1.0.36 build 136.
/// Do not modify without documented regression testing.
/// Protected: recording, metadata, verification, session comparison.
////////////////////////////////////////////////////
// STABLE_CORE_V1 protected component — modification requires regression checklist; do not refactor casually.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MultiCamApp.Core;

namespace MultiCamApp.Verification;

public sealed class VideoProbeService
{
    private readonly VerificationSettings _settings;
    private string? _ffprobePath;
    private bool _resolved;

    public VideoProbeService(VerificationSettings settings) => _settings = settings;

    public bool IsAvailable => ResolveFfprobe() != null;

    public string? MissingToolMessage =>
        "Video verification tool is missing. Please reinstall MultiCamApp or check runtime/ffmpeg.";

    public async Task<VideoProbeData> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = new VideoProbeData();
        if (!File.Exists(filePath))
        {
            result.Error = "File does not exist";
            return result;
        }

        var info = new FileInfo(filePath);
        result.FileSizeBytes = info.Length;
        if (info.Length <= 0)
        {
            result.Error = "File size is zero";
            return result;
        }

        if (!string.Equals(Path.GetExtension(filePath), ".mp4", StringComparison.OrdinalIgnoreCase))
            result.Error = "Extension is not .mp4";

        var ffprobe = ResolveFfprobe();
        if (ffprobe == null)
        {
            result.Error = MissingToolMessage;
            return result;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                result.Error = "Could not start ffprobe";
                return result;
            }

            var stdout = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);

            if (proc.ExitCode != 0)
            {
                result.Error = string.IsNullOrWhiteSpace(stderr) ? $"ffprobe exit {proc.ExitCode}" : stderr.Trim();
                return result;
            }

            ParseFfprobeJson(stdout, result);
            result.Success = result.HasVideoStream && result.DurationSeconds > 0;
            if (!result.HasVideoStream)
                result.Error = "No video stream found";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    private void ParseFfprobeJson(string json, VideoProbeData result)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("format", out var fmt))
        {
            if (fmt.TryGetProperty("format_name", out var fn))
                result.ContainerFormat = fn.GetString();
            if (fmt.TryGetProperty("duration", out var dur) &&
                double.TryParse(dur.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                result.DurationSeconds = d;
            if (fmt.TryGetProperty("bit_rate", out var br) && long.TryParse(br.GetString(), out var b))
                result.BitRate = b;
            if (fmt.TryGetProperty("nb_streams", out _)) { }
        }

        if (!root.TryGetProperty("streams", out var streams)) return;
        foreach (var stream in streams.EnumerateArray())
        {
            if (!stream.TryGetProperty("codec_type", out var ct)) continue;
            var type = ct.GetString();
            if (type == "video" && !result.HasVideoStream)
            {
                result.HasVideoStream = true;
                if (stream.TryGetProperty("codec_name", out var codec))
                    result.VideoCodec = codec.GetString();
                if (stream.TryGetProperty("width", out var w)) result.Width = w.GetInt32();
                if (stream.TryGetProperty("height", out var h)) result.Height = h.GetInt32();
                if (stream.TryGetProperty("pix_fmt", out var pf)) result.PixelFormat = pf.GetString();
                if (stream.TryGetProperty("nb_frames", out var nbf) &&
                    long.TryParse(nbf.GetString(), out var fc))
                    result.FrameCount = fc;
                if (stream.TryGetProperty("avg_frame_rate", out var afr))
                {
                    result.AvgFrameRateRaw = afr.GetString();
                    result.Fps = ParseFrameRate(afr.GetString());
                }
                else if (stream.TryGetProperty("r_frame_rate", out var rfr))
                {
                    result.RFrameRateRaw = rfr.GetString();
                    result.Fps = ParseFrameRate(rfr.GetString());
                }
                if (stream.TryGetProperty("r_frame_rate", out var rfr2))
                    result.RFrameRateRaw = rfr2.GetString();
                if (stream.TryGetProperty("avg_frame_rate", out var afr2))
                    result.AvgFrameRateRaw = afr2.GetString();
                var avg = ParseFrameRate(result.AvgFrameRateRaw);
                var raw = ParseFrameRate(result.RFrameRateRaw);
                if (avg > 0 && raw > 0)
                    result.ConstantFps = Math.Abs(avg - raw) <= 0.05;
            }
            else if (type == "audio")
                result.HasAudioStream = true;
        }
    }

    private static double ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0/0") return 0;
        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0)
            return n / d;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private string? ResolveFfprobe()
    {
        if (_resolved) return _ffprobePath;
        _resolved = true;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_settings.FfprobePath))
        {
            if (Path.IsPathRooted(_settings.FfprobePath))
                candidates.Add(_settings.FfprobePath);
            else
                candidates.Add(Path.Combine(AppContext.BaseDirectory, _settings.FfprobePath));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "runtime", "ffmpeg", "ffprobe.exe"));
        var root = Environment.GetEnvironmentVariable("MULTICAMAPP_ROOT");
        if (!string.IsNullOrEmpty(root))
            candidates.Add(Path.Combine(root, "runtime", "ffmpeg", "ffprobe.exe"));

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                _ffprobePath = c;
                return _ffprobePath;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', Path.PathSeparator))
        {
            var exe = Path.Combine(dir.Trim(), "ffprobe.exe");
            if (File.Exists(exe))
            {
                _ffprobePath = exe;
                return _ffprobePath;
            }
        }

        return null;
    }
}
