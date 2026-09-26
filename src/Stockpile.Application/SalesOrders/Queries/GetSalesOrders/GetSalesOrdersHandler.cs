using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;

namespace Stockpile.Application.SalesOrders.Queries.GetSalesOrders;

public sealed class GetSalesOrdersHandler(IAppDbContext db)
    : IRequestHandler<GetSalesOrdersQuery, Result<PagedList<SalesOrderSummaryDto>>>
{
    private const int MaxSize = 200;

    public async Task<Result<PagedList<SalesOrderSummaryDto>>> Handle(
        GetSalesOrdersQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var size = Math.Clamp(query.Size, 1, MaxSize);

        var source = db.SalesOrders.AsNoTracking();

        if (query.WarehouseId is { } warehouseId)
            source = source.Where(o => o.WarehouseId == warehouseId);

        if (query.Status is { } status)
            source = source.Where(o => o.Status == status);

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            // Newest first; Id breaks ties so paging is stable.
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(o => new SalesOrderSummaryDto(
                o.Id, o.Number, o.CustomerId, o.WarehouseId, o.Status, o.CreatedAt,
                o.Lines.Count, o.Lines.Sum(l => l.QuantityOrdered)))
            .ToListAsync(cancellationToken);

        return new PagedList<SalesOrderSummaryDto>(items, page, size, totalCount);
    }
}
