using BookingPlatform.Application.Identity;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Identity;

public sealed class RefreshTokenRepository(DbSession db) : IRefreshTokenRepository
{
    public Task<Guid> InsertAsync(NewRefreshToken token, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO refresh_tokens (user_id, tenant_id, token_hash, family_id, expires_at, family_expires_at, created_ip, user_agent)
            VALUES (@UserId, @TenantId, @TokenHash, @FamilyId, @ExpiresAt, @FamilyExpiresAt, @CreatedIp, @UserAgent)
            RETURNING id
            """,
            token,
            cancellationToken);

    public Task<RefreshTokenRecord?> GetByHashForUpdateAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<RefreshTokenRecord>(
            """
            SELECT id, user_id, tenant_id, family_id, expires_at, family_expires_at, revoked_at, replaced_by_id
            FROM refresh_tokens
            WHERE token_hash = @tokenHash
            FOR UPDATE
            """,
            new { tokenHash },
            cancellationToken);

    public Task MarkRotatedAsync(Guid id, Guid replacedById, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE refresh_tokens
            SET revoked_at = @now, revoked_reason = @reason, replaced_by_id = @replacedById
            WHERE id = @id AND revoked_at IS NULL
            """,
            new { id, replacedById, now, reason = RevocationReasons.Rotated },
            cancellationToken);

    public Task RevokeFamilyAsync(Guid familyId, string reason, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @now, revoked_reason = @reason WHERE family_id = @familyId AND revoked_at IS NULL",
            new { familyId, reason, now },
            cancellationToken);

    public Task RevokeAllForUserAsync(Guid userId, string reason, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = @now, revoked_reason = @reason WHERE user_id = @userId AND revoked_at IS NULL",
            new { userId, reason, now },
            cancellationToken);

    public Task RevokeAllForUserInTenantAsync(Guid userId, Guid tenantId, string reason, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE refresh_tokens SET revoked_at = @now, revoked_reason = @reason
            WHERE user_id = @userId AND tenant_id = @tenantId AND revoked_at IS NULL
            """,
            new { userId, tenantId, reason, now },
            cancellationToken);

    public Task<int> PurgeExpiredAsync(DateTime olderThan, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            DELETE FROM refresh_tokens
            WHERE id IN (SELECT id FROM refresh_tokens WHERE family_expires_at < @olderThan ORDER BY id LIMIT 5000)
            """,
            new { olderThan },
            cancellationToken);
}
