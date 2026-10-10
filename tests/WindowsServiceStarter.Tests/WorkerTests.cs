using Microsoft.Extensions.Logging.Abstractions;
using WindowsServiceStarter.Jobs;
using Xunit;

namespace WindowsServiceStarter.Tests;

public class WorkerTests
{
    private sealed class CountingJob(string name, Func<int, CancellationToken, Task>? body = null) : IJob
    {
        public int Runs;
        public string Name => name;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            var run = Interlocked.Increment(ref Runs);
            return body?.Invoke(run, cancellationToken) ?? Task.CompletedTask;
        }
    }

    private static WorkerOptions Options(string job, TimeSpan interval, TimeSpan? timeout = null, bool enabled = true, int maxFailures = 3) => new()
    {
        MaxConsecutiveFailures = maxFailures,
        Jobs =
        {
            [job] = new JobSettings
            {
                Enabled = enabled,
                Interval = interval,
                Timeout = timeout ?? TimeSpan.FromSeconds(5)
            }
        }
    };

    private static Worker CreateWorker(IJob job, WorkerOptions options, Action<int>? exit = null) =>
        new([job], new TestOptionsMonitor<WorkerOptions>(options), NullLogger<Worker>.Instance)
        {
            Exit = exit ?? (_ => { })
        };

    [Fact]
    public async Task Runs_job_repeatedly_on_interval()
    {
        var job = new CountingJob("A");
        using var worker = CreateWorker(job, Options("A", TimeSpan.FromMilliseconds(50)));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(400);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(job.Runs >= 3, $"expected at least 3 runs, got {job.Runs}");
    }

    [Fact]
    public async Task Disabled_job_does_not_run()
    {
        var job = new CountingJob("A");
        using var worker = CreateWorker(job, Options("A", TimeSpan.FromMilliseconds(20), enabled: false));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, job.Runs);
    }

    [Fact]
    public async Task Repeated_failures_exit_with_code_1()
    {
        var exitCode = -1;
        var exited = new TaskCompletionSource();
        var job = new CountingJob("A", (_, _) => throw new InvalidOperationException("boom"));
        using var worker = CreateWorker(job, Options("A", TimeSpan.FromMilliseconds(10), maxFailures: 3),
            code => { exitCode = code; exited.TrySetResult(); });

        await worker.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(exited.Task, Task.Delay(3000));
        await worker.StopAsync(CancellationToken.None);

        Assert.Same(exited.Task, finished);
        Assert.Equal(1, exitCode);
        Assert.Equal(3, job.Runs);
    }

    [Fact]
    public async Task Success_resets_failure_count()
    {
        var exited = false;
        // Fails twice, succeeds once, forever. Never reaches 3 in a row.
        var job = new CountingJob("A", (run, _) => run % 3 == 0 ? Task.CompletedTask : throw new Exception("fail"));
        using var worker = CreateWorker(job, Options("A", TimeSpan.FromMilliseconds(10), maxFailures: 3), _ => exited = true);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(400);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(exited);
        Assert.True(job.Runs >= 6);
    }

    [Fact]
    public async Task Slow_run_is_cancelled_by_timeout()
    {
        var sawCancellation = false;
        var job = new CountingJob("A", async (_, ct) =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
            catch (OperationCanceledException) { sawCancellation = true; throw; }
        });
        using var worker = CreateWorker(job, Options("A", TimeSpan.FromSeconds(10), timeout: TimeSpan.FromMilliseconds(50)));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(sawCancellation);
    }

    [Fact]
    public void Job_settings_lookup_ignores_case()
    {
        var options = Options("Heartbeat", TimeSpan.FromSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(7), options.For("heartbeat").Interval);
        Assert.Same(JobSettings.Default, options.For("missing"));
    }
}
