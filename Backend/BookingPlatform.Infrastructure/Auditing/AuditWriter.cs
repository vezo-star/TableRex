using System.Text.Json;
using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Auditing;

/// <summary>Writes audit rows through the scoped session, so they commit with the business change.</summary>
public sealed class AuditWriter(DbSession db, ICurrentUser currentUser, IRequestMetadata metadata) : IAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> RedactedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwordHash", "password_hash", "token", "tokenHash", "token_hash", "refreshToken", "secret",
        "signingKey", "authorization", "publicToken", "public_token", "publicTokenHash", "public_token_hash",
    };

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return db.ExecuteAsync(
            """
            INSERT INTO audit_logs (tenant_id, user_id, entity_type, entity_id, action, old_values, new_values, ip, user_agent, correlation_id)
            VALUES (@TenantId, @UserId, @EntityType, @EntityId, @Action, @OldValues::jsonb, @NewValues::jsonb, @Ip, @UserAgent, @CorrelationId)
            """,
            new
            {
                entry.TenantId,
                UserId = entry.ActorUserId ?? currentUser.UserId,
                entry.EntityType,
                entry.EntityId,
                entry.Action,
                OldValues = Serialize(entry.OldValues),
                NewValues = Serialize(entry.NewValues),
                Ip = metadata.IpAddress,
                metadata.UserAgent,
                metadata.CorrelationId,
            },
            cancellationToken);
    }

    public static string? Serialize(object? values)
    {
        if (values is null)
        {
            return null;
        }

        var element = JsonSerializer.SerializeToElement(values, JsonOptions);
        return Redact(element)?.ToJsonString();
    }

    private static System.Text.Json.Nodes.JsonNode? Redact(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new System.Text.Json.Nodes.JsonObject();
                foreach (var property in element.EnumerateObject())
                {
                    obj[property.Name] = RedactedKeys.Contains(property.Name) ? "[REDACTED]" : Redact(property.Value);
                }

                return obj;
            case JsonValueKind.Array:
                var array = new System.Text.Json.Nodes.JsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(Redact(item));
                }

                return array;
            default:
                return System.Text.Json.Nodes.JsonValue.Create(element);
        }
    }
}
