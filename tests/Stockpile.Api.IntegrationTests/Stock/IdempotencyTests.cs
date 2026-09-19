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
public class IdempotencyTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, Guid ProductId, Guid WarehouseId)> ArrangeAsync(int onHand)
    {
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("IDEM-001");
        var warehouseId = await factory.SeedWarehouseAsync("IDEM-WH");
        await factory.SeedStockAsync(productId, warehouseId, onHand);
        return (await factory.CreateClientAs(Role.Operator), productId, warehouseId);
    }

    [Fact]
    public async Task ReplayedKey_appliesOnce_andReturnsOriginalAfterValues()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);
        var key = $"reserve-{Guid.CreateVersion7()}";
        var body = new { productId, warehouseId, quantity = 3, idempotencyKey = key };

        var first = await client.PostAsJsonAsync("/api/stock/reserve", body, ct);
        var second = await client.PostAsJsonAsync("/api/stock/reserve", body, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(ct);

        secondBody.GetProperty("onHandAfter").GetInt32()
            .ShouldBe(firstBody.GetProperty("onHandAfter").GetInt32());
        secondBody.GetProperty("reservedAfter").GetInt32()
            .ShouldBe(firstBody.GetProperty("reservedAfter").GetInt32());
        secondBody.GetProperty("wasReplay").GetBoolean().ShouldBeTrue();

        // Applied exactly once.
        (await factory.ReadStockAsync(productId, warehouseId)).Reserved.ShouldBe(3);
    }

    [Fact]
    public async Task ReplayedKey_writesOnlyOneLedgerRow()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);
        var key = $"reserve-{Guid.CreateVersion7()}";
        var body = new { productId, warehouseId, quantity = 2, idempotencyKey = key };

        await client.PostAsJsonAsync("/api/stock/reserve", body, ct);
        await client.PostAsJsonAsync("/api/stock/reserve", body, ct);
        await client.PostAsJsonAsync("/api/stock/reserve", body, ct);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.StockMovements.CountAsync(m => m.IdempotencyKey == key, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentReplaysOfTheSameKey_applyExactlyOnce()
    {
        // The pre-check alone cannot guarantee this: two requests can both read
        // "not yet applied" before either inserts. The unique index is the guarantee.
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 100);
        var key = $"reserve-race-{Guid.CreateVersion7()}";
        var body = new { productId, warehouseId, quantity = 5, idempotencyKey = key };

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/stock/reserve", body, ct)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await factory.ReadStockAsync(productId, warehouseId)).Reserved.ShouldBe(5);
    }

    [Fact]
    public async Task DifferentKeys_applySeparately()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);

        await client.PostAsJsonAsync("/api/stock/reserve",
            new { productId, warehouseId, quantity = 2, idempotencyKey = $"a-{Guid.CreateVersion7()}" }, ct);
        await client.PostAsJsonAsync("/api/stock/reserve",
            new { productId, warehouseId, quantity = 3, idempotencyKey = $"b-{Guid.CreateVersion7()}" }, ct);

        (await factory.ReadStockAsync(productId, warehouseId)).Reserved.ShouldBe(5);
    }

    [Fact]
    public async Task MissingIdempotencyKey_returns400()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var response = await client.PostAsJsonAsync("/api/stock/reserve",
            new { productId, warehouseId, quantity = 1, idempotencyKey = "" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
