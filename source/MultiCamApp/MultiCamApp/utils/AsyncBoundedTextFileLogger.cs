using System.Threading.Channels;
using System.Text;

namespace MultiCamApp.Utils;

/// <summary>
/// Best-effort, non-blocking text logger for app-folder debug logs.
/// - Bounded queue: if full, lines are dropped.
/// - Max bytes: stops writing after the file reaches the limit.
/// - Never throws to callers.
/// </summary>
internal sealed class AsyncBoundedTextFileLogger : IDisposable
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly Channel<string> _queue;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _cts = new();
    private long _bytes;

    public string Path => _path;

    public AsyncBoundedTextFileLogger(
        string path,
        string initialHeader,
        int queueCapacity = 2048,
        long maxBytes = 2 * 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            // Header is written synchronously so the file exists immediately.
            File.WriteAllText(path, PrivacySanitizer.SanitizeForLog(initialHeader ?? string.Empty), Encoding.UTF8);
            _bytes = new FileInfo(path).Length;
        }
        catch
        {
            // If IO fails, we still create the object; writes become no-ops.
            _bytes = _maxBytes;
        }

        _writerTask = Task.Run(WriterLoop, _cts.Token);
    }

    public void Line(string text)
    {
        if (_cts.IsCancellationRequested) return;
        var line = PrivacySanitizer.SanitizeForLog(text ?? string.Empty);
        _queue.Writer.TryWrite(line.EndsWith('\n') ? line : line + Environment.NewLine);
    }

    private async Task WriterLoop()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var sw = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            await foreach (var item in _queue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                if (Interlocked.Read(ref _bytes) >= _maxBytes)
                    continue;

                try
                {
                    var len = Encoding.UTF8.GetByteCount(item);
                    if (Interlocked.Read(ref _bytes) + len > _maxBytes)
                    {
                        // Best-effort: stop growing beyond the limit.
                        Interlocked.Exchange(ref _bytes, _maxBytes);
                        continue;
                    }

                    await sw.WriteAsync(item).ConfigureAwait(false);
                    Interlocked.Add(ref _bytes, len);
                }
                catch
                {
                    // Never fail the app due to logging.
                }
            }
        }
        catch
        {
            // Ignore failures (no throw).
        }
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _queue.Writer.TryComplete();
            try { _writerTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        }
        catch { }
        finally { _cts.Dispose(); }
    }
}

