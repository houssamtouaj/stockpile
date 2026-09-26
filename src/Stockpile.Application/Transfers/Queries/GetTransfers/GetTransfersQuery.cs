using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.Transfers.Queries.GetTransfers;

public sealed record StockTransferSummaryDto(
    Guid Id,
    string Number,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    TransferStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt,
    int LineCount,
    int TotalUnits);

/// <summary><see cref="WarehouseId"/> matches either end, so a warehouse sees both what it
/// is sending and what it is expecting.</summary>
public sealed record GetTransfersQuery(
    Guid? WarehouseId = null,
    TransferStatus? Status = null,
    int Page = 1,
    int Size = 25) : IQuery<Result<PagedList<StockTransferSummaryDto>>>;

public sealed class GetTransfersHandler(IAppDbContext db)
    : IRequestHandler<GetTransfersQuery, Result<PagedList<StockTransferSummaryDto>>>
{
    private const int MaxSize = 200;

    public async Task<Result<PagedList<StockTransferSummaryDto>>> Handle(
        GetTransfersQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var size = Math.Clamp(query.Size, 1, MaxSize);

        var source = db.StockTransfers.AsNoTracking();

        if (query.WarehouseId is { } warehouseId)
            source = source.Where(t => t.FromWarehouseId == warehouseId || t.ToWarehouseId == warehouseId);

        if (query.Status is { } status)
            source = source.Where(t => t.Status == status);

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(t => new StockTransferSummaryDto(
                t.Id, t.Number, t.FromWarehouseId, t.ToWarehouseId, t.Status, t.CreatedAt, t.DispatchedAt,
                t.Lines.Count, t.Lines.Sum(l => l.Quantity)))
            .ToListAsync(cancellationToken);

        return new PagedList<StockTransferSummaryDto>(items, page, size, totalCount);
    }
}
