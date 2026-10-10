using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WindowsServiceStarter.Logging;

public static class FileLoggerExtensions
{
    /// <summary>Adds the rolling file logger, configured from the given section (usually Logging:File).</summary>
    public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder, IConfiguration section)
    {
        builder.Services.Configure<FileLoggerOptions>(section);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>());
        return builder;
    }
}
