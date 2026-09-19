using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Stockpile.Domain.Enums;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests.Stock;

[Collection(nameof(ApiCollection))]
public class ReconciliationTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnAFreshDatabase_reportsZeroDiscrepancies()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var report = await (await client.PostAsync("/api/stock/reconcile", null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task AfterNormalOperations_reportsZeroDiscrepancies()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("REC-001");
        var warehouseId = await factory.SeedWarehouseAsync("REC-WH");
        await factory.SeedStockAsync(productId, warehouseId, 100);

        var ops = await factory.CreateClientAs(Role.Operator);
        var manager = await factory.CreateClientAs(Role.WarehouseManager);

        await ops.PostAsJsonAsync("/api/stock/reserve",
            new { productId, warehouseId, quantity = 10, idempotencyKey = $"r1-{Guid.CreateVersion7()}" }, ct);
        await ops.PostAsJsonAsync("/api/stock/release",
            new { productId, warehouseId, quantity = 4, idempotencyKey = $"r2-{Guid.CreateVersion7()}" }, ct);
        await manager.PostAsJsonAsync("/api/stock/adjust",
            new { productId, warehouseId, onHandDelta = -7, reason = "Damage",
                  idempotencyKey = $"a1-{Guid.CreateVersion7()}" }, ct);
        await manager.PostAsJsonAsync("/api/stock/count",
            new { productId, warehouseId, observedOnHand = 90, reason = "Count",
                  idempotencyKey = $"c1-{Guid.CreateVersion7()}" }, ct);

        var report = await (await manager.PostAsync("/api/stock/reconcile", null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(0);
        report.GetProperty("rowsChecked").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task AfterConcurrentLoad_reportsZeroDiscrepancies()
    {
        // The §14 item. Run the race, then prove the ledger still explains the snapshot.
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("REC-002");
        var warehouseId = await factory.SeedWarehouseAsync("REC-WH2");
        await factory.SeedStockAsync(productId, warehouseId, 10);

        var ops = await factory.CreateClientAs(Role.Operator);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i =>
            ops.PostAsJsonAsync("/api/stock/reserve", new
            {
                productId, warehouseId, quantity = 1,
                idempotencyKey = $"rec-race-{i}-{Guid.CreateVersion7()}"
            }, ct)));

        var manager = await factory.CreateClientAs(Role.WarehouseManager);
        var report = await (await manager.PostAsync("/api/stock/reconcile", null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task WhenTheSnapshotIsTamperedWith_theDiscrepancyIsDetected()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("REC-003");
        var warehouseId = await factory.SeedWarehouseAsync("REC-WH3");
        await factory.SeedStockAsync(productId, warehouseId, 50);

        // Corrupt the cache behind the application's back. The ledger is untouched, so
        // the ledger must be able to prove the snapshot wrong.
        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE stock_items SET quantity_on_hand = 999 WHERE product_id = {0};", [productId], ct);
        }

        var manager = await factory.CreateClientAs(Role.WarehouseManager);
        var report = await (await manager.PostAsync("/api/stock/reconcile", null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(1);
        var discrepancy = report.GetProperty("discrepancies").EnumerateArray().Single();
        discrepancy.GetProperty("snapshotOnHand").GetInt32().ShouldBe(999);
        discrepancy.GetProperty("ledgerOnHand").GetInt32().ShouldBe(50);
    }

    [Fact]
    public async Task BothDeltasAreReplayed_notJustOnHand()
    {
        // A single-delta ledger would pass an on-hand check and silently miss this.
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("REC-004");
        var warehouseId = await factory.SeedWarehouseAsync("REC-WH4");
        await factory.SeedStockAsync(productId, warehouseId, 50);

        var ops = await factory.CreateClientAs(Role.Operator);
        await ops.PostAsJsonAsync("/api/stock/reserve",
            new { productId, warehouseId, quantity = 20, idempotencyKey = $"br-{Guid.CreateVersion7()}" }, ct);

        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // On-hand is left correct; only reserved is corrupted.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE stock_items SET quantity_reserved = 3 WHERE product_id = {0};", [productId], ct);
        }

        var manager = await factory.CreateClientAs(Role.WarehouseManager);
        var report = await (await manager.PostAsync("/api/stock/reconcile", null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(1);
        var discrepancy = report.GetProperty("discrepancies").EnumerateArray().Single();
        discrepancy.GetProperty("snapshotOnHand").GetInt32().ShouldBe(50);
        discrepancy.GetProperty("ledgerOnHand").GetInt32().ShouldBe(50);
        discrepancy.GetProperty("snapshotReserved").GetInt32().ShouldBe(3);
        discrepancy.GetProperty("ledgerReserved").GetInt32().ShouldBe(20);
    }

    [Fact]
    public async Task WithRepair_theSnapshotIsRewrittenFromTheLedger()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("REC-005");
        var warehouseId = await factory.SeedWarehouseAsync("REC-WH5");
        await factory.SeedStockAsync(productId, warehouseId, 50);

        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE stock_items SET quantity_on_hand = 7 WHERE product_id = {0};", [productId], ct);
        }

        var manager = await factory.CreateClientAs(Role.WarehouseManager);
        var repaired = await (await manager.PostAsync("/api/stock/reconcile?repair=true", null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        repaired.GetProperty("discrepancyCount").GetInt32().ShouldBe(1);
        repaired.GetProperty("repaired").GetBoolean().ShouldBeTrue();

        (await factory.ReadStockAsync(productId, warehouseId)).OnHand.ShouldBe(50);

        var after = await (await manager.PostAsync("/api/stock/reconcile", null, ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        after.GetProperty("discrepancyCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Reconcile_asOperator_returns403()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.Operator);

        (await client.PostAsync("/api/stock/reconcile", null, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
