namespace WindowsServiceStarter;

/// <summary>
/// Bound from the "Worker" section of appsettings.json.
/// Changes to the file are picked up while the service is running.
/// </summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>
    /// When a single job fails this many times in a row, the process exits with code 1
    /// so the Windows service recovery actions (restart) kick in. 0 disables this.
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 5;

    /// <summary>Per job settings, keyed by <see cref="Jobs.IJob.Name"/>.</summary>
    public Dictionary<string, JobSettings> Jobs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public JobSettings For(string jobName)
    {
        // The config binder may swap in its own dictionary, so don't rely on the comparer.
        foreach (var (name, settings) in Jobs)
        {
            if (string.Equals(name, jobName, StringComparison.OrdinalIgnoreCase))
            {
                return settings;
            }
        }
        return JobSettings.Default;
    }
}

public sealed class JobSettings
{
    public static readonly JobSettings Default = new();

    public bool Enabled { get; set; } = true;

    /// <summary>Time between the start of two runs. Format: hh:mm:ss</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>A run that takes longer than this is cancelled and counted as a failure.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Run once immediately at startup instead of waiting one interval.</summary>
    public bool RunOnStart { get; set; } = true;
}
