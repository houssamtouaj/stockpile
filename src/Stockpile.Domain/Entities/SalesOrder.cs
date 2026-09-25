using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;
using Stockpile.Domain.Events;

namespace Stockpile.Domain.Entities;

public sealed class SalesOrder : Entity
{
    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder() { }   // EF

    public string Number { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public static Result<SalesOrder> Create(
        string number,
        Guid customerId,
        Guid warehouseId,
        DateTimeOffset createdAt,
        IReadOnlyList<(Guid ProductId, int Quantity, long UnitPriceCents)> lines)
    {
        if (lines.Count == 0)
            return new DomainRuleError("so.lines_required", "A sales order needs at least one line.");

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            return new DomainRuleError(
                "so.duplicate_product_line",
                "A product may appear on only one line. Merge the quantities.");

        if (lines.Any(l => l.Quantity <= 0))
            return new DomainRuleError("so.line_quantity_invalid", "Every line quantity must be positive.");

        var order = new SalesOrder
        {
            Number = number,
            CustomerId = customerId,
            WarehouseId = warehouseId,
            Status = SalesOrderStatus.Draft,
            CreatedAt = createdAt
        };

        foreach (var (productId, quantity, unitPriceCents) in lines)
            order._lines.Add(SalesOrderLine.Create(productId, quantity, unitPriceCents));

        return order;
    }

    public Result Confirm(DateTimeOffset occurredAt)
    {
        if (Status != SalesOrderStatus.Draft)
            return InvalidTransition(nameof(Confirm));

        return TransitionTo(SalesOrderStatus.Confirmed, occurredAt);
    }

    public Result Pick(Guid lineId, int quantity)
    {
        if (Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.Picking))
            return InvalidTransition(nameof(Pick));

        if (quantity <= 0)
            return new DomainRuleError("so.pick_quantity_invalid", "Pick quantity must be positive.");

        var line = _lines.SingleOrDefault(l => l.Id == lineId);
        if (line is null)
            return new NotFoundError("SalesOrderLine", lineId);

        if (line.QuantityPicked + quantity > line.QuantityOrdered)
            return new DomainRuleError(
                "so.pick_exceeds_ordered",
                $"Picking {quantity} would exceed the {line.QuantityOrdered} ordered "
                + $"({line.QuantityPicked} already picked).");

        line.RecordPick(quantity);
        Status = SalesOrderStatus.Picking;
        return Result.Ok();
    }

    public Result Pack()
    {
        if (Status != SalesOrderStatus.Picking)
            return InvalidTransition(nameof(Pack));

        if (!_lines.All(l => l.IsFullyPicked))
            return new DomainRuleError(
                "so.lines_not_fully_picked",
                "Every line must be fully picked before the order can be packed.");

        Status = SalesOrderStatus.Packed;
        return Result.Ok();
    }

    public Result Ship(DateTimeOffset occurredAt)
    {
        if (Status != SalesOrderStatus.Packed)
            return InvalidTransition(nameof(Ship));

        ShippedAt = occurredAt;
        return TransitionTo(SalesOrderStatus.Shipped, occurredAt);
    }

    /// <summary>
    /// Cancels the order and returns the lines whose reservations the caller must release.
    /// Returning the list rather than a bare Result is deliberate: the aggregate is the only
    /// thing that knows which reservations are still outstanding, so a handler cannot
    /// forget to release them (§8, §14).
    /// </summary>
    public Result<IReadOnlyList<SalesOrderLine>> Cancel()
    {
        if (Status == SalesOrderStatus.Shipped)
            return new DomainRuleError(
                "so.already_shipped",
                "A shipped order cannot be cancelled. Raise a return instead.");

        if (Status == SalesOrderStatus.Cancelled)
            return new DomainRuleError("so.already_cancelled", "This order is already cancelled.");

        var toRelease = Status == SalesOrderStatus.Draft
            ? []
            : _lines.ToList();

        Status = SalesOrderStatus.Cancelled;
        return Result<IReadOnlyList<SalesOrderLine>>.Ok(toRelease);
    }

    private Result TransitionTo(SalesOrderStatus next, DateTimeOffset occurredAt)
    {
        var from = Status;
        Status = next;

        Raise(new OrderStatusChangedEvent(
            Id, nameof(SalesOrder), from.ToString(), next.ToString(), WarehouseId, occurredAt));

        return Result.Ok();
    }

    private Result InvalidTransition(string action) =>
        new DomainRuleError("so.invalid_transition", $"Cannot {action} an order in status {Status}.");
}
