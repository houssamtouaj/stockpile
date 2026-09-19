using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;

namespace Stockpile.Application.Stock.Commands.AdjustStock;

public sealed record AdjustStockCommand(
    Guid ProductId,
    Guid WarehouseId,
    int OnHandDelta,
    string Reason,
    string IdempotencyKey) : ICommand<StockMutationOutcome>;
