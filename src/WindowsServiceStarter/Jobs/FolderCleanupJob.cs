using Microsoft.Extensions.Options;

namespace WindowsServiceStarter.Jobs;

public sealed class FolderCleanupOptions
{
    public const string SectionName = "FolderCleanup";

    /// <summary>Folder to clean. Empty means the job does nothing.</summary>
    public string Path { get; set; } = "";

    /// <summary>Only files matching this pattern are considered, e.g. *.tmp</summary>
    public string SearchPattern { get; set; } = "*";

    /// <summary>Files last written more than this many days ago are deleted.</summary>
    public int OlderThanDays { get; set; } = 30;

    public bool Recursive { get; set; }

    /// <summary>Log what would be deleted without deleting anything.</summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>
/// Deletes old files from a folder. A typical chore for a background service:
/// export folders, temp uploads, old reports.
/// Ships with DryRun = true so nothing is deleted until you turn it off.
/// </summary>
public sealed class FolderCleanupJob : IJob
{
    private readonly IOptionsMonitor<FolderCleanupOptions> _options;
    private readonly ILogger<FolderCleanupJob> _logger;
    private readonly TimeProvider _time;

    public FolderCleanupJob(IOptionsMonitor<FolderCleanupOptions> options, ILogger<FolderCleanupJob> logger)
        : this(options, logger, TimeProvider.System)
    {
    }

    internal FolderCleanupJob(IOptionsMonitor<FolderCleanupOptions> options, ILogger<FolderCleanupJob> logger, TimeProvider time)
    {
        _options = options;
        _logger = logger;
        _time = time;
    }

    public string Name => "FolderCleanup";

    public Task RunAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(options.Path))
        {
            _logger.LogDebug("FolderCleanup: no path configured, skipping");
            return Task.CompletedTask;
        }

        if (!Directory.Exists(options.Path))
        {
            _logger.LogWarning("FolderCleanup: folder {Path} does not exist", options.Path);
            return Task.CompletedTask;
        }

        var cutoff = _time.GetUtcNow().UtcDateTime.AddDays(-options.OlderThanDays);
        var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        int deleted = 0, failed = 0;
        long bytes = 0;

        foreach (var file in Directory.EnumerateFiles(options.Path, options.SearchPattern, searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new FileInfo(file);
            if (info.LastWriteTimeUtc >= cutoff)
            {
                continue;
            }

            if (options.DryRun)
            {
                _logger.LogInformation("FolderCleanup (dry run): would delete {File}", file);
                deleted++;
                bytes += info.Length;
                continue;
            }

            try
            {
                var length = info.Length;
                info.Delete();
                deleted++;
                bytes += length;
            }
            catch (IOException ex)
            {
                // File in use: try again on the next run.
                failed++;
                _logger.LogWarning("FolderCleanup: could not delete {File}: {Message}", file, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                failed++;
                _logger.LogWarning("FolderCleanup: access denied for {File}: {Message}", file, ex.Message);
            }
        }

        if (deleted > 0 || failed > 0)
        {
            _logger.LogInformation(
                "FolderCleanup: {Verb} {Deleted} file(s), {Mb:F2} MB, {Failed} failed",
                options.DryRun ? "matched" : "deleted", deleted, bytes / 1024d / 1024d, failed);
        }

        return Task.CompletedTask;
    }
}
