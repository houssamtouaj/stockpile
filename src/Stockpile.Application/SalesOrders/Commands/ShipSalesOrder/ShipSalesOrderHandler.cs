using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.SalesOrders.Commands.ShipSalesOrder;

public sealed class ShipSalesOrderHandler(
    IAppDbContext db,
    IStockWriter writer,
    IStockMutator mutator,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<ShipSalesOrderCommand, Result<SalesOrderDto>>
{
    public async Task<Result<SalesOrderDto>> Handle(
        ShipSalesOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.SalesOrderId, cancellationToken);

        if (order is null)
            return new NotFoundError("SalesOrder", command.SalesOrderId);

        var transition = order.Ship(clock.UtcNow);
        if (transition.IsFailure)
            return transition.Error;

        // Every row this command writes, locked in one global order before the first write,
        // so two orders sharing products in opposite line order cannot deadlock.
        await writer.LockRowsAsync(
            order.Lines.Select(l => (l.ProductId, order.WarehouseId)).ToList(), cancellationToken);

        // Shipping consumes the reservation and the on-hand stock together, so each Issue
        // movement carries BOTH deltas negative.
        foreach (var line in order.Lines)
        {
            var issue = await mutator.ApplyAsync(
                new StockMutationRequest(
                    line.ProductId,
                    order.WarehouseId,
                    DerivedIdempotencyKey.For(command.IdempotencyKey, DerivedIdempotencyKey.ShipLine, line.Id),
                    ReferenceType: nameof(SalesOrder),
                    ReferenceId: order.Id,
                    Reason: null,
                    Operation: $"so-ship:issue:{line.QuantityOrdered}"),
                write: (writer, ct) =>
                    writer.TryIssueAsync(line.ProductId, order.WarehouseId, line.QuantityOrdered, ct),
                buildMovement: (context, write) =>
                    StockMovement.Issue(context, line.QuantityOrdered, write.OnHandAfter, write.ReservedAfter),
                // The order held this reservation since confirm, so a refusal here means
                // something outside the order released or consumed it.
                onRefused: held => new DomainRuleError(
                    "so.issue_failed",
                    $"Cannot ship line {line.Id}: {line.QuantityOrdered} unit(s) are needed but only "
                    + $"{held.Reserved} are reserved and {held.OnHand} on hand."),
                cancellationToken);

            if (issue.IsFailure)
                return issue.Error;
        }

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(SalesOrder), order.Status.ToString()));

        return SalesOrderDto.From(order);
    }
}
