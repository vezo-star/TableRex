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
if (command is not ("migrate" or "seed-dev" or "bootstrap-admin"))
{
    Console.Error.WriteLine("""
        Usage: BookingPlatform.Migrations <command>

        Commands:
          migrate          Apply pending migrations from database/migrations to ConnectionStrings:Postgres.
          seed-dev         Apply migrations, then create idempotent demo data (refused when DOTNET_ENVIRONMENT=Production).
                           Requires DEV_ADMIN_PASSWORD (min 12 characters); DEV_ADMIN_EMAIL defaults to admin@demo.local.
          bootstrap-admin  Apply migrations, then create the platform admin PLATFORM_ADMIN_EMAIL with PLATFORM_ADMIN_PASSWORD
                           (PLATFORM_ADMIN_NAME optional), or promote that existing account without changing its password.
                           Allowed in every environment; idempotent.

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
builder.Services.AddSingleton<DevSeeder>();
builder.Services.AddSingleton<PlatformAdminBootstrapper>();

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILogger<MigrationRunner>>();

if (command == "seed-dev" && builder.Environment.IsProduction())
{
    log.LogError("seed-dev is refused in the Production environment.");
    return ExitFailure;
}

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    log.LogError("ConnectionStrings:Postgres is not configured.");
    return ExitFailure;
}

DevSeedOptions? seedOptions = null;
if (command == "seed-dev")
{
    var password = builder.Configuration["DEV_ADMIN_PASSWORD"];
    var email = builder.Configuration["DEV_ADMIN_EMAIL"] is { Length: > 0 } configured ? configured.Trim() : "admin@demo.local";
    if (string.IsNullOrEmpty(password) || password.Length < 12)
    {
        log.LogError("DEV_ADMIN_PASSWORD must be set to at least 12 characters.");
        return ExitFailure;
    }

    if (email.Count(c => c == '@') != 1 || email.StartsWith('@') || email.EndsWith('@'))
    {
        log.LogError("DEV_ADMIN_EMAIL is not a valid email address.");
        return ExitFailure;
    }

    seedOptions = new DevSeedOptions(email, password);
}

PlatformAdminOptions? adminOptions = null;
if (command == "bootstrap-admin")
{
    adminOptions = new PlatformAdminOptions(
        builder.Configuration["PLATFORM_ADMIN_EMAIL"]?.Trim() ?? string.Empty,
        builder.Configuration["PLATFORM_ADMIN_PASSWORD"] ?? string.Empty,
        builder.Configuration["PLATFORM_ADMIN_NAME"] is { Length: > 0 } name ? name : "Platform Admin");
    if (PlatformAdminBootstrapper.Validate(adminOptions) is { } error)
    {
        log.LogError("{Error}", error);
        return ExitFailure;
    }
}

log.LogInformation("Applying migrations ({Environment})", builder.Environment.EnvironmentName);
var result = host.Services.GetRequiredService<MigrationRunner>().Run(connectionString);
if (!result.Successful)
{
    return ExitFailure;
}

if (seedOptions is not null)
{
    try
    {
        await host.Services.GetRequiredService<DevSeeder>().SeedAsync(connectionString, seedOptions, CancellationToken.None);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "seed-dev failed");
        return ExitFailure;
    }
}

if (adminOptions is not null)
{
    try
    {
        await host.Services.GetRequiredService<PlatformAdminBootstrapper>().RunAsync(connectionString, adminOptions, CancellationToken.None);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "bootstrap-admin failed");
        return ExitFailure;
    }
}

return ExitSuccess;
