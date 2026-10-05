using BookingPlatform.Application.Identity;
using BookingPlatform.Application.Notifications;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Identity;

public sealed class PasswordResetRepository(DbSession db) : IPasswordResetRepository
{
    public Task InsertAsync(Guid id, Guid userId, DateTime expiresAt, string? requestedIp, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            INSERT INTO password_reset_requests (id, user_id, expires_at, requested_ip)
            VALUES (@id, @userId, @expiresAt, @requestedIp)
            """,
            new { id, userId, expiresAt, requestedIp },
            cancellationToken);

    public Task InvalidateOutstandingAsync(Guid userId, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE password_reset_requests SET invalidated_at = @now
            WHERE user_id = @userId AND used_at IS NULL AND invalidated_at IS NULL AND expires_at > @now
            """,
            new { userId, now },
            cancellationToken);

    public Task<PasswordResetRequestRecord?> GetByTokenHashForUpdateAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<PasswordResetRequestRecord>(
            """
            SELECT id, user_id, expires_at, used_at, invalidated_at
            FROM password_reset_requests
            WHERE token_hash = @tokenHash
            FOR UPDATE
            """,
            new { tokenHash },
            cancellationToken);

    public Task MarkUsedAsync(Guid id, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE password_reset_requests SET used_at = @now WHERE id = @id AND used_at IS NULL",
            new { id, now },
            cancellationToken);

    public Task<PasswordResetDelivery?> GetForDeliveryAsync(Guid id, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<PasswordResetDelivery>(
            """
            SELECT r.id AS request_id, r.user_id, u.email::text AS email, u.display_name, u.status AS user_status,
                   r.expires_at, r.used_at, r.invalidated_at
            FROM password_reset_requests r
            JOIN users u ON u.id = r.user_id
            WHERE r.id = @id
            """,
            new { id },
            cancellationToken);

    public async Task<bool> SetTokenHashAsync(Guid id, byte[] tokenHash, DateTime now, CancellationToken cancellationToken) =>
        await db.ExecuteAsync(
            """
            UPDATE password_reset_requests SET token_hash = @tokenHash
            WHERE id = @id AND used_at IS NULL AND invalidated_at IS NULL AND expires_at > @now
            """,
            new { id, tokenHash, now },
            cancellationToken) == 1;
}
