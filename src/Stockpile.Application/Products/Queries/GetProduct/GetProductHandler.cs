using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Products.Queries.GetProduct;

public sealed class GetProductHandler(IAppDbContext db)
    : IRequestHandler<GetProductQuery, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(
        GetProductQuery query, CancellationToken cancellationToken)
    {
        // Sku is a value-converted property: EF reads the column and materialises the
        // value object, but it cannot translate `p.Sku.Value` inside the projection. So
        // select the property itself — still one column in the SELECT list — and unwrap
        // it once the row is in memory.
        var row = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == query.Id)
            .Select(p => new
            {
                p.Id, p.Sku, p.Name, p.Description, p.Category,
                p.UnitPriceCents, p.Barcode, p.ReorderPoint, p.ReorderQuantity, p.IsActive,
                p.RowVersion
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? new NotFoundError("Product", query.Id)
            : new ProductDto(
                row.Id, row.Sku.Value, row.Name, row.Description, row.Category,
                row.UnitPriceCents, row.Barcode, row.ReorderPoint, row.ReorderQuantity, row.IsActive,
                row.RowVersion);
    }
}
