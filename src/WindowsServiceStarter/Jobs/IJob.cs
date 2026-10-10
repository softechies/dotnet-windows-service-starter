namespace WindowsServiceStarter.Jobs;

/// <summary>
/// A unit of recurring work. Implement this, register it in Program.cs,
/// and add a matching entry under Worker:Jobs in appsettings.json.
/// </summary>
public interface IJob
{
    /// <summary>Name used in logs and as the key under Worker:Jobs.</summary>
    string Name { get; }

    /// <summary>
    /// Do one run of the job. Honour the token: it is cancelled on shutdown
    /// and when the run exceeds its configured timeout.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);
}
