using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Customers.Queries.SearchCustomers;

public sealed class SearchCustomersHandler(IAppDbContext db)
    : IRequestHandler<SearchCustomersQuery, Result<PagedList<CustomerDto>>>
{
    private const int MaxSize = 200;

    public async Task<Result<PagedList<CustomerDto>>> Handle(
        SearchCustomersQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var size = Math.Clamp(query.Size, 1, MaxSize);

        var source = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // UPPER(...) LIKE rather than Postgres's ILike: the match still happens in
            // SQL, but without pulling the Npgsql provider into Application.
            var term = $"%{query.Search.Trim().ToUpperInvariant()}%";
            source = source.Where(c =>
                EF.Functions.Like(c.Name.ToUpper(), term) ||
                (c.Email != null && EF.Functions.Like(c.Email.ToUpper(), term)));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(c => c.Name)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(c => new CustomerDto(
                c.Id, c.Name, c.Email, c.Phone, c.ShippingAddress, c.IsActive, c.RowVersion))
            .ToListAsync(cancellationToken);

        return new PagedList<CustomerDto>(items, page, size, totalCount);
    }
}
