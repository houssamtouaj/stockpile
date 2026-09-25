using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Customer> Customers { get; }
    DbSet<StockItem> StockItems { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<ApplicationUser> Users { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<StockTransfer> StockTransfers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Needed by the edit-style aggregates only, to pin RowVersion's OriginalValue to the
    /// version the client last saw so EF puts `xmin = @original` in the UPDATE's WHERE
    /// clause. Nothing on the stock-mutation path uses this: StockItem has no token.
    /// </summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
}
