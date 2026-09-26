using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.Stock.Commands;

/// <summary>
/// Manual stock commands act on physical warehouses only. Units in the in-transit warehouse
/// belong to transfers: a reservation or adjustment there leaves a later receipt unable to
/// take its units out, and the transfer is stranded InTransit with no way to cancel it. Only
/// the transfer legs, which bypass these commands, may move them.
/// <para>
/// A warehouse that does not exist passes: the mutation then reports the missing stock row
/// as a 404, as it always has.
/// </para>
/// </summary>
internal static class PhysicalWarehouseGuard
{
    public static async Task<Error?> RefuseIfNotPhysicalAsync(
        IAppDbContext db, Guid warehouseId, CancellationToken cancellationToken) =>
        await db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.Kind != WarehouseKind.Physical, cancellationToken)
            ? new DomainRuleError(
                "stock.warehouse_not_physical",
                "Stock in an in-transit warehouse belongs to transfers and can only be moved by dispatching or receiving them.")
            : null;
}
