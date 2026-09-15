using Stockpile.Domain.Common;
using Stockpile.Domain.Events;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Domain.Entities;

/// <summary>
/// A derived snapshot of stock for one product in one warehouse.
/// <para>
/// This is a CACHE. <see cref="StockMovement"/> is the source of truth, and
/// <c>POST /api/stock/reconcile</c> can rebuild every field here by replaying the ledger.
/// </para>
/// <para>
/// There is deliberately no concurrency token. In production these rows are mutated by the
/// atomic conditional UPDATE in <c>StockWriter</c>, not by the change tracker; optimistic
/// concurrency on a hot counter row turns legitimate operations into spurious failures (§6).
/// The methods below exist to state the invariants executably, to be unit-tested, and to
/// be used by in-memory ledger replay.
/// </para>
/// </summary>
public sealed class StockItem : Entity
{
    private StockItem() { }   // EF

    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public int QuantityOnHand { get; private set; }
    public int QuantityReserved { get; private set; }
    public long AverageUnitCostCents { get; private set; }
    public string? BinLocation { get; private set; }
    public DateTimeOffset? LastCountedAt { get; private set; }

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    public static StockItem Create(Guid productId, Guid warehouseId, string? binLocation) =>
        new()
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            BinLocation = binLocation
        };

    public Result Reserve(Quantity quantity, DateTimeOffset occurredAt)
    {
        if (quantity.Value > QuantityAvailable)
            return new InsufficientStockError(quantity.Value, QuantityAvailable);

        QuantityReserved += quantity.Value;
        RaiseChanged(occurredAt);
        return Result.Ok();
    }

    public Result Release(Quantity quantity, DateTimeOffset occurredAt)
    {
        if (quantity.Value > QuantityReserved)
            return new DomainRuleError(
                "stock.release_exceeds_reserved",
                $"Cannot release {quantity.Value} unit(s); only {QuantityReserved} are reserved.");

        QuantityReserved -= quantity.Value;
        RaiseChanged(occurredAt);
        return Result.Ok();
    }

    /// <summary>Ship: converts a reservation into an outbound issue, moving both counters.</summary>
    public Result Issue(Quantity quantity, DateTimeOffset occurredAt)
    {
        if (quantity.Value > QuantityReserved)
            return new DomainRuleError(
                "stock.issue_exceeds_reserved",
                $"Cannot issue {quantity.Value} unit(s); only {QuantityReserved} are reserved.");

        if (quantity.Value > QuantityOnHand)
            return new InsufficientStockError(quantity.Value, QuantityOnHand);

        QuantityOnHand -= quantity.Value;
        QuantityReserved -= quantity.Value;
        RaiseChanged(occurredAt);

        if (QuantityOnHand == 0)
            Raise(new StockDepletedEvent(ProductId, WarehouseId, occurredAt));

        return Result.Ok();
    }

    public Result Receive(Quantity quantity, Money unitCost, DateTimeOffset occurredAt)
    {
        RecomputeAverageCost(quantity.Value, unitCost.Cents);
        QuantityOnHand += quantity.Value;
        RaiseChanged(occurredAt);
        return Result.Ok();
    }

    /// <summary>Cycle count. Takes the observed absolute quantity, never a delta.</summary>
    public Result AdjustTo(int observedOnHand, DateTimeOffset occurredAt)
    {
        if (observedOnHand < 0)
            return new DomainRuleError("stock.count_negative", "A counted quantity cannot be negative.");

        if (observedOnHand < QuantityReserved)
            return new DomainRuleError(
                "stock.count_below_reserved",
                $"Counted {observedOnHand} but {QuantityReserved} unit(s) are reserved. " +
                "Release the reservations before recording this count.");

        QuantityOnHand = observedOnHand;
        LastCountedAt = occurredAt;
        RaiseChanged(occurredAt);

        if (QuantityOnHand == 0)
            Raise(new StockDepletedEvent(ProductId, WarehouseId, occurredAt));

        return Result.Ok();
    }

    /// <summary>
    /// Weighted average: (existing value + incoming value) / total units, rounded to the
    /// nearest cent. FIFO cost layers are out of scope (§2) and documented as an extension.
    /// </summary>
    private void RecomputeAverageCost(int incomingQuantity, long incomingUnitCostCents)
    {
        var totalUnits = QuantityOnHand + incomingQuantity;
        if (totalUnits <= 0)
            return;

        var existingValue = (decimal)AverageUnitCostCents * QuantityOnHand;
        var incomingValue = (decimal)incomingUnitCostCents * incomingQuantity;

        AverageUnitCostCents = (long)Math.Round(
            (existingValue + incomingValue) / totalUnits,
            MidpointRounding.ToEven);
    }

    private void RaiseChanged(DateTimeOffset occurredAt) =>
        Raise(new StockChangedEvent(ProductId, WarehouseId, QuantityOnHand, QuantityReserved, occurredAt));
}
