using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Entities;
using Stockpile.Infrastructure.Identity;

namespace Stockpile.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<StockpileIdentityUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    /// <summary>
    /// Hides IdentityDbContext.Users (a DbSet&lt;StockpileIdentityUser&gt;) on purpose. The
    /// two user tables are separate by design — app_users is the domain's, asp_net_users
    /// is Identity's credential store — and IAppDbContext.Users means the domain one.
    /// Identity itself resolves its store through Set&lt;StockpileIdentityUser&gt;(), not
    /// through this property, so hiding it changes nothing for Identity.
    /// </summary>
    public new DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // base FIRST: IdentityDbContext builds its own model here, and calling it after
        // ApplyConfigurationsFromAssembly lets it overwrite your configuration.
        base.OnModelCreating(modelBuilder);

        // UseSnakeCaseNamingConvention rewrites names EF *derives* — columns, keys,
        // indexes — but not names a configuration set explicitly, and IdentityDbContext
        // sets its table names explicitly via ToTable. Without these seven lines Identity
        // lands as AspNetUsers/AspNetRoles/... sitting next to app_users and
        // stock_movements. Verified with \dt, not assumed.
        modelBuilder.Entity<StockpileIdentityUser>().ToTable("asp_net_users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("asp_net_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.Ignore<Stockpile.Domain.Events.DomainEvent>();
    }
}
