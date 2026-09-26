using Stockpile.Domain.Common;

namespace Stockpile.Domain.Entities;

public sealed class StockTransferLine : Entity
{
    private StockTransferLine() { }   // EF

    public Guid StockTransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    /// <summary>
    /// What each unit was worth when it left the source, recorded at dispatch. Null until
    /// then. The receipt moves the units at this cost rather than at the in-transit row's
    /// average, which blends every transfer of the product currently on the road.
    /// </summary>
    public long? UnitCostCents { get; private set; }

    internal static StockTransferLine Create(Guid productId, int quantity) =>
        new() { ProductId = productId, Quantity = quantity };

    internal void RecordUnitCost(long unitCostCents) => UnitCostCents = unitCostCents;
}
