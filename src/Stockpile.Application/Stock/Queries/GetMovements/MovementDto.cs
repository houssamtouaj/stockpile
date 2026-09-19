using Stockpile.Domain.Enums;

namespace Stockpile.Application.Stock.Queries.GetMovements;

/// <summary>
/// UnitCostCents is deliberately absent: phase 05 adds it as a nullable field gated on
/// ICurrentUser.CanViewCosts, so a Viewer's query never reads the column at all.
/// </summary>
public sealed record MovementDto(
    Guid Id,
    Guid ProductId,
    Guid WarehouseId,
    MovementType Type,
    int OnHandDelta,
    int ReservedDelta,
    int OnHandAfter,
    int ReservedAfter,
    string ReferenceType,
    Guid? ReferenceId,
    string? Reason,
    DateTimeOffset OccurredAt,
    Guid PerformedByUserId);
