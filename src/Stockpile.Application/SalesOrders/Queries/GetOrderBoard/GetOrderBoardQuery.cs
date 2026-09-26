using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.SalesOrders.Queries.GetOrderBoard;

public sealed record GetOrderBoardQuery(Guid? WarehouseId = null) : IQuery<Result<OrderBoardDto>>;
