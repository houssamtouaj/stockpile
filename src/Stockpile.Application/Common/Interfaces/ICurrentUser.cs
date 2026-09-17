using Stockpile.Domain.Enums;

namespace Stockpile.Application.Common.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    Role? Role { get; }

    /// <summary>§8: WarehouseManager and Admin only. Query handlers project cost fields
    /// out of their DTOs when this is false — the UI is not where this is enforced.</summary>
    bool CanViewCosts { get; }

    /// <summary>Flows from the inbound request header to every log line and audit entry.</summary>
    string CorrelationId { get; }
}
