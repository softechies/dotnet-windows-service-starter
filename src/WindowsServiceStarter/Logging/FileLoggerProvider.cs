using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace WindowsServiceStarter.Logging;

/// <summary>
/// Small rolling file logger with no third party dependencies.
/// Log calls only push to an in memory queue; a single background task writes to disk,
/// so a slow disk never blocks the service.
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int QueueCapacity = 10_000;

    private readonly FileLoggerOptions _options;
    private readonly TimeProvider _time;
    private readonly string _directory;
    private readonly Channel<string> _queue;
    private readonly Task _writerTask;

    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private int _currentIndex;

    public FileLoggerProvider(IOptions<FileLoggerOptions> options)
        : this(options.Value, TimeProvider.System)
    {
    }

    internal FileLoggerProvider(FileLoggerOptions options, TimeProvider time)
    {
        _options = options;
        _time = time;
        _directory = Path.IsPathRooted(options.Directory)
            ? options.Directory
            : Path.Combine(AppContext.BaseDirectory, options.Directory);

        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _writerTask = Task.Run(ProcessQueueAsync);
    }

    internal string LogDirectory => _directory;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal DateTimeOffset Now => _time.GetLocalNow();

    internal void Enqueue(string line) => _queue.Writer.TryWrite(line);

    private async Task ProcessQueueAsync()
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var line))
            {
                try
                {
                    GetWriter().Write(line);
                }
                catch (Exception ex)
                {
                    // Logging must never take the service down.
                    Console.Error.WriteLine($"FileLogger write failed: {ex.Message}");
                    CloseWriter();
                }
            }

            // Queue drained: flush once instead of after every line.
            try { _writer?.Flush(); } catch { CloseWriter(); }
        }

        CloseWriter();
    }

    private StreamWriter GetWriter()
    {
        var today = DateOnly.FromDateTime(Now.DateTime);

        if (_writer is null || today != _currentDate)
        {
            CloseWriter();
            _currentDate = today;
            System.IO.Directory.CreateDirectory(_directory);
            DeleteExpiredFiles();
            _currentIndex = FindLatestIndex(today);
            _writer = OpenWriter(today, _currentIndex);
        }

        // Also covers reopening a file that was already full before a restart.
        while (IsFull(_writer))
        {
            CloseWriter();
            _currentIndex++;
            _writer = OpenWriter(today, _currentIndex);
        }

        return _writer;
    }

    private bool IsFull(StreamWriter writer) =>
        _options.MaxFileSizeMB > 0 && writer.BaseStream.Length >= _options.MaxFileSizeMB * 1024L * 1024L;

    private StreamWriter OpenWriter(DateOnly date, int index)
    {
        // FileShare.ReadWrite lets you tail the log in Notepad++ or PowerShell while the service runs.
        var stream = new FileStream(BuildPath(date, index), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        return new StreamWriter(stream, new UTF8Encoding(false));
    }

    internal string BuildPath(DateOnly date, int index)
    {
        var name = $"{_options.FileNamePrefix}-{date:yyyyMMdd}";
        if (index > 0)
        {
            name += "_" + index.ToString(CultureInfo.InvariantCulture);
        }
        return Path.Combine(_directory, name + ".log");
    }

    private int FindLatestIndex(DateOnly date)
    {
        // After a restart, continue in the newest file for today instead of starting over at _0.
        var index = 0;
        while (File.Exists(BuildPath(date, index + 1)))
        {
            index++;
        }
        return index;
    }

    private void DeleteExpiredFiles()
    {
        if (_options.RetainDays <= 0)
        {
            return;
        }

        var cutoff = Now.UtcDateTime.AddDays(-_options.RetainDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, _options.FileNamePrefix + "-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // In use or already gone, try again tomorrow.
            }
        }
    }

    private void CloseWriter()
    {
        try { _writer?.Dispose(); } catch { /* ignore */ }
        _writer = null;
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        // Give pending lines a chance to reach the disk on shutdown.
        _writerTask.Wait(TimeSpan.FromSeconds(5));
    }
}
