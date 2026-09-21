using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Products.Queries.GetProduct;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Products.Queries.GetProductByBarcode;

/// <summary>
/// §8 calls this the scanner's fast path, so it must be a single indexed lookup against
/// the filtered barcode index built in phase 01 — never a scan.
/// </summary>
public sealed class GetProductByBarcodeHandler(IAppDbContext db)
    : IRequestHandler<GetProductByBarcodeQuery, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(
        GetProductByBarcodeQuery query, CancellationToken cancellationToken)
    {
        var barcode = query.Barcode.Trim();

        var row = await db.Products
            .AsNoTracking()
            .Where(p => p.Barcode == barcode)
            .Select(p => new
            {
                p.Id, p.Sku, p.Name, p.Description, p.Category,
                p.UnitPriceCents, p.Barcode, p.ReorderPoint, p.ReorderQuantity,
                p.IsActive, p.RowVersion
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? new NotFoundError("Product", Guid.Empty)
            : new ProductDto(
                row.Id, row.Sku.Value, row.Name, row.Description, row.Category,
                row.UnitPriceCents, row.Barcode, row.ReorderPoint, row.ReorderQuantity,
                row.IsActive, row.RowVersion);
    }
}
