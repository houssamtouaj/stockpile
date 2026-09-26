using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.PurchaseOrders.Commands.ReceivePurchaseOrder;

public sealed class ReceivePurchaseOrderHandler(
    IAppDbContext db,
    IStockWriter writer,
    IStockMutator mutator,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<ReceivePurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> Handle(
        ReceivePurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == command.PurchaseOrderId, cancellationToken);

        if (order is null)
            return new NotFoundError("PurchaseOrder", command.PurchaseOrderId);

        var line = order.Lines.SingleOrDefault(l => l.Id == command.LineId);
        if (line is null)
            return new NotFoundError("PurchaseOrderLine", command.LineId);

        // A product may never have been stocked at this warehouse before.
        await writer.EnsureStockItemAsync(line.ProductId, order.WarehouseId, cancellationToken);

        // The stock write runs first so its replay check is the only one. Unlike every other
        // order transition, a receipt is repeatable, so the status guard cannot stop a retry:
        // were ReceiveLine to run on a replay, it would count the units against the line a
        // second time — on-hand right, the order's receipt count wrong, and the honest retry
        // of a final delivery refused as exceeding the outstanding quantity. So the domain
        // step runs only for a fresh receipt, and if it refuses, the failed Result rolls
        // this stock write back with it.
        var mutation = await mutator.ApplyAsync(
            new StockMutationRequest(
                line.ProductId,
                order.WarehouseId,
                command.IdempotencyKey,
                ReferenceType: nameof(PurchaseOrder),
                ReferenceId: order.Id,
                Reason: null,
                Operation: $"po-receive:{line.Id}:{command.Quantity}"),
            write: (w, ct) => w.TryReceiveAsync(
                line.ProductId, order.WarehouseId, command.Quantity, line.UnitCostCents, ct),
            // The ledger records what THIS delivery cost; the stock row's average becomes
            // the blended figure. History and current basis are both needed.
            buildMovement: (context, write) => StockMovement.Receipt(
                context, command.Quantity, line.UnitCostCents, write.OnHandAfter, write.ReservedAfter),
            onRefused: _ => new DomainRuleError(
                "po.receipt_failed", "The receipt could not be applied to stock."),
            cancellationToken);

        if (mutation.IsFailure)
            return mutation.Error;

        if (mutation.Value.WasReplay)
            return PurchaseOrderDto.From(order);

        var received = order.ReceiveLine(command.LineId, command.Quantity, clock.UtcNow);
        if (received.IsFailure)
            return received.Error;

        // A partial receipt on an order already PartiallyReceived changes only the line,
        // which has no concurrency token. Forcing the order row into the UPDATE puts its
        // xmin check on every receipt, so of two concurrent receipts the loser gets a
        // 409 — and its stock write rolls back — instead of silently losing a count.
        db.Entry(order).Property(o => o.Status).IsModified = true;

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(PurchaseOrder), order.Status.ToString()));

        return PurchaseOrderDto.From(order);
    }
}
