using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Pagination;
using Stockpile.Application.Common.Querying;
using Stockpile.Application.Products.Queries.GetProduct;
using Stockpile.Domain.Common;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Application.Products.Queries.SearchProducts;

public sealed class SearchProductsHandler(IAppDbContext db)
    : IRequestHandler<SearchProductsQuery, Result<PagedList<ProductDto>>>
{
    private const int MaxSize = 200;

    public async Task<Result<PagedList<ProductDto>>> Handle(
        SearchProductsQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var size = Math.Clamp(query.Size, 1, MaxSize);

        var source = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var raw = query.Search.Trim();

            // Uppercased on both sides so the match is case-insensitive in SQL. ILike is
            // the idiomatic Postgres spelling, but it is an Npgsql extension and the
            // dependency rule keeps the provider out of Application. UPPER(...) LIKE still
            // runs in the database; ToUpper().Contains() in C# would not.
            var pattern = LikePattern.Contains(raw.ToUpperInvariant());

            // Sku is a value-converted property, so EF coerces anything compared against
            // that column through the Sku converter — which a '%term%' pattern cannot
            // survive. Partial SKU matching would therefore need the column mapped as a
            // plain string, and phase 01 deliberately mapped it as a value object. So the
            // SKU arm is an exact match on a normalised term (the scanner's partial-code
            // case is served by the dedicated /by-barcode lookup), and the LIKE stays on
            // the columns that are plain strings.
            var exactSku = Sku.Create(raw);

            source = exactSku.IsSuccess
                ? source.Where(p =>
                    EF.Functions.Like(p.Name.ToUpper(), pattern, LikePattern.EscapeCharacter)
                    || p.Sku == exactSku.Value)
                : source.Where(p =>
                    EF.Functions.Like(p.Name.ToUpper(), pattern, LikePattern.EscapeCharacter));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
            source = source.Where(p => p.Category == query.Category);

        var totalCount = await source.CountAsync(cancellationToken);

        var rows = await source
            // Id breaks the tie. Without it two products sharing a name have no defined
            // order between pages, so one can repeat on page 2 while another is never shown.
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(p => new
            {
                p.Id, p.Sku, p.Name, p.Description, p.Category,
                p.UnitPriceCents, p.Barcode, p.ReorderPoint, p.ReorderQuantity,
                p.IsActive, p.RowVersion
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new ProductDto(
            r.Id, r.Sku.Value, r.Name, r.Description, r.Category,
            r.UnitPriceCents, r.Barcode, r.ReorderPoint, r.ReorderQuantity,
            r.IsActive, r.RowVersion)).ToList();

        return new PagedList<ProductDto>(items, page, size, totalCount);
    }
}
