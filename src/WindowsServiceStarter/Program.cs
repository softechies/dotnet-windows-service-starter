using WindowsServiceStarter;
using WindowsServiceStarter.Jobs;
using WindowsServiceStarter.Logging;

// Services start with the working directory set to C:\Windows\System32,
// so pin the content root to the exe folder or appsettings.json won't be found.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// When started by the Service Control Manager this runs as a Windows Service.
// When started from a terminal it runs as a normal console app (Ctrl+C to stop).
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = builder.Configuration["Service:Name"] ?? "WindowsServiceStarter";
});

builder.Logging.AddFileLogger(builder.Configuration.GetSection("Logging:File"));

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<FolderCleanupOptions>(builder.Configuration.GetSection(FolderCleanupOptions.SectionName));

// Register your jobs here. Each one is run by the Worker on its own schedule.
builder.Services.AddSingleton<IJob, HeartbeatJob>();
builder.Services.AddSingleton<IJob, FolderCleanupJob>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
