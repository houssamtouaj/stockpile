using Stockpile.Application.Common.Messaging;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

namespace Stockpile.Application.SalesOrders.Commands.CreateSalesOrder;

public sealed record CreateSalesOrderLine(Guid ProductId, int Quantity, long UnitPriceCents);

public sealed record CreateSalesOrderCommand(
    Guid CustomerId,
    Guid WarehouseId,
    IReadOnlyList<CreateSalesOrderLine> Lines) : ICommand<SalesOrderDto>;
