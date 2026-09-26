using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.SalesOrders.Queries.GetOrderBoard;

/// <summary>
/// The kanban read model phase 04 pushes updates into: every open sales order, grouped by
/// status, in one round trip.
/// </summary>
public sealed class GetOrderBoardHandler(IAppDbContext db)
    : IRequestHandler<GetOrderBoardQuery, Result<OrderBoardDto>>
{
    /// <summary>Board columns, left to right. Shipped and Cancelled are terminal: a board of
    /// finished orders is a report, not a board.</summary>
    private static readonly SalesOrderStatus[] Columns =
    [
        SalesOrderStatus.Draft,
        SalesOrderStatus.Confirmed,
        SalesOrderStatus.Picking,
        SalesOrderStatus.Packed
    ];

    public async Task<Result<OrderBoardDto>> Handle(
        GetOrderBoardQuery query, CancellationToken cancellationToken)
    {
        var warehouseId = query.WarehouseId;

        var rows = await db.SalesOrders
            .AsNoTracking()
            .Where(o => Columns.Contains(o.Status))
            .Where(o => warehouseId == null || o.WarehouseId == warehouseId)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Select(o => new
            {
                o.Id,
                o.Number,
                CustomerName = db.Customers.Where(c => c.Id == o.CustomerId).Select(c => c.Name).First(),
                LineCount = o.Lines.Count,
                TotalUnits = o.Lines.Sum(l => l.QuantityOrdered),
                o.CreatedAt,
                o.Status
            })
            .ToListAsync(cancellationToken);

        // Grouped in memory on purpose: the board is bounded by the number of OPEN orders,
        // which is small, and one query with client-side grouping beats four round trips.
        // If open orders ever run to thousands, the board is the wrong UI, not the wrong query.
        var byStatus = rows.ToLookup(r => r.Status);

        var columns = Columns.ToDictionary(
            status => status.ToString(),
            status => (IReadOnlyList<OrderCardDto>)byStatus[status]
                .Select(r => new OrderCardDto(
                    r.Id, r.Number, r.CustomerName, r.LineCount, r.TotalUnits, r.CreatedAt, r.Status.ToString()))
                .ToList());

        return new OrderBoardDto(columns);
    }
}
