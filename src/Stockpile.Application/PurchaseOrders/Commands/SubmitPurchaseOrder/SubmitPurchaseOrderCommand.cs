using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.PurchaseOrders.Commands.SubmitPurchaseOrder;

public sealed record SubmitPurchaseOrderCommand(Guid PurchaseOrderId) : ICommand<PurchaseOrderDto>;

/// <summary>Sending the order to the supplier. No stock moves until goods arrive.</summary>
public sealed class SubmitPurchaseOrderHandler(
    IAppDbContext db,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<SubmitPurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> Handle(
        SubmitPurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.PurchaseOrderId, cancellationToken);

        if (order is null)
            return new NotFoundError("PurchaseOrder", command.PurchaseOrderId);

        var submitted = order.Submit(clock.UtcNow);
        if (submitted.IsFailure)
            return submitted.Error;

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(PurchaseOrder), order.Status.ToString()));

        return PurchaseOrderDto.From(order);
    }
}
