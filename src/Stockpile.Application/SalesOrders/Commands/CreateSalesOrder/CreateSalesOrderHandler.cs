using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Orders;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.SalesOrders.Commands.CreateSalesOrder;

public sealed class CreateSalesOrderHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numbers,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<CreateSalesOrderCommand, Result<SalesOrderDto>>
{
    public async Task<Result<SalesOrderDto>> Handle(
        CreateSalesOrderCommand command, CancellationToken cancellationToken)
    {
        var customerIsActive = await db.Customers
            .Where(c => c.Id == command.CustomerId)
            .Select(c => (bool?)c.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (customerIsActive is null)
            return new NotFoundError("Customer", command.CustomerId);

        if (customerIsActive is false)
            return new DomainRuleError("so.customer_inactive", "This customer is inactive and cannot be sold to.");

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == command.WarehouseId, cancellationToken);

        if (warehouse is null)
            return new NotFoundError("Warehouse", command.WarehouseId);

        if (!warehouse.IsActive)
            return new DomainRuleError("so.warehouse_inactive", "This warehouse is inactive.");

        // Nobody can pick from a truck: in-transit stock belongs to a transfer.
        if (warehouse.Kind != WarehouseKind.Physical)
            return new DomainRuleError(
                "so.warehouse_not_physical", "A sales order must ship from a physical warehouse.");

        var lines = command.Lines.Select(l => (l.ProductId, l.Quantity, l.UnitPriceCents)).ToList();

        var validLines = SalesOrder.ValidateLines(lines);
        if (validLines.IsFailure)
            return validLines.Error;

        if (await OrderProducts.CheckAsync(db, command.Lines.Select(l => l.ProductId), "so", cancellationToken)
            is { } productRefused)
            return productRefused;

        // Numbered last, after every check that could refuse: nextval() is not rolled back,
        // so numbering first would burn a number on every rejected request.
        var number = await numbers.NextAsync("SO", cancellationToken);

        var order = SalesOrder.Create(number, command.CustomerId, command.WarehouseId, clock.UtcNow, lines).Value;
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(SalesOrder), order.Status.ToString()));

        return SalesOrderDto.From(order);
    }
}
