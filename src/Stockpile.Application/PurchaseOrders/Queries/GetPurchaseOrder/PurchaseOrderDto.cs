using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;

/// <summary>
/// UnitCostCents is still unconditional here. Projecting it out for roles without
/// CanViewCosts is phase 05 task 1, which changes it to a nullable, omitted-when-null field.
/// </summary>
public sealed record PurchaseOrderLineDto(
    Guid Id,
    Guid ProductId,
    int QuantityOrdered,
    int QuantityReceived,
    long UnitCostCents);

public sealed record PurchaseOrderDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    Guid WarehouseId,
    PurchaseOrderStatus Status,
    DateTimeOffset? ExpectedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PurchaseOrderLineDto> Lines)
{
    public static PurchaseOrderDto From(PurchaseOrder o) =>
        new(o.Id, o.Number, o.SupplierId, o.WarehouseId, o.Status, o.ExpectedAt, o.CreatedAt,
            o.Lines
                .Select(l => new PurchaseOrderLineDto(
                    l.Id, l.ProductId, l.QuantityOrdered, l.QuantityReceived, l.UnitCostCents))
                .ToList());
}
