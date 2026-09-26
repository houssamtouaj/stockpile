using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;
using Stockpile.Domain.Events;

namespace Stockpile.Domain.Entities;

public sealed class PurchaseOrder : Entity
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder() { }   // EF

    public string Number { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public DateTimeOffset? ExpectedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    public static Result<PurchaseOrder> Create(
        string number,
        Guid supplierId,
        Guid warehouseId,
        DateTimeOffset? expectedAt,
        DateTimeOffset createdAt,
        IReadOnlyList<(Guid ProductId, int Quantity, long UnitCostCents)> lines)
    {
        var valid = ValidateLines(lines);
        if (valid.IsFailure)
            return valid.Error;

        var order = new PurchaseOrder
        {
            Number = number,
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            Status = PurchaseOrderStatus.Draft,
            ExpectedAt = expectedAt,
            CreatedAt = createdAt
        };

        foreach (var (productId, quantity, unitCostCents) in lines)
            order._lines.Add(PurchaseOrderLine.Create(productId, quantity, unitCostCents));

        return order;
    }

    /// <summary>
    /// The line rules <see cref="Create"/> enforces, callable before there is a number to
    /// create with: document numbers come from a sequence that does not roll back, so a
    /// handler asks first and numbers only an order that will be created.
    /// </summary>
    public static Result ValidateLines(IReadOnlyList<(Guid ProductId, int Quantity, long UnitCostCents)> lines)
    {
        if (lines.Count == 0)
            return new DomainRuleError("po.lines_required", "A purchase order needs at least one line.");

        // One line per product keeps "how much of X is outstanding" a single-row question,
        // and a receipt against X unambiguous about which line it completes.
        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            return new DomainRuleError(
                "po.duplicate_product_line",
                "A product may appear on only one line. Merge the quantities.");

        if (lines.Any(l => l.Quantity <= 0))
            return new DomainRuleError("po.line_quantity_invalid", "Every line quantity must be positive.");

        if (lines.Any(l => l.UnitCostCents < 0))
            return new DomainRuleError("po.line_cost_invalid", "A unit cost cannot be negative.");

        return Result.Ok();
    }

    public Result Submit(DateTimeOffset occurredAt)
    {
        if (Status != PurchaseOrderStatus.Draft)
            return InvalidTransition(nameof(Submit));

        return TransitionTo(PurchaseOrderStatus.Submitted, occurredAt);
    }

    /// <summary>
    /// Records a (possibly partial) receipt against one line and recomputes the order's
    /// status. Returns the line so the handler has its UnitCostCents for the ledger and the
    /// weighted-average recompute without a second lookup.
    /// </summary>
    public Result<PurchaseOrderLine> ReceiveLine(Guid lineId, int quantity, DateTimeOffset occurredAt)
    {
        if (Status is not (PurchaseOrderStatus.Submitted or PurchaseOrderStatus.PartiallyReceived))
            return InvalidTransition(nameof(ReceiveLine));

        if (quantity <= 0)
            return new DomainRuleError("po.receipt_quantity_invalid", "Receipt quantity must be positive.");

        var line = _lines.SingleOrDefault(l => l.Id == lineId);
        if (line is null)
            return new NotFoundError("PurchaseOrderLine", lineId);

        if (quantity > line.QuantityOutstanding)
            return new DomainRuleError(
                "po.receipt_exceeds_outstanding",
                $"Receiving {quantity} would exceed the {line.QuantityOutstanding} still outstanding "
                + $"on this line ({line.QuantityReceived} of {line.QuantityOrdered} already received).");

        line.RecordReceipt(quantity);

        var next = _lines.All(l => l.IsFullyReceived)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;

        if (next != Status)
            TransitionTo(next, occurredAt);

        return line;
    }

    public Result Cancel(DateTimeOffset occurredAt)
    {
        if (Status == PurchaseOrderStatus.Received)
            return new DomainRuleError(
                "po.already_received",
                "A fully received purchase order cannot be cancelled. Return the goods to the supplier instead.");

        if (Status == PurchaseOrderStatus.Cancelled)
            return new DomainRuleError("po.already_cancelled", "This purchase order is already cancelled.");

        // Cancelling a partially received order keeps what already arrived: those units
        // are on the shelf and in the ledger. Only the outstanding remainder is abandoned.
        return TransitionTo(PurchaseOrderStatus.Cancelled, occurredAt);
    }

    private Result TransitionTo(PurchaseOrderStatus next, DateTimeOffset occurredAt)
    {
        var from = Status;
        Status = next;

        Raise(new OrderStatusChangedEvent(
            Id, nameof(PurchaseOrder), from.ToString(), next.ToString(), WarehouseId, occurredAt));

        return Result.Ok();
    }

    private DomainRuleError InvalidTransition(string action) =>
        new("po.invalid_transition", $"Cannot {action} a purchase order in status {Status}.");
}
