using Stockpile.Application.Common.Messaging;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

namespace Stockpile.Application.SalesOrders.Commands.PackSalesOrder;

public sealed record PackSalesOrderCommand(Guid SalesOrderId) : ICommand<SalesOrderDto>;
