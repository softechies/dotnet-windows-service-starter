using System.Text;

namespace WindowsServiceStarter.Logging;

internal sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // Level filtering is done by the logging framework using Logging:File:LogLevel.
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        var sb = new StringBuilder(128);
        sb.Append(provider.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
        sb.Append(" [").Append(Abbreviate(logLevel)).Append("] ");
        sb.Append(category).Append(": ");
        sb.AppendLine(message);
        if (exception is not null)
        {
            sb.AppendLine(exception.ToString());
        }

        provider.Enqueue(sb.ToString());
    }

    internal static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };
}
