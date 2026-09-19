using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.Warehouses.Queries.GetWarehouses;

public sealed record WarehouseDto(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    WarehouseKind Kind,
    bool IsActive)
{
    public static WarehouseDto From(Warehouse w) =>
        new(w.Id, w.Code, w.Name, w.Address, w.Kind, w.IsActive);
}
