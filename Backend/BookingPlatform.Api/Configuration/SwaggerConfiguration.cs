using Microsoft.OpenApi;

namespace BookingPlatform.Api.Configuration;

public static class SwaggerConfiguration
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddPlatformSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "Booking Platform API",
                Version = DocumentName,
                Description = "Multi-tenant booking and reservation platform. Errors use RFC 7807 ProblemDetails with a stable `code` and `traceId`.",
            });
        });

        return services;
    }

    public static bool IsEnabled(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Test");
}
