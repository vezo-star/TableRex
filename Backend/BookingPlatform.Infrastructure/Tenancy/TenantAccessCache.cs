using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Identity;
using BookingPlatform.Application.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace BookingPlatform.Infrastructure.Tenancy;

/// <summary>
/// Caches resolved memberships and user flags for up to 60 s per instance. Changes made on this instance invalidate
/// immediately; other instances converge within the TTL (SECURITY.md §2).
/// </summary>
public sealed class TenantAccessCache(IMemoryCache cache, IServiceScopeFactory scopeFactory) : ITenantAccessCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(10);

    public async Task<TenantAccess?> GetAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var key = AccessKey(userId, tenantId);
        if (cache.TryGetValue(key, out AccessEntry? entry) && entry is not null)
        {
            return entry.Access;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var access = await scope.ServiceProvider.GetRequiredService<IMembershipRepository>()
            .GetTenantAccessAsync(userId, tenantId, cancellationToken);

        cache.Set(key, new AccessEntry(access), access is null ? NegativeTtl : Ttl);
        return access;
    }

    public async Task<UserAccessFlags?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = UserKey(userId);
        if (cache.TryGetValue(key, out UserEntry? entry) && entry is not null)
        {
            return entry.Flags;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var flags = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetAccessFlagsAsync(userId, cancellationToken);
        var result = flags is { } f ? new UserAccessFlags(f.Status, f.IsPlatformAdmin) : null;

        cache.Set(key, new UserEntry(result), result is null ? NegativeTtl : Ttl);
        return result;
    }

    public void Invalidate(Guid userId, Guid tenantId) => cache.Remove(AccessKey(userId, tenantId));

    public void InvalidateUser(Guid userId) => cache.Remove(UserKey(userId));

    private static string AccessKey(Guid userId, Guid tenantId) => $"tenant-access:{userId:N}:{tenantId:N}";

    private static string UserKey(Guid userId) => $"user-flags:{userId:N}";

    private sealed record AccessEntry(TenantAccess? Access);

    private sealed record UserEntry(UserAccessFlags? Flags);
}
