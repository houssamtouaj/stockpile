using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Suppliers.Queries.SearchSuppliers;

public sealed class SearchSuppliersHandler(IAppDbContext db)
    : IRequestHandler<SearchSuppliersQuery, Result<PagedList<SupplierDto>>>
{
    private const int MaxSize = 200;

    public async Task<Result<PagedList<SupplierDto>>> Handle(
        SearchSuppliersQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var size = Math.Clamp(query.Size, 1, MaxSize);

        var source = db.Suppliers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // UPPER(...) LIKE rather than Postgres's ILike: the match still happens in
            // SQL, but without pulling the Npgsql provider into Application (see the
            // dependency-rule tests). Code is persisted already uppercased.
            var term = $"%{query.Search.Trim().ToUpperInvariant()}%";
            source = source.Where(s =>
                EF.Functions.Like(s.Name.ToUpper(), term) || EF.Functions.Like(s.Code, term));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(s => s.Code)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(s => new SupplierDto(
                s.Id, s.Code, s.Name, s.Email, s.Phone, s.Address, s.IsActive, s.RowVersion))
            .ToListAsync(cancellationToken);

        return new PagedList<SupplierDto>(items, page, size, totalCount);
    }
}
