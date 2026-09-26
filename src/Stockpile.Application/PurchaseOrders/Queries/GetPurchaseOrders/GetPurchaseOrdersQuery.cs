using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrders;

public sealed record PurchaseOrderSummaryDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    Guid WarehouseId,
    PurchaseOrderStatus Status,
    DateTimeOffset? ExpectedAt,
    DateTimeOffset CreatedAt,
    int LineCount,
    int UnitsOrdered,
    int UnitsReceived);

public sealed record GetPurchaseOrdersQuery(
    Guid? WarehouseId = null,
    Guid? SupplierId = null,
    PurchaseOrderStatus? Status = null,
    int Page = 1,
    int Size = 25) : IQuery<Result<PagedList<PurchaseOrderSummaryDto>>>;

public sealed class GetPurchaseOrdersHandler(IAppDbContext db)
    : IRequestHandler<GetPurchaseOrdersQuery, Result<PagedList<PurchaseOrderSummaryDto>>>
{
    private const int MaxSize = 200;

    public async Task<Result<PagedList<PurchaseOrderSummaryDto>>> Handle(
        GetPurchaseOrdersQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var size = Math.Clamp(query.Size, 1, MaxSize);

        var source = db.PurchaseOrders.AsNoTracking();

        if (query.WarehouseId is { } warehouseId)
            source = source.Where(o => o.WarehouseId == warehouseId);

        if (query.SupplierId is { } supplierId)
            source = source.Where(o => o.SupplierId == supplierId);

        if (query.Status is { } status)
            source = source.Where(o => o.Status == status);

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(o => new PurchaseOrderSummaryDto(
                o.Id, o.Number, o.SupplierId, o.WarehouseId, o.Status, o.ExpectedAt, o.CreatedAt,
                o.Lines.Count,
                o.Lines.Sum(l => l.QuantityOrdered),
                o.Lines.Sum(l => l.QuantityReceived)))
            .ToListAsync(cancellationToken);

        return new PagedList<PurchaseOrderSummaryDto>(items, page, size, totalCount);
    }
}
