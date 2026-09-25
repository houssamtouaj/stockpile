using Stockpile.Domain.Common;

namespace Stockpile.Domain.Entities;

public sealed class StockTransferLine : Entity
{
    private StockTransferLine() { }   // EF

    public Guid StockTransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    internal static StockTransferLine Create(Guid productId, int quantity) =>
        new() { ProductId = productId, Quantity = quantity };
}
