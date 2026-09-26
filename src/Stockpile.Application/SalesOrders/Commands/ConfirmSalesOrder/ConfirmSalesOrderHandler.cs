using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.SalesOrders.Commands.ConfirmSalesOrder;

public sealed class ConfirmSalesOrderHandler(
    IAppDbContext db,
    IStockMutator mutator,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<ConfirmSalesOrderCommand, Result<SalesOrderDto>>
{
    public async Task<Result<SalesOrderDto>> Handle(
        ConfirmSalesOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.SalesOrderId, cancellationToken);

        if (order is null)
            return new NotFoundError("SalesOrder", command.SalesOrderId);

        var transition = order.Confirm(clock.UtcNow);
        if (transition.IsFailure)
            return transition.Error;

        // One reservation per line. Any refusal returns immediately; the ambient
        // transaction rolls back every reservation already applied, so confirm is
        // all-or-nothing.
        foreach (var line in order.Lines)
        {
            var reservation = await mutator.ApplyAsync(
                new StockMutationRequest(
                    line.ProductId,
                    order.WarehouseId,
                    DerivedIdempotencyKey.For(command.IdempotencyKey, "line", line.Id),
                    ReferenceType: nameof(SalesOrder),
                    ReferenceId: order.Id,
                    Reason: null,
                    Operation: $"so-confirm:reserve:{line.QuantityOrdered}"),
                write: (writer, ct) =>
                    writer.TryReserveAsync(line.ProductId, order.WarehouseId, line.QuantityOrdered, ct),
                buildMovement: (context, write) =>
                    StockMovement.ReservationHold(
                        context, line.QuantityOrdered, write.OnHandAfter, write.ReservedAfter),
                onRefused: held =>
                    new InsufficientStockError(line.QuantityOrdered, held.Available),
                cancellationToken);

            if (reservation.IsFailure)
                return reservation.Error;
        }

        // Last, so an early return above never broadcasts a status the database never reached.
        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(SalesOrder), order.Status.ToString()));

        return SalesOrderDto.From(order);
    }
}
