using BookingPlatform.Application.Availability;
using BookingPlatform.Application.Identity;
using BookingPlatform.Application.Tenancy;
using BookingPlatform.Application.Venues;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BookingPlatform.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        AddValidators(services);

        services.AddScoped<AuthService>();
        services.AddScoped<TenantService>();
        services.AddScoped<StaffService>();
        services.AddScoped<IVenueAccessGuard, VenueAccessGuard>();
        services.AddScoped<VenueService>();
        services.AddScoped<ResourceService>();
        services.AddScoped<FloorPlanService>();
        services.AddScoped<ResourceCombinationService>();
        services.AddScoped<AvailabilityService>();

        return services;
    }

    private static void AddValidators(IServiceCollection services)
    {
        var validatorTypes = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false, IsPublic: true });

        foreach (var type in validatorTypes)
        {
            foreach (var validatorInterface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddSingleton(validatorInterface, type);
            }
        }
    }
}
