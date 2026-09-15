using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Stockpile.Domain.Entities;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
[Trait("Category", "Smoke")]
public class DatabaseSmokeTests(StockpileApiFactory factory)
{
    [Fact]
    public async Task Migrations_apply_andTheSchemaIsReachable()
    {
        var ct = TestContext.Current.CancellationToken;

        await factory.ResetDatabaseAsync();
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Products.CountAsync(ct)).ShouldBe(0);
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task StockNeverNegativeConstraint_rejectsANegativeOnHandQuantity()
    {
        var ct = TestContext.Current.CancellationToken;

        await factory.ResetDatabaseAsync();
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var warehouse = Warehouse.CreatePhysical("T-01", "Test", null).Value;
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(ct);

        // Raw SQL surfaces the provider exception directly; EF only wraps failures in a
        // DbUpdateException on the SaveChanges path. Asserting the SQLSTATE and the
        // constraint name proves THIS constraint fired, not merely that something failed.
        var exception = await Should.ThrowAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO stock_items
                    (id, product_id, warehouse_id, quantity_on_hand,
                     quantity_reserved, average_unit_cost_cents)
                VALUES (gen_random_uuid(), gen_random_uuid(), {0}, -1, 0, 0);
                """, [warehouse.Id], ct));

        exception.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.ShouldBe("stock_never_negative");
    }
}
