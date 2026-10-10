using Microsoft.Extensions.Logging.Abstractions;
using WindowsServiceStarter.Jobs;
using Xunit;

namespace WindowsServiceStarter.Tests;

public class FolderCleanupJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static FolderCleanupJob CreateJob(FolderCleanupOptions options) =>
        new(new TestOptionsMonitor<FolderCleanupOptions>(options), NullLogger<FolderCleanupJob>.Instance, new FixedTimeProvider(Now));

    [Fact]
    public async Task Dry_run_deletes_nothing()
    {
        using var temp = new TempFolder();
        var old = temp.File("a.tmp", Now.UtcDateTime.AddDays(-60));

        await CreateJob(new FolderCleanupOptions { Path = temp.Path, SearchPattern = "*.tmp", OlderThanDays = 30, DryRun = true })
            .RunAsync(CancellationToken.None);

        Assert.True(File.Exists(old));
    }

    [Fact]
    public async Task Deletes_only_old_matching_files()
    {
        using var temp = new TempFolder();
        var oldTmp = temp.File("old.tmp", Now.UtcDateTime.AddDays(-60));
        var newTmp = temp.File("new.tmp", Now.UtcDateTime.AddDays(-5));
        var oldTxt = temp.File("old.txt", Now.UtcDateTime.AddDays(-60));

        await CreateJob(new FolderCleanupOptions { Path = temp.Path, SearchPattern = "*.tmp", OlderThanDays = 30, DryRun = false })
            .RunAsync(CancellationToken.None);

        Assert.False(File.Exists(oldTmp));
        Assert.True(File.Exists(newTmp));
        Assert.True(File.Exists(oldTxt));
    }

    [Fact]
    public async Task Empty_or_missing_path_is_a_no_op()
    {
        await CreateJob(new FolderCleanupOptions { Path = "" }).RunAsync(CancellationToken.None);
        await CreateJob(new FolderCleanupOptions { Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) })
            .RunAsync(CancellationToken.None);
    }
}
