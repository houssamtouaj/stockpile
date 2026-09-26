using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;

public sealed record GetPurchaseOrderQuery(Guid Id) : IQuery<Result<PurchaseOrderDto>>;

public sealed class GetPurchaseOrderHandler(IAppDbContext db)
    : IRequestHandler<GetPurchaseOrderQuery, Result<PurchaseOrderDto>>
{
    public async Task<Result<PurchaseOrderDto>> Handle(
        GetPurchaseOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == query.Id, cancellationToken);

        return order is null
            ? new NotFoundError("PurchaseOrder", query.Id)
            : PurchaseOrderDto.From(order);
    }
}
