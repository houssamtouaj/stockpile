using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.PurchaseOrders.Commands.CreatePurchaseOrder;

public sealed class CreatePurchaseOrderHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numbers,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<CreatePurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> Handle(
        CreatePurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == command.SupplierId, cancellationToken))
            return new NotFoundError("Supplier", command.SupplierId);

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == command.WarehouseId, cancellationToken);

        if (warehouse is null)
            return new NotFoundError("Warehouse", command.WarehouseId);

        // Suppliers deliver to a dock, not to a truck already on the road.
        if (warehouse.Kind != WarehouseKind.Physical)
            return new DomainRuleError(
                "po.warehouse_not_physical", "A purchase order must be delivered to a physical warehouse.");

        var requested = command.Lines.Select(l => l.ProductId).Distinct().ToList();
        var known = await db.Products
            .Where(p => requested.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (requested.Except(known).Cast<Guid?>().FirstOrDefault() is { } missing)
            return new NotFoundError("Product", missing);

        // Numbered after every check that could refuse: nextval() is not rolled back.
        var number = await numbers.NextAsync("PO", cancellationToken);

        var created = PurchaseOrder.Create(
            number,
            command.SupplierId,
            command.WarehouseId,
            command.ExpectedAt,
            clock.UtcNow,
            command.Lines.Select(l => (l.ProductId, l.Quantity, l.UnitCostCents)).ToList());

        if (created.IsFailure)
            return created.Error;

        var order = created.Value;
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(PurchaseOrder), order.Status.ToString()));

        return PurchaseOrderDto.From(order);
    }
}
