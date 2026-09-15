using Stockpile.Domain.Common;

namespace Stockpile.Domain.Entities;

/// <summary>
/// APPEND ONLY, like StockMovement. Old and new values are raw JSON strings here so the
/// Domain layer stays free of any serializer dependency; the EF configuration maps them
/// to jsonb columns (task 9) and the interceptor that fills them lives in Infrastructure.
/// </summary>
public sealed class AuditEntry : Entity
{
    private AuditEntry() { }   // EF

    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public Guid UserId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }

    public static AuditEntry Create(
        string entityType,
        Guid entityId,
        string action,
        string? oldValuesJson,
        string? newValuesJson,
        Guid userId,
        string correlationId,
        DateTimeOffset occurredAt) =>
        new()
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValues = oldValuesJson,
            NewValues = newValuesJson,
            UserId = userId,
            CorrelationId = correlationId,
            OccurredAt = occurredAt
        };
}
