using Stockpile.Domain.Common;

namespace Stockpile.Domain.Entities;

public sealed class SalesOrderLine : Entity
{
    private SalesOrderLine() { }   // EF

    public Guid SalesOrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int QuantityOrdered { get; private set; }
    public int QuantityPicked { get; private set; }
    public long UnitPriceCents { get; private set; }

    public bool IsFullyPicked => QuantityPicked >= QuantityOrdered;

    internal static SalesOrderLine Create(Guid productId, int quantity, long unitPriceCents) =>
        new()
        {
            ProductId = productId,
            QuantityOrdered = quantity,
            QuantityPicked = 0,
            UnitPriceCents = unitPriceCents
        };

    internal void RecordPick(int quantity) => QuantityPicked += quantity;
}
