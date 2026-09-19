using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;

namespace Stockpile.Application.Stock.Commands.ReserveStock;

public sealed record ReserveStockCommand(
    Guid ProductId,
    Guid WarehouseId,
    int Quantity,
    string IdempotencyKey,
    string? ReferenceType = null,
    Guid? ReferenceId = null) : ICommand<StockMutationOutcome>;
