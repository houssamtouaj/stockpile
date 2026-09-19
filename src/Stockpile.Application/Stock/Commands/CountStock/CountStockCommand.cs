using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;

namespace Stockpile.Application.Stock.Commands.CountStock;

public sealed record CountStockCommand(
    Guid ProductId,
    Guid WarehouseId,
    int ObservedOnHand,
    string Reason,
    string IdempotencyKey) : ICommand<StockMutationOutcome>;
