using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Common.Orders;

/// <summary>
/// The product check every order and transfer create makes on its lines, in one query.
/// </summary>
internal static class OrderProducts
{
    /// <summary>The first requested product that does not exist, as a 404; null when all do.</summary>
    public static async Task<Error?> CheckAsync(
        IAppDbContext db, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var requested = productIds.Distinct().ToList();

        var known = await db.Products
            .Where(p => requested.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        return requested.Except(known).Cast<Guid?>().FirstOrDefault() is { } missing
            ? new NotFoundError("Product", missing)
            : null;
    }
}
