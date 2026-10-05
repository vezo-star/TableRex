using BookingPlatform.Infrastructure;
using BookingPlatform.Infrastructure.Housekeeping;
using BookingPlatform.Infrastructure.Logging;
using BookingPlatform.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddSerilog(
    (services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WritePlatformConsole(builder.Configuration),
    preserveStaticLogger: true);

builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<HousekeepingRepository>();
builder.Services.AddOptions<HousekeepingOptions>()
    .Bind(builder.Configuration.GetSection(HousekeepingOptions.SectionName))
    .Validate(options => options.IsValid(), "Housekeeping options are out of range.")
    .ValidateOnStart();
builder.Services.AddHostedService<WorkerLifetimeLogger>();
builder.Services.AddHostedService<HousekeepingWorker>();

using var host = builder.Build();
await host.RunAsync();
