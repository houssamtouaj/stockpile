using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
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
        if (!await db.Customers.AnyAsync(c => c.Id == command.CustomerId, cancellationToken))
            return new NotFoundError("Customer", command.CustomerId);

        var warehouse = await db.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == command.WarehouseId, cancellationToken);

        if (warehouse is null)
            return new NotFoundError("Warehouse", command.WarehouseId);

        // Nobody can pick from a truck: in-transit stock belongs to a transfer.
        if (warehouse.Kind != WarehouseKind.Physical)
            return new DomainRuleError(
                "so.warehouse_not_physical", "A sales order must ship from a physical warehouse.");

        var missing = await MissingProductAsync(command, cancellationToken);
        if (missing is { } productId)
            return new NotFoundError("Product", productId);

        // Numbered last, after every check that could refuse: nextval() is not rolled back,
        // so numbering first would burn a number on every rejected request.
        var number = await numbers.NextAsync("SO", cancellationToken);

        var created = SalesOrder.Create(
            number,
            command.CustomerId,
            command.WarehouseId,
            clock.UtcNow,
            command.Lines.Select(l => (l.ProductId, l.Quantity, l.UnitPriceCents)).ToList());

        if (created.IsFailure)
            return created.Error;

        var order = created.Value;
        db.SalesOrders.Add(order);

        // Flushed so Postgres assigns xmin and the response carries a usable RowVersion.
        await db.SaveChangesAsync(cancellationToken);

        notifications.Enqueue(new OrderStatusChangedNotification(
            order.WarehouseId, order.Id, nameof(SalesOrder), order.Status.ToString()));

        return SalesOrderDto.From(order);
    }

    private async Task<Guid?> MissingProductAsync(
        CreateSalesOrderCommand command, CancellationToken cancellationToken)
    {
        var requested = command.Lines.Select(l => l.ProductId).Distinct().ToList();

        var known = await db.Products
            .Where(p => requested.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        return requested.Except(known).Cast<Guid?>().FirstOrDefault();
    }
}
