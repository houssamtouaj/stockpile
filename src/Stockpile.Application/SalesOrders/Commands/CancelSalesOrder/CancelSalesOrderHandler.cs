using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.SalesOrders.Commands.CancelSalesOrder;

/// <summary>
/// §14: cancelling a confirmed order releases every outstanding reservation. The aggregate
/// hands back exactly the lines still holding one, so there is nothing here to forget.
/// </summary>
public sealed class CancelSalesOrderHandler(
    IAppDbContext db,
    IStockWriter writer,
    IStockMutator mutator,
    INotificationPublisher notifications) : IRequestHandler<CancelSalesOrderCommand, Result<SalesOrderDto>>
{
    public async Task<Result<SalesOrderDto>> Handle(
        CancelSalesOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.SalesOrderId, cancellationToken);

        if (order is null)
            return new NotFoundError("SalesOrder", command.SalesOrderId);

        var cancellation = order.Cancel();
        if (cancellation.IsFailure)
            return cancellation.Error;

        // Every row this command writes, locked in one global order before the first write,
        // so two orders sharing products in opposite line order cannot deadlock.
        await writer.LockRowsAsync(
            cancellation.Value.Select(l => (l.ProductId, order.WarehouseId)).ToList(), cancellationToken);

        foreach (var line in cancellation.Value)
        {
            var release = await mutator.ApplyAsync(
                new StockMutationRequest(
                    line.ProductId,
                    order.WarehouseId,
                    DerivedIdempotencyKey.For(command.IdempotencyKey, DerivedIdempotencyKey.CancelLine, line.Id),
                    ReferenceType: nameof(SalesOrder),
                    ReferenceId: order.Id,
                    Reason: "Sales order cancelled",
                    Operation: $"so-cancel:release:{line.QuantityOrdered}"),
                write: (writer, ct) =>
                    writer.TryReleaseAsync(line.ProductId, order.WarehouseId, line.QuantityOrdered, ct),
                buildMovement: (context, write) =>
                    StockMovement.ReservationRelease(
                        context, line.QuantityOrdered, write.OnHandAfter, write.ReservedAfter),
                onRefused: held => new DomainRuleError(
                    "so.release_failed",
                    $"Could not release the reservation for line {line.Id}: {line.QuantityOrdered} "
                    + $"unit(s) were held but only {held.Reserved} are reserved."),
                cancellationToken);

            if (release.IsFailure)
                return release.Error;
        }

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(SalesOrder), order.Status.ToString()));

        return SalesOrderDto.From(order);
    }
}
