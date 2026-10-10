# .NET Windows Service Starter

A production ready template for building Windows Services in .NET 8.
Clone it, drop in your own job, run one script, and you have a service that
logs to rolling files, restarts itself after a crash, and picks up config
changes without a restart.

[![build](https://github.com/softechies/dotnet-windows-service-starter/actions/workflows/build.yml/badge.svg)](https://github.com/softechies/dotnet-windows-service-starter/actions/workflows/build.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

## Why this exists

The default `dotnet new worker` template gets you a loop that writes to the console.
Real services need a lot more before they can be left running on a client server:

| Problem | What this starter does |
|---|---|
| Service can't find `appsettings.json` (working dir is `System32`) | Content root pinned to the exe folder |
| No logs on disk | Built in rolling file logger: daily files, size limit, retention, no extra packages |
| Service crashes and stays stopped | Exits with code 1 on fatal errors and installs recovery actions (restart 5s, 30s, 60s) |
| One stuck task blocks everything | Each job runs in its own loop with a per run timeout |
| Changing an interval needs a redeploy | Settings re-read before every run, edit `appsettings.json` and it applies |
| Install steps live in someone's head | `publish.ps1`, `install-service.ps1`, `uninstall-service.ps1` |

## Quick start

```powershell
git clone https://github.com/softechies/dotnet-windows-service-starter.git
cd dotnet-windows-service-starter

# Run as a console app while developing (Ctrl+C to stop)
dotnet run --project src/WindowsServiceStarter

# Publish and install as a real service (elevated PowerShell)
.\scripts\publish.ps1
.\scripts\install-service.ps1

# Remove it again
.\scripts\uninstall-service.ps1
```

Logs are written to `publish\logs\service-yyyyMMdd.log`.

## Adding your own job

1. Implement `IJob`:

```csharp
public sealed class InvoiceSyncJob(IHttpClientFactory http, ILogger<InvoiceSyncJob> logger) : IJob
{
    public string Name => "InvoiceSync";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Pass the token to every async call so timeouts and shutdown work.
        var client = http.CreateClient();
        var invoices = await client.GetStringAsync("https://example.com/api/invoices", cancellationToken);
        logger.LogInformation("Fetched {Length} bytes of invoices", invoices.Length);
    }
}
```

2. Register it in `Program.cs`:

```csharp
builder.Services.AddSingleton<IJob, InvoiceSyncJob>();
```

3. Give it a schedule in `appsettings.json`:

```json
"Worker": {
  "Jobs": {
    "InvoiceSync": { "Enabled": true, "Interval": "00:15:00", "Timeout": "00:02:00" }
  }
}
```

Jobs without an entry use the defaults: enabled, every minute, 5 minute timeout.

## Configuration

| Setting | Default | Notes |
|---|---|---|
| `Service:Name` | `WindowsServiceStarter` | Name reported to the Service Control Manager |
| `Worker:MaxConsecutiveFailures` | `5` | After this many failed runs in a row the process exits with code 1 so Windows restarts it. `0` disables |
| `Worker:Jobs:<Name>:Enabled` | `true` | Toggle a job at runtime |
| `Worker:Jobs:<Name>:Interval` | `00:01:00` | Time between run starts. A run that takes 10s on a 60s interval waits 50s |
| `Worker:Jobs:<Name>:Timeout` | `00:05:00` | The run's token is cancelled after this and it counts as a failure |
| `Worker:Jobs:<Name>:RunOnStart` | `true` | Run immediately at startup instead of waiting one interval |
| `Logging:File:Directory` | `logs` | Relative to the exe folder |
| `Logging:File:MaxFileSizeMB` | `10` | Rolls to `service-yyyyMMdd_1.log`, `_2`, ... |
| `Logging:File:RetainDays` | `14` | Older log files are deleted |
| `Logging:File:LogLevel:Default` | `Information` | Standard .NET log level filtering |

Warnings and errors also go to the Windows Event Log (Application log) when running as a service.

## Included jobs

* **HeartbeatJob** logs uptime, working set and heap size. Useful to prove the service is alive and to spot slow memory leaks.
* **FolderCleanupJob** deletes files older than N days from a folder (temp uploads, exports, old reports). Ships with `DryRun: true` so it only logs what it would delete until you switch it off.

## Project layout

```
src/WindowsServiceStarter/
  Program.cs                host setup and job registration
  Worker.cs                 runs each job on its own schedule
  WorkerOptions.cs          strongly typed settings
  Jobs/                     IJob plus the two sample jobs
  Logging/                  rolling file logger
tests/WindowsServiceStarter.Tests/
scripts/                    publish, install, uninstall
```

## Running the tests

```bash
dotnet test
```

The tests cover job scheduling, timeouts, the failure and exit behaviour, file log rollover and retention, and the cleanup job.

## Recovery behaviour, in detail

`install-service.ps1` runs:

```
sc.exe failure <name> reset= 86400 actions= restart/5000/restart/30000/restart/60000
sc.exe failureflag <name> 1
```

The second line matters: without it Windows only restarts the service after a hard crash,
not when the process exits cleanly with a non zero code. The worker deliberately calls
`Environment.Exit(1)` on a fatal error so this path is used.

## License

MIT

---

Built and maintained by [Softechies Solutions](https://softechies.net), a software house
specialising in .NET, Windows services, and desktop and system level development.
Need a Windows service, driver or desktop tool built? Get in touch.
