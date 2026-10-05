using BookingPlatform.Application.Identity;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Identity;

public sealed class UserRepository(DbSession db) : IUserRepository
{
    private const string Columns = """
        id, email, password_hash, display_name, status, is_platform_admin, failed_login_count, lockout_until, last_login_at
        """;

    public Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<UserRecord>($"SELECT {Columns} FROM users WHERE email = @email::citext", new { email }, cancellationToken);

    public Task<UserRecord?> GetByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<UserRecord>($"SELECT {Columns} FROM users WHERE id = @userId", new { userId }, cancellationToken);

    public Task<UserRecord?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<UserRecord>($"SELECT {Columns} FROM users WHERE id = @userId FOR UPDATE", new { userId }, cancellationToken);

    public Task RecordFailedLoginAsync(Guid userId, int failedCount, DateTime? lockoutUntil, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE users
            SET failed_login_count = @failedCount,
                lockout_until = COALESCE(@lockoutUntil, lockout_until),
                updated_at = now()
            WHERE id = @userId
            """,
            new { userId, failedCount, lockoutUntil },
            cancellationToken);

    public Task RecordSuccessfulLoginAsync(Guid userId, DateTime now, string? rehashedPassword, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE users
            SET failed_login_count = 0,
                lockout_until = NULL,
                last_login_at = @now,
                password_hash = COALESCE(@rehashedPassword, password_hash),
                updated_at = now()
            WHERE id = @userId
            """,
            new { userId, now, rehashedPassword },
            cancellationToken);

    public Task UpdatePasswordAsync(Guid userId, string passwordHash, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE users
            SET password_hash = @passwordHash, password_changed_at = @now, failed_login_count = 0, lockout_until = NULL, updated_at = now()
            WHERE id = @userId
            """,
            new { userId, passwordHash, now },
            cancellationToken);

    public Task<Guid> CreateAsync(string email, string passwordHash, string displayName, bool isPlatformAdmin, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO users (email, password_hash, display_name, is_platform_admin)
            VALUES (lower(@email)::citext, @passwordHash, @displayName, @isPlatformAdmin)
            RETURNING id
            """,
            new { email, passwordHash, displayName, isPlatformAdmin },
            cancellationToken);

    public async Task<(string Status, bool IsPlatformAdmin)?> GetAccessFlagsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<AccessFlagsRow>(
            "SELECT status, is_platform_admin FROM users WHERE id = @userId", new { userId }, cancellationToken);
        return row is null ? null : (row.Status, row.IsPlatformAdmin);
    }

    private sealed class AccessFlagsRow
    {
        public string Status { get; init; } = string.Empty;
        public bool IsPlatformAdmin { get; init; }
    }
}
