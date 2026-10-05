using BookingPlatform.Infrastructure.Configuration;
using BookingPlatform.Infrastructure.HealthChecks;
using BookingPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace BookingPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ConnectionStringsOptions>()
            .Bind(configuration.GetSection(ConnectionStringsOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Postgres), "ConnectionStrings:Postgres is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Redis), "ConnectionStrings:Redis is required.")
            .ValidateOnStart();

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value;
            var builder = new NpgsqlDataSourceBuilder(options.Postgres);
            PostgresConnectionDefaults.Apply(builder.ConnectionStringBuilder, options.Postgres);
            return builder.Build();
        });

        // Redis is never authoritative: the multiplexer must not abort when Redis is down so the API keeps serving bookings.
        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value;
            var redisOptions = ConfigurationOptions.Parse(options.Redis);
            redisOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisOptions);
        });

        services.AddSingleton<PostgresHealthCheck>();
        services.AddSingleton<RedisHealthCheck>();

        return services;
    }
}
