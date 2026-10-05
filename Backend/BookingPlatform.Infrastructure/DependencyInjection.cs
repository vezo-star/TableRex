using BookingPlatform.Application.Availability;
using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Idempotency;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Infrastructure.Bookings;
using BookingPlatform.Infrastructure.Availability;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.RateLimiting;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Identity;
using BookingPlatform.Infrastructure.Auditing;
using BookingPlatform.Infrastructure.Configuration;
using BookingPlatform.Infrastructure.HealthChecks;
using BookingPlatform.Infrastructure.Identity;
using BookingPlatform.Infrastructure.Persistence;
using BookingPlatform.Infrastructure.RateLimiting;
using BookingPlatform.Infrastructure.Security;
using BookingPlatform.Infrastructure.Tenancy;
using BookingPlatform.Application.Tenancy;
using BookingPlatform.Application.Venues;
using BookingPlatform.Infrastructure.Venues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace BookingPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        DapperConfiguration.Configure();

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

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<DbSession>();
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<DbSession>());
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.KeyPrefix), "RateLimiting:KeyPrefix must not be empty.");
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();

        services.AddMemoryCache();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddSingleton<ITenantAccessCache, TenantAccessCache>();

        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IVenueAccessRepository, VenueAccessRepository>();
        services.AddScoped<IResourceRepository, ResourceRepository>();
        services.AddScoped<IFloorPlanRepository, FloorPlanRepository>();
        services.AddScoped<IResourceCombinationRepository, ResourceCombinationRepository>();
        services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();

        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IBookingCustomerRepository, BookingCustomerRepository>();
        services.AddScoped<IAllocatableResourceRepository, AllocatableResourceRepository>();
        services.AddScoped<IAllocationRepository, AllocationRepository>();
        services.AddScoped<IHoldRepository, HoldRepository>();
        services.AddScoped<IBlockRepository, BlockRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        return services;
    }

    /// <summary>Token issuing for hosts that authenticate users (the API). Fails fast on a missing or weak signing key.</summary>
    public static IServiceCollection AddTokenIssuing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(JwtOptions.IsValid,
                $"Jwt:Issuer and Jwt:Audience are required, Jwt:SigningKey must be at least {JwtOptions.MinimumKeyBytes} bytes and not a placeholder, and Jwt:AccessTokenMinutes must be 1-60.")
            .ValidateOnStart();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(options => options.RefreshTokenDays >= 1 && options.RefreshTokenAbsoluteDays >= options.RefreshTokenDays
                && options.LockoutThreshold >= 1 && options.LockoutBaseMinutes >= 1 && options.LockoutMaxMinutes >= options.LockoutBaseMinutes,
                "Auth options are invalid.")
            .ValidateOnStart();

        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        return services;
    }
}
