using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

public sealed record GetSalesOrderQuery(Guid Id) : IQuery<Result<SalesOrderDto>>;
