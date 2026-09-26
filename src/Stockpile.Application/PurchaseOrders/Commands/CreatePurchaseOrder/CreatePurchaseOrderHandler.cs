using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Orders;
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
        var supplierIsActive = await db.Suppliers
            .Where(s => s.Id == command.SupplierId)
            .Select(s => (bool?)s.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (supplierIsActive is null)
            return new NotFoundError("Supplier", command.SupplierId);

        if (supplierIsActive is false)
            return new DomainRuleError("po.supplier_inactive", "This supplier is inactive and cannot be ordered from.");

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == command.WarehouseId, cancellationToken);

        if (warehouse is null)
            return new NotFoundError("Warehouse", command.WarehouseId);

        if (!warehouse.IsActive)
            return new DomainRuleError("po.warehouse_inactive", "This warehouse is inactive.");

        // Suppliers deliver to a dock, not to a truck already on the road.
        if (warehouse.Kind != WarehouseKind.Physical)
            return new DomainRuleError(
                "po.warehouse_not_physical", "A purchase order must be delivered to a physical warehouse.");

        var lines = command.Lines.Select(l => (l.ProductId, l.Quantity, l.UnitCostCents)).ToList();

        var validLines = PurchaseOrder.ValidateLines(lines);
        if (validLines.IsFailure)
            return validLines.Error;

        if (await OrderProducts.CheckAsync(db, command.Lines.Select(l => l.ProductId), "po", cancellationToken)
            is { } productRefused)
            return productRefused;

        // Numbered after every check that could refuse: nextval() is not rolled back.
        var number = await numbers.NextAsync("PO", cancellationToken);

        var order = PurchaseOrder.Create(
            number, command.SupplierId, command.WarehouseId, command.ExpectedAt, clock.UtcNow, lines).Value;
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(PurchaseOrder), order.Status.ToString()));

        return PurchaseOrderDto.From(order);
    }
}
