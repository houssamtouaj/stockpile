using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.PurchaseOrders.Commands.CancelPurchaseOrder;

public sealed record CancelPurchaseOrderCommand(Guid PurchaseOrderId) : ICommand<PurchaseOrderDto>;

/// <summary>
/// Moves no stock: a purchase order reserves nothing, and whatever a partially received
/// order already delivered stays on the shelf and in the ledger.
/// </summary>
public sealed class CancelPurchaseOrderHandler(
    IAppDbContext db,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<CancelPurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> Handle(
        CancelPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.PurchaseOrderId, cancellationToken);

        if (order is null)
            return new NotFoundError("PurchaseOrder", command.PurchaseOrderId);

        var cancelled = order.Cancel(clock.UtcNow);
        if (cancelled.IsFailure)
            return cancelled.Error;

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(PurchaseOrder), order.Status.ToString()));

        return PurchaseOrderDto.From(order);
    }
}
