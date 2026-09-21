using Stockpile.Domain.Entities;

namespace Stockpile.Application.Products.Queries.GetProduct;

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    string Category,
    long UnitPriceCents,
    string? Barcode,
    int ReorderPoint,
    int ReorderQuantity,
    bool IsActive,
    uint RowVersion)
{
    public static ProductDto From(Product p) => new(
        p.Id, p.Sku.Value, p.Name, p.Description, p.Category,
        p.UnitPriceCents, p.Barcode, p.ReorderPoint, p.ReorderQuantity, p.IsActive, p.RowVersion);
}
