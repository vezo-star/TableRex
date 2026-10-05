using System.Globalization;
using System.Text;
using System.Text.Json;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using FluentValidation;

namespace BookingPlatform.Application.Auditing;

public sealed record AuditLogDto(
    Guid Id,
    string EntityType,
    Guid? EntityId,
    string Action,
    Guid? UserId,
    string? UserEmail,
    string? UserDisplayName,
    JsonElement? OldValues,
    JsonElement? NewValues,
    string? Ip,
    string? UserAgent,
    string? CorrelationId,
    DateTime CreatedAt);

public sealed record AuditLogQuery(
    string? EntityType,
    Guid? EntityId,
    Guid? UserId,
    string? Action,
    DateTime? From,
    DateTime? To,
    string? Before,
    int Limit = AuditLogQuery.DefaultLimit)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
}

/// <summary>Newest first; pass <see cref="NextCursor"/> as <c>before</c> to load the next page.</summary>
public sealed record AuditLogPage(IReadOnlyList<AuditLogDto> Items, string? NextCursor);

/// <summary>Keyset position: rows strictly older than (<see cref="CreatedAt"/>, <see cref="Id"/>).</summary>
public sealed record AuditCursor(DateTime CreatedAt, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture)}_{Id:N}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static AuditCursor? TryDecode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
        {
            return null;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('_');
            return parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is >= 0 and <= 3155378975999999999
                && Guid.TryParseExact(parts[1], "N", out var id)
                ? new AuditCursor(new DateTime(ticks, DateTimeKind.Utc), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

public interface IAuditLogRepository
{
    Task<IReadOnlyList<AuditLogDto>> ListAsync(Guid tenantId, AuditLogQuery query, AuditCursor? before, int take, CancellationToken cancellationToken);
}

public sealed class AuditLogQueryValidator : AbstractValidator<AuditLogQuery>
{
    public AuditLogQueryValidator()
    {
        RuleFor(q => q.EntityType).MaximumLength(100);
        RuleFor(q => q.Action).MaximumLength(100);
        RuleFor(q => q.Limit).InclusiveBetween(1, AuditLogQuery.MaxLimit);
        RuleFor(q => q.Before)
            .Must(b => b is null || AuditCursor.TryDecode(b) is not null)
            .WithMessage("Invalid cursor.");
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.From <= q.To)
            .WithName("to")
            .WithMessage("'to' must be on or after 'from'.");
    }
}

/// <summary>Read-only access to the tenant's audit trail (settings.manage). Values are stored already redacted.</summary>
public sealed class AuditLogService(IAuditLogRepository repository, ITenantContext tenantContext, IValidator<AuditLogQuery> validator)
{
    public async Task<AuditLogPage> ListAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await validator.EnsureValidAsync(query, cancellationToken);
        var before = AuditCursor.TryDecode(query.Before);
        if (query.Before is not null && before is null)
        {
            throw new ValidationAppException("before", "Invalid cursor.");
        }

        var rows = await repository.ListAsync(tenantContext.TenantId, query, before, query.Limit + 1, cancellationToken);
        var items = rows.Take(query.Limit).ToList();
        var next = rows.Count > query.Limit ? new AuditCursor(items[^1].CreatedAt, items[^1].Id).Encode() : null;
        return new AuditLogPage(items, next);
    }
}
