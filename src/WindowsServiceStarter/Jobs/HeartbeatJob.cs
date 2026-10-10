using System.Diagnostics;

namespace WindowsServiceStarter.Jobs;

/// <summary>
/// Logs uptime and memory use. Handy to confirm the service is alive
/// and to spot memory growth over days of running.
/// </summary>
public sealed class HeartbeatJob(ILogger<HeartbeatJob> logger) : IJob
{
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    public string Name => "Heartbeat";

    public Task RunAsync(CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - _startedUtc;

        logger.LogInformation(
            "Heartbeat: uptime {Uptime:d\\.hh\\:mm\\:ss}, working set {WorkingSetMb:F1} MB, managed heap {HeapMb:F1} MB, threads {Threads}",
            uptime,
            process.WorkingSet64 / 1024d / 1024d,
            GC.GetTotalMemory(false) / 1024d / 1024d,
            process.Threads.Count);

        return Task.CompletedTask;
    }
}
