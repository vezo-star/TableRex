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
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token from POST /api/v1/auth/login.",
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            });
            options.CustomSchemaIds(type => type.FullName?.Replace("+", ".", StringComparison.Ordinal));
        });

        return services;
    }

    public static bool IsEnabled(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Test");
}
