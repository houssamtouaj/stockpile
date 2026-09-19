using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Stock.Queries.GetMovements;

public sealed class GetMovementsHandler(IAppDbContext db)
    : IRequestHandler<GetMovementsQuery, Result<CursorPage<MovementDto>>>
{
    private const int MaxSize = 200;

    public async Task<Result<CursorPage<MovementDto>>> Handle(
        GetMovementsQuery query, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(query.Size, 1, MaxSize);

        var source = db.StockMovements.AsNoTracking();

        if (query.ProductId is { } productId)
            source = source.Where(m => m.ProductId == productId);

        if (query.WarehouseId is { } warehouseId)
            source = source.Where(m => m.WarehouseId == warehouseId);

        if (query.From is { } from)
            source = source.Where(m => m.OccurredAt >= from);

        if (query.To is { } to)
            source = source.Where(m => m.OccurredAt <= to);

        if (query.Type is { } type)
            source = source.Where(m => m.Type == type);

        // Keyset: strictly "older than the last row we returned", tie-broken by id so the
        // ordering is total even when two movements share a timestamp to the microsecond.
        if (MovementCursor.TryDecode(query.Cursor) is { } cursor)
        {
            source = source.Where(m =>
                m.OccurredAt < cursor.OccurredAt ||
                (m.OccurredAt == cursor.OccurredAt && m.Id.CompareTo(cursor.Id) < 0));
        }

        var rows = await source
            .OrderByDescending(m => m.OccurredAt)
            .ThenByDescending(m => m.Id)
            .Take(size + 1)                       // one extra tells us whether more exist
            .Select(m => new MovementDto(
                m.Id, m.ProductId, m.WarehouseId, m.Type,
                m.OnHandDelta, m.ReservedDelta, m.OnHandAfter, m.ReservedAfter,
                m.ReferenceType, m.ReferenceId, m.Reason,
                m.OccurredAt, m.PerformedByUserId))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > size;
        var items = hasMore ? rows[..size] : rows;

        var nextCursor = hasMore && items.Count > 0
            ? new MovementCursor(items[^1].OccurredAt, items[^1].Id).Encode()
            : null;

        return new CursorPage<MovementDto>(items, nextCursor, hasMore);
    }
}
