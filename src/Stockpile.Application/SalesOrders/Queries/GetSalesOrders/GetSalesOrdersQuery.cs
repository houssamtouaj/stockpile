using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.SalesOrders.Queries.GetSalesOrders;

public sealed record SalesOrderSummaryDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    Guid WarehouseId,
    SalesOrderStatus Status,
    DateTimeOffset CreatedAt,
    int LineCount,
    int TotalUnits);

public sealed record GetSalesOrdersQuery(
    Guid? WarehouseId = null,
    SalesOrderStatus? Status = null,
    int Page = 1,
    int Size = 25) : IQuery<Result<PagedList<SalesOrderSummaryDto>>>;
