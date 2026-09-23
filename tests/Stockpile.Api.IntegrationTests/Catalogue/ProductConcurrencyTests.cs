using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Catalogue;

/// <summary>
/// The contrast that makes the §6 argument land: 409 is the <em>right</em> answer here,
/// because two people editing the same product form genuinely is a conflict a human must
/// resolve. The same tool applied to a hot counter row would have turned forty correct
/// refusals into forty spurious errors — see ConcurrentReservationTests.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ProductConcurrencyTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Patch_withAStaleRowVersion_returns409_withTheCurrentVersion()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var created = await (await client.PostAsJsonAsync("/api/products", new
        {
            sku = "CONC-001", name = "Original", category = "Test",
            unitPriceCents = 100L, reorderPoint = 1, reorderQuantity = 1
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        var id = created.GetProperty("id").GetGuid();
        var staleVersion = created.GetProperty("rowVersion").GetUInt32();

        // First edit wins and bumps xmin.
        var first = await client.PatchAsJsonAsync($"/api/products/{id}",
            new { name = "First edit", rowVersion = staleVersion }, ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Second edit carries the now-stale version.
        var second = await client.PatchAsJsonAsync($"/api/products/{id}",
            new { name = "Second edit", rowVersion = staleVersion }, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("concurrency.conflict");
        problem.GetProperty("entityType").GetString().ShouldBe("Product");
    }

    [Fact]
    public async Task Patch_thatChangesNothing_stillRejectsAStaleRowVersion()
    {
        // The quiet version of the same lost update. EF emits no UPDATE when no property
        // differs, so the xmin predicate is never sent and SaveChanges cannot raise a
        // conflict — the client is handed 200 plus somebody else's data and believes the
        // write they were holding went through.
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var created = await (await client.PostAsJsonAsync("/api/products", new
        {
            sku = "CONC-003", name = "Original", category = "Test",
            unitPriceCents = 100L, reorderPoint = 1, reorderQuantity = 1
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        var id = created.GetProperty("id").GetGuid();
        var staleVersion = created.GetProperty("rowVersion").GetUInt32();

        // Somebody else edits, which bumps xmin.
        (await client.PatchAsJsonAsync($"/api/products/{id}",
            new { category = "Moved", rowVersion = staleVersion }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // We re-send the name we read before their edit. Nothing changes, so nothing is
        // modified — and the conflict is real all the same.
        var noOp = await client.PatchAsJsonAsync($"/api/products/{id}",
            new { name = "Original", rowVersion = staleVersion }, ct);

        noOp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await noOp.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("concurrency.conflict");
    }

    [Fact]
    public async Task Patch_thatChangesNothing_withACurrentRowVersion_succeeds()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var created = await (await client.PostAsJsonAsync("/api/products", new
        {
            sku = "CONC-004", name = "Original", category = "Test",
            unitPriceCents = 100L, reorderPoint = 1, reorderQuantity = 1
        }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);

        var noOp = await client.PatchAsJsonAsync($"/api/products/{created.GetProperty("id").GetGuid()}",
            new { name = "Original", rowVersion = created.GetProperty("rowVersion").GetUInt32() }, ct);

        noOp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patch_onAProduct_neverAffectsStockQuantities()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("CONC-002");
        var warehouseId = await factory.SeedWarehouseAsync("CONC-WH");
        await factory.SeedStockAsync(productId, warehouseId, 42);

        var client = await factory.CreateClientAs(Role.WarehouseManager);
        var current = await client.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", ct);

        await client.PatchAsJsonAsync($"/api/products/{productId}", new
        {
            name = "Renamed", rowVersion = current.GetProperty("rowVersion").GetUInt32()
        }, ct);

        (await factory.ReadStockAsync(productId, warehouseId)).OnHand.ShouldBe(42);
    }
}
