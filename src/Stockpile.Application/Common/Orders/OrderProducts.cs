using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Common.Orders;

/// <summary>
/// The product checks every order and transfer create makes on its lines, in one query.
/// </summary>
internal static class OrderProducts
{
    /// <summary>
    /// The first requested product that does not exist, as a 404; else the first that is
    /// deactivated, as <c>{rulePrefix}.product_inactive</c>; null when every product may be
    /// put on a new document. Deactivated products stay on existing ones.
    /// </summary>
    public static async Task<Error?> CheckAsync(
        IAppDbContext db, IEnumerable<Guid> productIds, string rulePrefix, CancellationToken cancellationToken)
    {
        var requested = productIds.Distinct().ToList();

        var found = await db.Products
            .Where(p => requested.Contains(p.Id))
            .Select(p => new { p.Id, p.IsActive })
            .ToListAsync(cancellationToken);

        if (requested.Except(found.Select(p => p.Id)).Cast<Guid?>().FirstOrDefault() is { } missing)
            return new NotFoundError("Product", missing);

        if (found.FirstOrDefault(p => !p.IsActive) is { } inactive)
            return new DomainRuleError(
                $"{rulePrefix}.product_inactive", $"Product '{inactive.Id}' is inactive and cannot be added.");

        return null;
    }
}
