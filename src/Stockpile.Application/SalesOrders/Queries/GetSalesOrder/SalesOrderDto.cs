using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

public sealed record SalesOrderLineDto(
    Guid Id,
    Guid ProductId,
    int QuantityOrdered,
    int QuantityPicked,
    long UnitPriceCents);

public sealed record SalesOrderDto(
    Guid Id,
    string Number,
    Guid CustomerId,
    Guid WarehouseId,
    SalesOrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ShippedAt,
    IReadOnlyList<SalesOrderLineDto> Lines)
{
    public static SalesOrderDto From(SalesOrder o) =>
        new(o.Id, o.Number, o.CustomerId, o.WarehouseId, o.Status, o.CreatedAt, o.ShippedAt,
            o.Lines
                .Select(l => new SalesOrderLineDto(
                    l.Id, l.ProductId, l.QuantityOrdered, l.QuantityPicked, l.UnitPriceCents))
                .ToList());
}
