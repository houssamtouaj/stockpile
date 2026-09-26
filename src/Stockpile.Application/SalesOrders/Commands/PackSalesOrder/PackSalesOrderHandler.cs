using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.SalesOrders.Commands.PackSalesOrder;

/// <summary>Like picking, packing moves no stock; the reservation stands until shipping.</summary>
public sealed class PackSalesOrderHandler(
    IAppDbContext db,
    INotificationPublisher notifications) : IRequestHandler<PackSalesOrderCommand, Result<SalesOrderDto>>
{
    public async Task<Result<SalesOrderDto>> Handle(
        PackSalesOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.SalesOrderId, cancellationToken);

        if (order is null)
            return new NotFoundError("SalesOrder", command.SalesOrderId);

        var packed = order.Pack();
        if (packed.IsFailure)
            return packed.Error;

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(SalesOrder), order.Status.ToString()));

        return SalesOrderDto.From(order);
    }
}
