using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Warehouses.Queries.GetWarehouses;

public sealed class GetWarehousesHandler(IAppDbContext db)
    : IRequestHandler<GetWarehousesQuery, Result<IReadOnlyList<WarehouseDto>>>
{
    public async Task<Result<IReadOnlyList<WarehouseDto>>> Handle(
        GetWarehousesQuery query, CancellationToken cancellationToken)
    {
        var warehouses = await db.Warehouses
            .AsNoTracking()
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseDto(w.Id, w.Code, w.Name, w.Address, w.Kind, w.IsActive))
            .ToListAsync(cancellationToken);

        return warehouses;
    }
}
