using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

public sealed class GetSalesOrderHandler(IAppDbContext db)
    : IRequestHandler<GetSalesOrderQuery, Result<SalesOrderDto>>
{
    public async Task<Result<SalesOrderDto>> Handle(
        GetSalesOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == query.Id, cancellationToken);

        return order is null
            ? new NotFoundError("SalesOrder", query.Id)
            : SalesOrderDto.From(order);
    }
}
