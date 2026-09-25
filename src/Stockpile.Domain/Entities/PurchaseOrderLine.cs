using Stockpile.Domain.Common;

namespace Stockpile.Domain.Entities;

public sealed class PurchaseOrderLine : Entity
{
    private PurchaseOrderLine() { }   // EF

    public Guid PurchaseOrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int QuantityOrdered { get; private set; }
    public int QuantityReceived { get; private set; }

    /// <summary>What this delivery costs per unit. Written to the Receipt movement as-is;
    /// the stock row's average is the blended figure, computed by the receipt SQL.</summary>
    public long UnitCostCents { get; private set; }

    public int QuantityOutstanding => QuantityOrdered - QuantityReceived;
    public bool IsFullyReceived => QuantityReceived >= QuantityOrdered;

    internal static PurchaseOrderLine Create(Guid productId, int quantity, long unitCostCents) =>
        new()
        {
            ProductId = productId,
            QuantityOrdered = quantity,
            QuantityReceived = 0,
            UnitCostCents = unitCostCents
        };

    internal void RecordReceipt(int quantity) => QuantityReceived += quantity;
}
