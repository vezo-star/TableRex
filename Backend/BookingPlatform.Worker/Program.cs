using BookingPlatform.Infrastructure;
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
builder.Services.AddHostedService<WorkerLifetimeLogger>();

using var host = builder.Build();
await host.RunAsync();
