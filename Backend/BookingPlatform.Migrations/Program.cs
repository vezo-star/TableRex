using BookingPlatform.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

const int ExitSuccess = 0;
const int ExitFailure = 1;
const int ExitUsage = 2;

var command = args.FirstOrDefault();
if (command is not "migrate")
{
    Console.Error.WriteLine("""
        Usage: BookingPlatform.Migrations <command>

        Commands:
          migrate    Apply pending migrations from database/migrations to ConnectionStrings:Postgres.

        Configuration: environment variables (ConnectionStrings__Postgres), command line
        (--ConnectionStrings:Postgres=...) and, when DOTNET_ENVIRONMENT=Development, .NET user-secrets.
        """);
    return ExitUsage;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Skip(1).ToArray(),
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddSerilog(
    (_, logger) =>
    {
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Npgsql", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "BookingPlatform.Migrations");

        if (builder.Environment.IsDevelopment())
        {
            logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        }
        else
        {
            logger.WriteTo.Console(new RenderedCompactJsonFormatter());
        }
    },
    preserveStaticLogger: true);

builder.Services.AddSingleton<MigrationRunner>();

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILogger<MigrationRunner>>();

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    log.LogError("ConnectionStrings:Postgres is not configured.");
    return ExitFailure;
}

log.LogInformation("Applying migrations ({Environment})", builder.Environment.EnvironmentName);
var result = host.Services.GetRequiredService<MigrationRunner>().Run(connectionString);
return result.Successful ? ExitSuccess : ExitFailure;
