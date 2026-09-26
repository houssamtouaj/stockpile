using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.Transfers.Queries.GetTransfer;

public sealed record StockTransferLineDto(Guid Id, Guid ProductId, int Quantity);

public sealed record StockTransferDto(
    Guid Id,
    string Number,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    Guid InTransitWarehouseId,
    TransferStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<StockTransferLineDto> Lines)
{
    public static StockTransferDto From(StockTransfer t) =>
        new(t.Id, t.Number, t.FromWarehouseId, t.ToWarehouseId, t.InTransitWarehouseId, t.Status,
            t.CreatedAt, t.DispatchedAt, t.ReceivedAt,
            t.Lines.Select(l => new StockTransferLineDto(l.Id, l.ProductId, l.Quantity)).ToList());
}
