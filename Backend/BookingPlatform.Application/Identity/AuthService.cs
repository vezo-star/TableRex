using System.Security.Cryptography;
using System.Text;
using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.RateLimiting;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Application.Identity;

public sealed record IssuedRefreshToken(string Token, DateTime ExpiresAt);

public sealed record AuthSession(
    AccessToken AccessToken,
    IssuedRefreshToken RefreshToken,
    Guid UserId,
    Guid? TenantId,
    IReadOnlyList<MembershipSummary> Memberships);

public sealed record MeResult(
    Guid UserId,
    string Email,
    string DisplayName,
    bool IsPlatformAdmin,
    MembershipSummary? CurrentTenant,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<MembershipSummary> Memberships);

public sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IMembershipRepository memberships,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokenIssuer,
    IRateLimiter rateLimiter,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IRequestMetadata requestMetadata,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger,
    IPasswordResetRepository passwordResets,
    IOutboxWriter outbox)
{
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(30);

    private const string InvalidCredentialsMessage = "The email or password is incorrect.";
    private const string InvalidRefreshMessage = "Your session has expired. Please sign in again.";
    private const string InvalidResetMessage = "This password reset link is invalid or has expired. Request a new one.";

    private readonly AuthOptions _options = options.Value;

    public async Task<AuthSession> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalisedEmail = email.Trim().ToLowerInvariant();

        var emailKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalisedEmail)));
        var accountLimit = await rateLimiter.AcquireAsync($"login:account:{emailKey}", 10, TimeSpan.FromMinutes(5), cancellationToken);
        if (!accountLimit.Allowed)
        {
            throw new RateLimitedAppException(accountLimit.RetryAfterSeconds);
        }

        var candidate = await users.GetByEmailAsync(normalisedEmail, cancellationToken);
        if (candidate is null)
        {
            passwordHasher.VerifyDummy(password);
            await audit.WriteAsync(new AuditEntry(null, "user", null, AuditActions.LoginFailed, NewValues: new { reason = "unknown_user" }), cancellationToken);
            logger.LogInformation("Login failed for unknown account");
            throw new UnauthorizedAppException(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }

        var outcome = await unitOfWork.ExecuteAsync(async () =>
        {
            var user = await users.GetByIdForUpdateAsync(candidate.Id, cancellationToken)
                ?? throw new UnauthorizedAppException(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
            var now = clock.UtcNow;

            if (user.LockoutUntil is { } lockedUntil && lockedUntil > now)
            {
                passwordHasher.VerifyDummy(password);
                return LoginOutcome.Locked(lockedUntil);
            }

            var verification = passwordHasher.Verify(user.PasswordHash, password);
            if (verification == PasswordVerification.Failed || user.Status != UserStatus.Active)
            {
                var failedCount = user.FailedLoginCount + 1;
                DateTime? lockoutUntil = null;
                if (failedCount >= _options.LockoutThreshold)
                {
                    var exponent = Math.Min(failedCount - _options.LockoutThreshold, 10);
                    var minutes = Math.Min(_options.LockoutBaseMinutes * Math.Pow(2, exponent), _options.LockoutMaxMinutes);
                    lockoutUntil = now.AddMinutes(minutes);
                }

                await users.RecordFailedLoginAsync(user.Id, failedCount, lockoutUntil, cancellationToken);
                await audit.WriteAsync(new AuditEntry(null, "user", user.Id, AuditActions.LoginFailed,
                    NewValues: new { reason = user.Status != UserStatus.Active ? "inactive_user" : "bad_password", failedCount }), cancellationToken);
                if (lockoutUntil is not null)
                {
                    await audit.WriteAsync(new AuditEntry(null, "user", user.Id, AuditActions.AccountLocked,
                        NewValues: new { lockoutUntil, failedCount }), cancellationToken);
                }

                return LoginOutcome.Failed;
            }

            var rehash = verification == PasswordVerification.SuccessRehashNeeded ? passwordHasher.Hash(password) : null;
            await users.RecordSuccessfulLoginAsync(user.Id, now, rehash, cancellationToken);

            var userMemberships = await memberships.GetActiveMembershipsAsync(user.Id, cancellationToken);
            Guid? tenantId = userMemberships.Count == 1 ? userMemberships[0].TenantId : null;

            var refresh = await IssueRefreshTokenAsync(user.Id, tenantId, Guid.CreateVersion7(), null, now, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "user", user.Id, AuditActions.LoginSucceeded) { ActorUserId = user.Id }, cancellationToken);

            return LoginOutcome.Success(new AuthSession(accessTokenIssuer.Issue(user.Id, tenantId), refresh, user.Id, tenantId, userMemberships));
        }, cancellationToken);

        return outcome switch
        {
            { Session: { } session } => session,
            { LockedUntil: { } until } => throw new LockedAppException(ErrorCodes.AccountLocked,
                "Too many failed sign-in attempts. Try again later.")
            {
                Extensions = new Dictionary<string, object?> { ["retryAfterSeconds"] = Math.Max(1, (int)Math.Ceiling((until - clock.UtcNow).TotalSeconds)) },
            },
            _ => throw new UnauthorizedAppException(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage),
        };
    }

    public async Task<AuthSession> RefreshAsync(string? rawRefreshToken, CancellationToken cancellationToken)
    {
        if (!RefreshTokens.IsWellFormed(rawRefreshToken))
        {
            throw new UnauthorizedAppException(ErrorCodes.InvalidRefreshToken, InvalidRefreshMessage);
        }

        var hash = RefreshTokens.Hash(rawRefreshToken!);
        var outcome = await unitOfWork.ExecuteAsync(async () =>
        {
            var now = clock.UtcNow;
            var token = await refreshTokens.GetByHashForUpdateAsync(hash, cancellationToken);
            if (token is null)
            {
                return (AuthSession?)null;
            }

            if (token.RevokedAt is not null)
            {
                if (token.ReplacedById is not null)
                {
                    // A rotated token was presented again: assume theft and kill the whole family.
                    await refreshTokens.RevokeFamilyAsync(token.FamilyId, RevocationReasons.Reuse, now, cancellationToken);
                    await audit.WriteAsync(new AuditEntry(token.TenantId, "user", token.UserId, AuditActions.RefreshReuseDetected,
                        NewValues: new { familyId = token.FamilyId }), cancellationToken);
                    logger.LogWarning("Refresh token reuse detected for user {UserId}; token family revoked", token.UserId);
                }

                return null;
            }

            if (token.ExpiresAt <= now || token.FamilyExpiresAt <= now)
            {
                return null;
            }

            var flags = await users.GetAccessFlagsAsync(token.UserId, cancellationToken);
            if (flags is not { Status: UserStatus.Active })
            {
                await refreshTokens.RevokeFamilyAsync(token.FamilyId, RevocationReasons.UserDisabled, now, cancellationToken);
                return null;
            }

            if (token.TenantId is { } tenantId
                && await memberships.GetTenantAccessAsync(token.UserId, tenantId, cancellationToken) is null)
            {
                await refreshTokens.RevokeFamilyAsync(token.FamilyId, RevocationReasons.MembershipRevoked, now, cancellationToken);
                return null;
            }

            var refresh = await IssueRefreshTokenAsync(token.UserId, token.TenantId, token.FamilyId, token.FamilyExpiresAt, now, cancellationToken,
                rotatedFrom: token.Id);
            var userMemberships = await memberships.GetActiveMembershipsAsync(token.UserId, cancellationToken);
            return new AuthSession(accessTokenIssuer.Issue(token.UserId, token.TenantId), refresh, token.UserId, token.TenantId, userMemberships);
        }, cancellationToken);

        return outcome ?? throw new UnauthorizedAppException(ErrorCodes.InvalidRefreshToken, InvalidRefreshMessage);
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken cancellationToken)
    {
        if (!RefreshTokens.IsWellFormed(rawRefreshToken))
        {
            return;
        }

        var hash = RefreshTokens.Hash(rawRefreshToken!);
        await unitOfWork.ExecuteAsync(async () =>
        {
            var token = await refreshTokens.GetByHashForUpdateAsync(hash, cancellationToken);
            if (token is not null)
            {
                await refreshTokens.RevokeFamilyAsync(token.FamilyId, RevocationReasons.Logout, clock.UtcNow, cancellationToken);
                if (token.RevokedAt is null)
                {
                    await audit.WriteAsync(new AuditEntry(token.TenantId, "user", token.UserId, AuditActions.Logout) { ActorUserId = token.UserId },
                        cancellationToken);
                }
            }
        }, cancellationToken);
    }

    public async Task<AuthSession> SwitchTenantAsync(Guid userId, Guid tenantId, string? currentRefreshToken, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteAsync(async () =>
        {
            var now = clock.UtcNow;
            var access = await memberships.GetTenantAccessAsync(userId, tenantId, cancellationToken)
                ?? throw new NotFoundAppException(ErrorCodes.TenantNotFound, "The tenant was not found.");

            await RevokeCurrentFamilyAsync(userId, currentRefreshToken, RevocationReasons.TenantSwitched, now, cancellationToken);

            var refresh = await IssueRefreshTokenAsync(userId, access.TenantId, Guid.CreateVersion7(), null, now, cancellationToken);
            await audit.WriteAsync(new AuditEntry(access.TenantId, "user", userId, AuditActions.TenantSwitched), cancellationToken);
            var userMemberships = await memberships.GetActiveMembershipsAsync(userId, cancellationToken);
            return new AuthSession(accessTokenIssuer.Issue(userId, access.TenantId), refresh, userId, access.TenantId, userMemberships);
        }, cancellationToken);
    }

    public async Task<AuthSession> ChangePasswordAsync(
        Guid userId, Guid? tenantId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var limit = await rateLimiter.AcquireAsync($"change-password:{userId:N}", 10, TimeSpan.FromMinutes(15), cancellationToken);
        if (!limit.Allowed)
        {
            throw new RateLimitedAppException(limit.RetryAfterSeconds);
        }

        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAppException(ErrorCodes.Unauthorized, "Authentication is required.");

        if (passwordHasher.Verify(user.PasswordHash, currentPassword) == PasswordVerification.Failed)
        {
            throw new ValidationAppException("currentPassword", "The current password is incorrect.");
        }

        if (PasswordPolicy.Validate(newPassword, user.Email) is { } policyError)
        {
            throw new ValidationAppException("newPassword", policyError);
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            throw new ValidationAppException("newPassword", "The new password must be different from the current password.");
        }

        var newHash = passwordHasher.Hash(newPassword);
        return await unitOfWork.ExecuteAsync(async () =>
        {
            var now = clock.UtcNow;
            await users.UpdatePasswordAsync(userId, newHash, now, cancellationToken);
            await refreshTokens.RevokeAllForUserAsync(userId, RevocationReasons.PasswordChanged, now, cancellationToken);
            await passwordResets.InvalidateOutstandingAsync(userId, now, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "user", userId, AuditActions.PasswordChanged), cancellationToken);

            var refresh = await IssueRefreshTokenAsync(userId, tenantId, Guid.CreateVersion7(), null, now, cancellationToken);
            var userMemberships = await memberships.GetActiveMembershipsAsync(userId, cancellationToken);
            return new AuthSession(accessTokenIssuer.Issue(userId, tenantId), refresh, userId, tenantId, userMemberships);
        }, cancellationToken);
    }

    /// <summary>
    /// Queues a reset email for an active account. Always completes the same way so callers cannot tell whether the
    /// email belongs to an account; per-email throttling silently drops excess requests.
    /// </summary>
    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var normalisedEmail = email.Trim().ToLowerInvariant();
        var emailKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalisedEmail)));
        var limit = await rateLimiter.AcquireAsync($"password-reset:account:{emailKey}", 3, TimeSpan.FromHours(1), cancellationToken);
        if (!limit.Allowed)
        {
            logger.LogInformation("Password reset request throttled for one account");
            return;
        }

        var user = await users.GetByEmailAsync(normalisedEmail, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            return;
        }

        await unitOfWork.ExecuteAsync(async () =>
        {
            var now = clock.UtcNow;
            var requestId = Guid.CreateVersion7();
            await passwordResets.InvalidateOutstandingAsync(user.Id, now, cancellationToken);
            await passwordResets.InsertAsync(requestId, user.Id, now + PasswordResetLifetime, requestMetadata.IpAddress, cancellationToken);
            await outbox.WritePasswordResetAsync(requestId, now, cancellationToken);
            await audit.WriteAsync(new AuditEntry(null, "user", user.Id, AuditActions.PasswordResetRequested) { ActorUserId = user.Id }, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Sets a new password from an emailed token, unlocks the account and signs out every session.</summary>
    public async Task ResetPasswordAsync(string? token, string newPassword, CancellationToken cancellationToken)
    {
        if (!RefreshTokens.IsWellFormed(token))
        {
            throw new BadRequestAppException(ErrorCodes.InvalidResetToken, InvalidResetMessage);
        }

        var hash = RefreshTokens.Hash(token!);
        await unitOfWork.ExecuteAsync(async () =>
        {
            var now = clock.UtcNow;
            var request = await passwordResets.GetByTokenHashForUpdateAsync(hash, cancellationToken);
            if (request is null || request.UsedAt is not null || request.InvalidatedAt is not null || request.ExpiresAt <= now)
            {
                throw new BadRequestAppException(ErrorCodes.InvalidResetToken, InvalidResetMessage);
            }

            var user = await users.GetByIdForUpdateAsync(request.UserId, cancellationToken);
            if (user is null || user.Status != UserStatus.Active)
            {
                throw new BadRequestAppException(ErrorCodes.InvalidResetToken, InvalidResetMessage);
            }

            if (PasswordPolicy.Validate(newPassword, user.Email) is { } policyError)
            {
                throw new ValidationAppException("newPassword", policyError);
            }

            await users.UpdatePasswordAsync(user.Id, passwordHasher.Hash(newPassword), now, cancellationToken);
            await passwordResets.MarkUsedAsync(request.Id, now, cancellationToken);
            await passwordResets.InvalidateOutstandingAsync(user.Id, now, cancellationToken);
            await refreshTokens.RevokeAllForUserAsync(user.Id, RevocationReasons.PasswordReset, now, cancellationToken);
            await audit.WriteAsync(new AuditEntry(null, "user", user.Id, AuditActions.PasswordReset) { ActorUserId = user.Id }, cancellationToken);
        }, cancellationToken);
    }

    public async Task<MeResult> GetMeAsync(Guid userId, ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAppException(ErrorCodes.Unauthorized, "Authentication is required.");
        var userMemberships = await memberships.GetActiveMembershipsAsync(userId, cancellationToken);
        var current = tenantContext.HasTenant ? userMemberships.FirstOrDefault(m => m.TenantId == tenantContext.TenantId) : null;

        return new MeResult(
            user.Id,
            user.Email,
            user.DisplayName,
            user.IsPlatformAdmin,
            current,
            tenantContext.Roles.Order(StringComparer.Ordinal).ToList(),
            tenantContext.Permissions.Order(StringComparer.Ordinal).ToList(),
            userMemberships);
    }

    private async Task RevokeCurrentFamilyAsync(Guid userId, string? rawRefreshToken, string reason, DateTime now, CancellationToken cancellationToken)
    {
        if (!RefreshTokens.IsWellFormed(rawRefreshToken))
        {
            return;
        }

        var token = await refreshTokens.GetByHashForUpdateAsync(RefreshTokens.Hash(rawRefreshToken!), cancellationToken);
        if (token is not null && token.UserId == userId)
        {
            await refreshTokens.RevokeFamilyAsync(token.FamilyId, reason, now, cancellationToken);
        }
    }

    private async Task<IssuedRefreshToken> IssueRefreshTokenAsync(
        Guid userId, Guid? tenantId, Guid familyId, DateTime? familyExpiresAt, DateTime now, CancellationToken cancellationToken,
        Guid? rotatedFrom = null)
    {
        var raw = RefreshTokens.Generate();
        var familyExpiry = familyExpiresAt ?? now.AddDays(_options.RefreshTokenAbsoluteDays);
        var expires = now.AddDays(_options.RefreshTokenDays);
        if (expires > familyExpiry)
        {
            expires = familyExpiry;
        }

        var newId = await refreshTokens.InsertAsync(new NewRefreshToken(
            userId, tenantId, RefreshTokens.Hash(raw), familyId, expires, familyExpiry,
            requestMetadata.IpAddress, requestMetadata.UserAgent), cancellationToken);

        if (rotatedFrom is { } previousId)
        {
            await refreshTokens.MarkRotatedAsync(previousId, newId, now, cancellationToken);
        }

        return new IssuedRefreshToken(raw, expires);
    }

    private sealed record LoginOutcome(AuthSession? Session, DateTime? LockedUntil)
    {
        public static readonly LoginOutcome Failed = new(null, null);

        public static LoginOutcome Success(AuthSession session) => new(session, null);

        public static LoginOutcome Locked(DateTime until) => new(null, until);
    }
}
