namespace WindowsServiceStarter.Logging;

/// <summary>
/// Bound from "Logging:File". Log levels for this provider are set the standard way,
/// under "Logging:File:LogLevel".
/// </summary>
public sealed class FileLoggerOptions
{
    /// <summary>Log folder. Relative paths are resolved against the exe folder.</summary>
    public string Directory { get; set; } = "logs";

    /// <summary>Files are named {prefix}-yyyyMMdd.log, then {prefix}-yyyyMMdd_1.log and so on.</summary>
    public string FileNamePrefix { get; set; } = "service";

    /// <summary>Start a new file for the same day once the current one reaches this size.</summary>
    public int MaxFileSizeMB { get; set; } = 10;

    /// <summary>Log files older than this are deleted. 0 keeps everything.</summary>
    public int RetainDays { get; set; } = 14;
}
