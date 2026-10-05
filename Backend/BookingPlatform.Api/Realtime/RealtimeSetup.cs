using BookingPlatform.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BookingPlatform.Api.Realtime;

public static class RealtimeSetup
{
    public static IServiceCollection AddPlatformRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RealtimeOptions>()
            .Bind(configuration.GetSection(RealtimeOptions.SectionName))
            .Validate(options => options.IsValid(), "Realtime options are out of range.")
            .ValidateOnStart();

        services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = false;
                options.MaximumReceiveMessageSize = 4 * 1024;
                options.AddFilter<HubErrorFilter>();
            })
            .AddStackExchangeRedis();

        // Bound lazily so test hosts and environment overrides supply the connection string.
        services.AddOptions<RedisOptions>()
            .Configure<IOptions<ConnectionStringsOptions>, IOptions<RealtimeOptions>, IHostEnvironment>((redis, connectionStrings, realtime, environment) =>
            {
                var configurationOptions = ConfigurationOptions.Parse(connectionStrings.Value.Redis);
                // Redis is not authoritative: without it live updates pause but the Api keeps serving bookings.
                configurationOptions.AbortOnConnectFail = false;
                configurationOptions.ChannelPrefix = RedisChannel.Literal(realtime.Value.ChannelPrefix ?? $"bp-{environment.EnvironmentName.ToLowerInvariant()}");
                redis.Configuration = configurationOptions;
            });

        if (configuration.GetValue(RealtimeOptions.SectionName + ":" + nameof(RealtimeOptions.DispatcherEnabled), true))
        {
            services.AddHostedService<RealtimeOutboxDispatcher>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapPlatformRealtime(this IEndpointRouteBuilder endpoints)
    {
        // WebSockets only, so instances behind a load balancer need no sticky sessions (REALTIME.md §5).
        endpoints.MapHub<VenueHub>(VenueHub.Path, options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.CloseOnAuthenticationExpiration = true;
        });
        return endpoints;
    }
}
