using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.SalesOrders.Commands.PickSalesOrder;

/// <summary>
/// Picking is a physical act inside the warehouse: the units are already reserved and still
/// on the shelf, so NO stock movement is written. Only shipping moves stock. Issuing at pick
/// time is the most common way these models end up double-counting.
/// </summary>
public sealed class PickSalesOrderHandler(
    IAppDbContext db,
    INotificationPublisher notifications) : IRequestHandler<PickSalesOrderCommand, Result<SalesOrderDto>>
{
    public async Task<Result<SalesOrderDto>> Handle(
        PickSalesOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.SalesOrderId, cancellationToken);

        if (order is null)
            return new NotFoundError("SalesOrder", command.SalesOrderId);

        var picked = order.Pick(command.LineId, command.Quantity);
        if (picked.IsFailure)
            return picked.Error;

        // A second pick on an order already in Picking changes only the line, and lines
        // carry no concurrency token — two concurrent picks would each read the same
        // QuantityPicked and one would be lost. Marking Status modified puts the order row
        // (and its xmin check) into every pick's UPDATE, so the loser gets a 409.
        db.Entry(order).Property(o => o.Status).IsModified = true;

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(SalesOrder), order.Status.ToString()));

        return SalesOrderDto.From(order);
    }
}
