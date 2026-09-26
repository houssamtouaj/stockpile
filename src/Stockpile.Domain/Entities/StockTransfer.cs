using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;
using Stockpile.Domain.Events;

namespace Stockpile.Domain.Entities;

/// <summary>
/// A move between two physical warehouses, routed through a warehouse of
/// <c>Kind = InTransit</c> so the units exist in some stock row at every moment and total
/// valuation never dips mid-flight (§5 correction 2).
/// <para>
/// Cancelling after dispatch is refused rather than implemented. Reversing a dispatch means
/// inventing compensating movements whose cost basis is ambiguous; the honest operational
/// answer is to receive at the destination and transfer back.
/// </para>
/// </summary>
public sealed class StockTransfer : Entity
{
    private readonly List<StockTransferLine> _lines = [];

    private StockTransfer() { }   // EF

    public string Number { get; private set; } = string.Empty;
    public Guid FromWarehouseId { get; private set; }
    public Guid ToWarehouseId { get; private set; }
    public Guid InTransitWarehouseId { get; private set; }
    public TransferStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<StockTransferLine> Lines => _lines;

    public static Result<StockTransfer> Create(
        string number,
        Guid fromWarehouseId,
        Guid toWarehouseId,
        Guid inTransitWarehouseId,
        DateTimeOffset createdAt,
        IReadOnlyList<(Guid ProductId, int Quantity)> lines)
    {
        if (lines.Count == 0)
            return new DomainRuleError("transfer.lines_required", "A transfer needs at least one line.");

        if (fromWarehouseId == toWarehouseId)
            return new DomainRuleError(
                "transfer.same_warehouse", "A transfer must move stock between two different warehouses.");

        if (inTransitWarehouseId == Guid.Empty
            || inTransitWarehouseId == fromWarehouseId
            || inTransitWarehouseId == toWarehouseId)
            return new DomainRuleError(
                "transfer.in_transit_warehouse_invalid",
                "The in-transit leg must use a third warehouse with Kind = InTransit.");

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            return new DomainRuleError(
                "transfer.duplicate_product_line",
                "A product may appear on only one line. Merge the quantities.");

        if (lines.Any(l => l.Quantity <= 0))
            return new DomainRuleError("transfer.line_quantity_invalid", "Every line quantity must be positive.");

        var transfer = new StockTransfer
        {
            Number = number,
            FromWarehouseId = fromWarehouseId,
            ToWarehouseId = toWarehouseId,
            InTransitWarehouseId = inTransitWarehouseId,
            Status = TransferStatus.Draft,
            CreatedAt = createdAt
        };

        foreach (var (productId, quantity) in lines)
            transfer._lines.Add(StockTransferLine.Create(productId, quantity));

        return transfer;
    }

    public Result Dispatch(DateTimeOffset occurredAt)
    {
        if (Status != TransferStatus.Draft)
            return InvalidTransition(nameof(Dispatch));

        DispatchedAt = occurredAt;
        return TransitionTo(TransferStatus.InTransit, FromWarehouseId, occurredAt);
    }

    /// <summary>
    /// Prices one line at the cost its units carried out of the source. Only meaningful as
    /// part of dispatch, so it is refused in any other status.
    /// </summary>
    public Result RecordDispatchCost(Guid lineId, long unitCostCents)
    {
        if (Status != TransferStatus.InTransit)
            return InvalidTransition(nameof(RecordDispatchCost));

        if (unitCostCents < 0)
            return new DomainRuleError("transfer.line_cost_invalid", "A unit cost cannot be negative.");

        var line = _lines.SingleOrDefault(l => l.Id == lineId);
        if (line is null)
            return new NotFoundError("StockTransferLine", lineId);

        line.RecordUnitCost(unitCostCents);
        return Result.Ok();
    }

    public Result Receive(DateTimeOffset occurredAt)
    {
        if (Status != TransferStatus.InTransit)
            return InvalidTransition(nameof(Receive));

        ReceivedAt = occurredAt;
        return TransitionTo(TransferStatus.Received, ToWarehouseId, occurredAt);
    }

    public Result Cancel(DateTimeOffset occurredAt)
    {
        if (Status is TransferStatus.InTransit or TransferStatus.Received)
            return new DomainRuleError(
                "transfer.cannot_cancel_after_dispatch",
                "A dispatched transfer cannot be cancelled. Receive it at the destination, "
                + "then create a transfer back to the source.");

        if (Status == TransferStatus.Cancelled)
            return new DomainRuleError("transfer.already_cancelled", "This transfer is already cancelled.");

        return TransitionTo(TransferStatus.Cancelled, FromWarehouseId, occurredAt);
    }

    private Result TransitionTo(TransferStatus next, Guid warehouseId, DateTimeOffset occurredAt)
    {
        var from = Status;
        Status = next;

        Raise(new OrderStatusChangedEvent(
            Id, nameof(StockTransfer), from.ToString(), next.ToString(), warehouseId, occurredAt));

        return Result.Ok();
    }

    private DomainRuleError InvalidTransition(string action) =>
        new("transfer.invalid_transition", $"Cannot {action} a transfer in status {Status}.");
}
