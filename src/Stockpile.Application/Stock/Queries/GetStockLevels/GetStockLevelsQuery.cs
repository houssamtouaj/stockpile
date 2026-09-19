using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Stock.Queries.GetStockLevels;

public sealed record GetStockLevelsQuery(
    Guid? ProductId = null,
    Guid? WarehouseId = null,
    bool LowStockOnly = false) : IQuery<Result<IReadOnlyList<StockLevelDto>>>;
