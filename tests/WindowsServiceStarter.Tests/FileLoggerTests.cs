using Microsoft.Extensions.Logging;
using WindowsServiceStarter.Logging;
using Xunit;

namespace WindowsServiceStarter.Tests;

public class FileLoggerTests
{
    private static readonly DateTimeOffset Today = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Writes_formatted_lines_to_dated_file()
    {
        using var temp = new TempFolder();
        var time = new FixedTimeProvider(Today);

        using (var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = temp.Path, FileNamePrefix = "app" }, time))
        {
            var logger = provider.CreateLogger("Orders");
            logger.LogInformation("Order {Id} saved", 42);
            logger.LogError(new InvalidOperationException("bad state"), "Something failed");
        }

        var text = File.ReadAllText(Path.Combine(temp.Path, "app-20260315.log"));
        Assert.Contains("[INF] Orders: Order 42 saved", text);
        Assert.Contains("[ERR] Orders: Something failed", text);
        Assert.Contains("InvalidOperationException: bad state", text);
    }

    [Fact]
    public void Starts_new_file_when_size_limit_is_reached()
    {
        using var temp = new TempFolder();
        var time = new FixedTimeProvider(Today);

        // Pre fill today's file past the 1 MB limit.
        File.WriteAllText(Path.Combine(temp.Path, "app-20260315.log"), new string('x', 1024 * 1024 + 10));

        using (var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = temp.Path, FileNamePrefix = "app", MaxFileSizeMB = 1 }, time))
        {
            provider.CreateLogger("Test").LogInformation("after rollover");
        }

        var rolled = Path.Combine(temp.Path, "app-20260315_1.log");
        Assert.True(File.Exists(rolled));
        Assert.Contains("after rollover", File.ReadAllText(rolled));
    }

    [Fact]
    public void Starts_new_file_on_new_day()
    {
        using var temp = new TempFolder();
        var time = new FixedTimeProvider(Today);

        using (var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = temp.Path, FileNamePrefix = "app" }, time))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogInformation("day one");
            Thread.Sleep(200); // let the writer drain before moving the clock
            time.Now = Today.AddDays(1);
            logger.LogInformation("day two");
        }

        Assert.Contains("day one", File.ReadAllText(Path.Combine(temp.Path, "app-20260315.log")));
        Assert.Contains("day two", File.ReadAllText(Path.Combine(temp.Path, "app-20260316.log")));
    }

    [Fact]
    public void Deletes_files_older_than_retention()
    {
        using var temp = new TempFolder();
        var time = new FixedTimeProvider(Today);
        var old = temp.File("app-20260101.log", Today.UtcDateTime.AddDays(-30));
        var recent = temp.File("app-20260314.log", Today.UtcDateTime.AddDays(-1));
        var other = temp.File("other-20260101.log", Today.UtcDateTime.AddDays(-30));

        using (var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = temp.Path, FileNamePrefix = "app", RetainDays = 14 }, time))
        {
            provider.CreateLogger("Test").LogInformation("trigger open");
        }

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other)); // different prefix, not ours
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void Abbreviates_levels(LogLevel level, string expected)
    {
        Assert.Equal(expected, FileLogger.Abbreviate(level));
    }
}
