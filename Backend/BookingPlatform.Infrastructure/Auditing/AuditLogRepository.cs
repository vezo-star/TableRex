using System.Text.Json;
using BookingPlatform.Application.Auditing;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Auditing;

public sealed class AuditLogRepository(DbSession db) : IAuditLogRepository
{
    public async Task<IReadOnlyList<AuditLogDto>> ListAsync(Guid tenantId, AuditLogQuery query, AuditCursor? before, int take, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<AuditRow>(
            """
            SELECT a.id, a.entity_type, a.entity_id, a.action, a.user_id, u.email::text AS user_email, u.display_name AS user_display_name,
                   a.old_values::text AS old_values, a.new_values::text AS new_values, a.ip, a.user_agent, a.correlation_id, a.created_at
            FROM audit_logs a
            LEFT JOIN users u ON u.id = a.user_id
            WHERE a.tenant_id = @tenantId
              AND (@entityType::text IS NULL OR a.entity_type = @entityType)
              AND (@entityId::uuid IS NULL OR a.entity_id = @entityId)
              AND (@userId::uuid IS NULL OR a.user_id = @userId)
              AND (@action::text IS NULL OR a.action = @action)
              AND (@from::timestamptz IS NULL OR a.created_at >= @from)
              AND (@to::timestamptz IS NULL OR a.created_at < @to)
              AND (@beforeAt::timestamptz IS NULL OR (a.created_at, a.id) < (@beforeAt, @beforeId))
            ORDER BY a.created_at DESC, a.id DESC
            LIMIT @take
            """,
            new
            {
                tenantId,
                entityType = Blank(query.EntityType),
                entityId = query.EntityId,
                userId = query.UserId,
                action = Blank(query.Action),
                from = query.From?.ToUniversalTime(),
                to = query.To?.ToUniversalTime(),
                beforeAt = before?.CreatedAt,
                beforeId = before?.Id ?? Guid.Empty,
                take,
            },
            cancellationToken);

        return rows.Select(r => new AuditLogDto(r.Id, r.EntityType, r.EntityId, r.Action, r.UserId, r.UserEmail, r.UserDisplayName,
            Parse(r.OldValues), Parse(r.NewValues), r.Ip, r.UserAgent, r.CorrelationId, r.CreatedAt)).ToList();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonElement? Parse(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class AuditRow
    {
        public Guid Id { get; init; }
        public string EntityType { get; init; } = string.Empty;
        public Guid? EntityId { get; init; }
        public string Action { get; init; } = string.Empty;
        public Guid? UserId { get; init; }
        public string? UserEmail { get; init; }
        public string? UserDisplayName { get; init; }
        public string? OldValues { get; init; }
        public string? NewValues { get; init; }
        public string? Ip { get; init; }
        public string? UserAgent { get; init; }
        public string? CorrelationId { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
