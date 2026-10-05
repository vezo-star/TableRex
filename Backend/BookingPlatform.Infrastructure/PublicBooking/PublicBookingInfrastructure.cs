using System.Security.Cryptography;
using System.Text;
using BookingPlatform.Application.PublicBooking;
using BookingPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Infrastructure.PublicBooking;

public sealed class PublicDirectory(DbSession db) : IPublicDirectory
{
    private const string Select = """
        SELECT t.id AS tenant_id, t.slug AS tenant_slug, t.name AS tenant_name, t.status AS tenant_status, v.id AS venue_id, v.slug AS venue_slug
        FROM tenants t
        JOIN venues v ON v.tenant_id = t.id
        """;

    public async Task<PublicVenueLocator?> FindAsync(string tenantSlug, string venueSlug, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<Row>($"{Select} WHERE t.slug = @tenantSlug AND v.slug = @venueSlug",
            new { tenantSlug, venueSlug }, cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<PublicVenueLocator?> GetAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<Row>($"{Select} WHERE t.id = @tenantId AND v.id = @venueId",
            new { tenantId, venueId }, cancellationToken);
        return row is null ? null : Map(row);
    }

    private static PublicVenueLocator Map(Row r) => new(r.TenantId, r.TenantSlug, r.TenantName, r.TenantStatus, r.VenueId, r.VenueSlug);

    private sealed class Row
    {
        public Guid TenantId { get; init; }
        public string TenantSlug { get; init; } = string.Empty;
        public string TenantName { get; init; } = string.Empty;
        public string TenantStatus { get; init; } = string.Empty;
        public Guid VenueId { get; init; }
        public string VenueSlug { get; init; } = string.Empty;
    }
}

/// <summary>Token = base64url(HMAC-SHA256(key, "booking:" + id)). Deterministic per booking, unguessable without the key.</summary>
public sealed class PublicBookingTokens(IOptions<PublicBookingOptions> options) : IPublicBookingTokens
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.TokenKey);

    public string Create(Guid bookingId)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"booking:{bookingId:N}"));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
