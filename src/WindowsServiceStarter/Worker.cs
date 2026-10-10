using System.Diagnostics;
using Microsoft.Extensions.Options;
using WindowsServiceStarter.Jobs;

namespace WindowsServiceStarter;

/// <summary>
/// Runs every registered <see cref="IJob"/> in its own loop, using the interval,
/// timeout and enabled flag from configuration. Settings are re-read before
/// every run, so editing appsettings.json takes effect without a restart.
/// </summary>
public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IJob> _jobs;
    private readonly IOptionsMonitor<WorkerOptions> _options;
    private readonly ILogger<Worker> _logger;
    private readonly IDisposable? _changeSubscription;

    /// <summary>Called on a fatal error. Swapped out in tests.</summary>
    internal Action<int> Exit { get; set; } = Environment.Exit;

    public Worker(IEnumerable<IJob> jobs, IOptionsMonitor<WorkerOptions> options, ILogger<Worker> logger)
    {
        _jobs = jobs.ToList();
        _options = options;
        _logger = logger;
        _changeSubscription = options.OnChange(_ => _logger.LogInformation("Configuration reloaded"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_jobs.Count == 0)
        {
            _logger.LogWarning("No jobs registered, the service will idle");
            return;
        }

        _logger.LogInformation("Service started with {Count} job(s): {Jobs}",
            _jobs.Count, string.Join(", ", _jobs.Select(j => j.Name)));

        try
        {
            await Task.WhenAll(_jobs.Select(job => RunLoopAsync(job, stoppingToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error, exiting with code 1 so service recovery can restart the process");

            // A non zero exit code is what makes the Service Control Manager
            // apply the recovery actions configured by scripts/install-service.ps1.
            Exit(1);
            return;
        }

        _logger.LogInformation("Service stopped");
    }

    private async Task RunLoopAsync(IJob job, CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        var first = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = _options.CurrentValue.For(job.Name);

            if (!settings.Enabled)
            {
                await Task.Delay(DisabledPollInterval, stoppingToken);
                continue;
            }

            if (first && !settings.RunOnStart)
            {
                first = false;
                await Task.Delay(settings.Interval, stoppingToken);
                continue;
            }
            first = false;

            var stopwatch = Stopwatch.StartNew();
            var ok = await RunOnceAsync(job, settings, stoppingToken);
            stopwatch.Stop();

            if (ok)
            {
                consecutiveFailures = 0;
            }
            else
            {
                consecutiveFailures++;
                var max = _options.CurrentValue.MaxConsecutiveFailures;
                if (max > 0 && consecutiveFailures >= max)
                {
                    throw new JobFailedException(job.Name, consecutiveFailures);
                }
            }

            // Keep a steady cadence: a run that took 10s on a 60s interval waits 50s.
            var wait = settings.Interval - stopwatch.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, stoppingToken);
            }
        }
    }

    private async Task<bool> RunOnceAsync(IJob job, JobSettings settings, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(settings.Timeout);

        try
        {
            _logger.LogDebug("Job {Job} starting", job.Name);
            await job.RunAsync(timeout.Token);
            _logger.LogDebug("Job {Job} finished", job.Name);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Job {Job} timed out after {Timeout}", job.Name, settings.Timeout);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Job} failed", job.Name);
            return false;
        }
    }

    public override void Dispose()
    {
        _changeSubscription?.Dispose();
        base.Dispose();
    }
}

public sealed class JobFailedException(string jobName, int failures)
    : Exception($"Job '{jobName}' failed {failures} times in a row")
{
    public string JobName { get; } = jobName;
}
