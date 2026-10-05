using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Compact;

namespace BookingPlatform.Infrastructure.Logging;

public static class SerilogConsole
{
    public const string FormatKey = "LogFormat";

    private const string TextTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {CorrelationId}{NewLine}{Exception}";

    /// <summary>
    /// Writes compact JSON to the console (for log aggregation) unless <c>LogFormat</c> is <c>text</c>, which is used in development.
    /// </summary>
    public static LoggerConfiguration WritePlatformConsole(this LoggerConfiguration logger, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        return string.Equals(configuration[FormatKey], "text", StringComparison.OrdinalIgnoreCase)
            ? logger.WriteTo.Console(outputTemplate: TextTemplate)
            : logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
}
