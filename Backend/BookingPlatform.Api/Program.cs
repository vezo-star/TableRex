using BookingPlatform.Api.Configuration;
using BookingPlatform.Api.Errors;
using BookingPlatform.Api.Health;
using BookingPlatform.Api.Middleware;
using BookingPlatform.Api.Realtime;
using BookingPlatform.Api.Security;
using BookingPlatform.Application;
using BookingPlatform.Application.Common.Idempotency;
using BookingPlatform.Infrastructure;
using BookingPlatform.Infrastructure.Logging;
using BookingPlatform.Migrations;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
});

builder.Services.AddSerilog(
    (services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WritePlatformConsole(builder.Configuration),
    preserveStaticLogger: true);

builder.Services.AddOptions<CorsSettings>()
    .Bind(builder.Configuration.GetSection(CorsSettings.SectionName))
    .Validate(CorsSettings.AreValid, "Cors:AllowedOrigins must contain absolute http(s) origins without paths or trailing slashes.")
    .ValidateOnStart();
builder.Services.AddCors();
builder.Services.AddSingleton<IConfigureOptions<CorsOptions>, ConfigureCorsPolicy>();

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Customize);
builder.Services.AddExceptionHandler<AppExceptionHandler>();

builder.Services
    .AddControllers(options => options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.MaxDepth = 32;
        options.JsonSerializerOptions.Converters.Add(new TimeOnlyJsonConverter());
    });
builder.Services.AddPlatformSwagger();

builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
builder.Services.AddPlatformForwardedHeaders(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTokenIssuing(builder.Configuration);
builder.Services.AddPublicBookingTokens(builder.Configuration);
builder.Services.AddPayments(builder.Configuration, builder.Environment);
builder.Services.AddApplication();
builder.Services.AddSingleton<IResponseSerializer, ApiResponseSerializer>();
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddPlatformHealthChecks();
builder.Services.AddPlatformRealtime(builder.Configuration);
builder.Services.AddSingleton<MigrationRunner>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    // The static Serilog logger is intentionally not used (preserveStaticLogger), so pass the host's logger explicitly.
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.GetLevel = (httpContext, _, exception) =>
        httpContext.Request.Path.StartsWithSegments(HealthEndpoints.LivenessPath) ? LogEventLevel.Verbose
        : exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error
        : LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
{
    // TLS terminates at the reverse proxy; HSTS instructs browsers to keep using HTTPS.
    app.UseHsts();
}

if (SwaggerConfiguration.IsEnabled(app.Environment))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint($"/swagger/{SwaggerConfiguration.DocumentName}/swagger.json", "Booking Platform API v1"));
}

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();

app.MapPlatformHealthChecks();
app.MapControllers();
app.MapPlatformRealtime();

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    var connectionString = app.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required for Database:AutoMigrate.");
    var result = app.Services.GetRequiredService<MigrationRunner>().Run(connectionString);
    if (!result.Successful)
    {
        throw new InvalidOperationException("Database migration failed; see the log for details.", result.Error);
    }
}

await app.RunAsync();

public partial class Program;
