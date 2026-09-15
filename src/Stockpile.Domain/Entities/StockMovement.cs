using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Domain.Entities;

/// <summary>The fields every movement needs regardless of type. Keeps the factory
/// signatures short enough to read and impossible to mis-order.</summary>
public sealed record MovementContext(
    Guid ProductId,
    Guid WarehouseId,
    string ReferenceType,
    Guid? ReferenceId,
    string IdempotencyKey,
    string? Reason,
    DateTimeOffset OccurredAt,
    Guid PerformedByUserId);

/// <summary>
/// APPEND ONLY. Never updated, never deleted — enforced by a database trigger (task 9).
/// <para>
/// Two signed deltas because reservations and on-hand move independently; a single delta
/// column cannot rebuild both snapshot columns during reconciliation (§5 correction 1).
/// </para>
/// <para>
/// <see cref="OnHandAfter"/> and <see cref="ReservedAfter"/> must always be written from the
/// conditional UPDATE's RETURNING clause. Computing them from a pre-update in-memory read
/// reintroduces the race the whole design exists to eliminate (§6).
/// </para>
/// </summary>
public sealed class StockMovement : Entity
{
    private StockMovement() { }   // EF

    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public MovementType Type { get; private set; }

    public int OnHandDelta { get; private set; }
    public int ReservedDelta { get; private set; }
    public int OnHandAfter { get; private set; }
    public int ReservedAfter { get; private set; }

    public long? UnitCostCents { get; private set; }
    public string ReferenceType { get; private set; } = string.Empty;
    public Guid? ReferenceId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid PerformedByUserId { get; private set; }

    public static StockMovement Receipt(
        MovementContext context, int quantity, long unitCostCents, int onHandAfter, int reservedAfter) =>
        Build(context, MovementType.Receipt, quantity, 0, onHandAfter, reservedAfter, unitCostCents);

    /// <summary>Shipping. Converts a reservation to an outbound issue, so BOTH deltas are negative.</summary>
    public static StockMovement Issue(
        MovementContext context, int quantity, int onHandAfter, int reservedAfter) =>
        Build(context, MovementType.Issue, -quantity, -quantity, onHandAfter, reservedAfter);

    public static StockMovement ReservationHold(
        MovementContext context, int quantity, int onHandAfter, int reservedAfter) =>
        Build(context, MovementType.ReservationHold, 0, quantity, onHandAfter, reservedAfter);

    public static StockMovement ReservationRelease(
        MovementContext context, int quantity, int onHandAfter, int reservedAfter) =>
        Build(context, MovementType.ReservationRelease, 0, -quantity, onHandAfter, reservedAfter);

    public static StockMovement Adjustment(
        MovementContext context, int onHandDelta, int onHandAfter, int reservedAfter) =>
        Build(context, MovementType.Adjustment, onHandDelta, 0, onHandAfter, reservedAfter);

    public static StockMovement Count(
        MovementContext context, int previousOnHand, int observedOnHand, int reservedAfter) =>
        Build(context, MovementType.Count, observedOnHand - previousOnHand, 0, observedOnHand, reservedAfter);

    public static StockMovement TransferOut(
        MovementContext context, int quantity, int onHandAfter, int reservedAfter) =>
        Build(context, MovementType.TransferOut, -quantity, 0, onHandAfter, reservedAfter);

    public static StockMovement TransferIn(
        MovementContext context, int quantity, long unitCostCents, int onHandAfter, int reservedAfter) =>
        Build(context, MovementType.TransferIn, quantity, 0, onHandAfter, reservedAfter, unitCostCents);

    private static StockMovement Build(
        MovementContext context,
        MovementType type,
        int onHandDelta,
        int reservedDelta,
        int onHandAfter,
        int reservedAfter,
        long? unitCostCents = null) =>
        new()
        {
            ProductId = context.ProductId,
            WarehouseId = context.WarehouseId,
            Type = type,
            OnHandDelta = onHandDelta,
            ReservedDelta = reservedDelta,
            OnHandAfter = onHandAfter,
            ReservedAfter = reservedAfter,
            UnitCostCents = unitCostCents,
            ReferenceType = context.ReferenceType,
            ReferenceId = context.ReferenceId,
            IdempotencyKey = context.IdempotencyKey,
            Reason = context.Reason,
            OccurredAt = context.OccurredAt,
            PerformedByUserId = context.PerformedByUserId
        };
}
