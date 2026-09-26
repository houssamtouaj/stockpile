using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Orders;

/// <summary>
/// Multi-row stock commands must take their row locks in one global order. Walking the
/// lines in load order lets two commands that touch the same rows in opposite orders each
/// hold the row the other one wants next — Postgres breaks the cycle by killing one of them
/// with 40P01, which used to reach the client as a 500.
/// </summary>
[Collection(nameof(ApiCollection))]
public class LockOrderingTests(StockpileApiFactory factory)
{
    private const int Rounds = 8;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentConfirms_withOppositeLineOrder_neverDeadlock()
    {
        await factory.ResetDatabaseAsync();
        var warehouseId = await factory.SeedWarehouseAsync("LOCK-WH");
        var productA = await factory.SeedProductAsync("LOCK-A");
        var productB = await factory.SeedProductAsync("LOCK-B");
        await factory.SeedStockAsync(productA, warehouseId, 10_000);
        await factory.SeedStockAsync(productB, warehouseId, 10_000);
        var customerId = await factory.SeedCustomerAsync("Lock SA");
        var manager = await factory.CreateClientAs(Role.WarehouseManager);

        var responses = new List<HttpResponseMessage>();

        for (var round = 0; round < Rounds; round++)
        {
            // Half the orders list A then B, half B then A: exactly the pair that deadlocks
            // when each confirm reserves its lines in the order it loaded them.
            var ids = new List<Guid>();
            for (var i = 0; i < 6; i++)
            {
                var (first, second) = i % 2 == 0 ? (productA, productB) : (productB, productA);
                ids.Add(await CreateSalesOrderAsync(manager, customerId, warehouseId, first, second));
            }

            responses.AddRange(await RaceAsync(ids.Select(id => (Func<Task<HttpResponseMessage>>)(() =>
                manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
                    new { idempotencyKey = $"lock-{Guid.CreateVersion7()}" }, Ct)))));
        }

        await AssertEveryResponseSucceededAsync(responses);

        // 48 orders x 1 unit per line.
        (await factory.ReadStockAsync(productA, warehouseId)).Reserved.ShouldBe(Rounds * 6);
        (await factory.ReadStockAsync(productB, warehouseId)).Reserved.ShouldBe(Rounds * 6);
        await AssertReconcilesAsync(manager);
    }

    [Fact]
    public async Task CrossedDispatchAndReceive_throughTheSharedInTransitWarehouse_neverDeadlock()
    {
        // A dispatch W1 -> W2 locks (W1, then in-transit); a receipt of a W2 -> W1 transfer
        // locks (in-transit, then W1). Sorting lines by product cannot help: it is one
        // product, and the crossing is between warehouses.
        await factory.ResetDatabaseAsync();
        var w1 = await factory.SeedWarehouseAsync("LOCK-W1");
        var w2 = await factory.SeedWarehouseAsync("LOCK-W2");
        var inTransit = await factory.SeedWarehouseAsync("LOCK-IT", WarehouseKind.InTransit);
        var productId = await factory.SeedProductAsync("LOCK-TR");
        await factory.SeedStockAsync(productId, w1, 10_000, averageUnitCostCents: 250);
        await factory.SeedStockAsync(productId, w2, 10_000, averageUnitCostCents: 250);
        var manager = await factory.CreateClientAs(Role.WarehouseManager);
        var ops = await factory.CreateClientAs(Role.Operator);

        var before = await factory.TotalValuationAsync();
        var responses = new List<HttpResponseMessage>();

        for (var round = 0; round < Rounds; round++)
        {
            var races = new List<Func<Task<HttpResponseMessage>>>();
            for (var i = 0; i < 3; i++)
            {
                var outbound = await CreateTransferAsync(manager, w1, w2, inTransit, productId);

                var inbound = await CreateTransferAsync(manager, w2, w1, inTransit, productId);
                (await ops.PostAsJsonAsync($"/api/transfers/{inbound}/dispatch",
                    new { idempotencyKey = $"lock-{Guid.CreateVersion7()}" }, Ct))
                    .StatusCode.ShouldBe(HttpStatusCode.OK);

                races.Add(() => ops.PostAsJsonAsync($"/api/transfers/{outbound}/dispatch",
                    new { idempotencyKey = $"lock-{Guid.CreateVersion7()}" }, Ct));
                races.Add(() => ops.PostAsJsonAsync($"/api/transfers/{inbound}/receive",
                    new { idempotencyKey = $"lock-{Guid.CreateVersion7()}" }, Ct));
            }

            responses.AddRange(await RaceAsync(races));
        }

        await AssertEveryResponseSucceededAsync(responses);

        // Every round sends 3 x 5 units W1 -> in-transit and brings 3 x 5 units in-transit -> W1.
        (await factory.ReadStockAsync(productId, w1)).OnHand.ShouldBe(10_000);
        (await factory.ReadStockAsync(productId, w2)).OnHand.ShouldBe(10_000 - Rounds * 15);
        (await factory.ReadStockAsync(productId, inTransit)).OnHand.ShouldBe(Rounds * 15);
        (await factory.TotalValuationAsync()).ShouldBe(before);
        await AssertReconcilesAsync(manager);
    }

    private static async Task<HttpResponseMessage[]> RaceAsync(IEnumerable<Func<Task<HttpResponseMessage>>> requests)
    {
        var list = requests.ToList();
        using var barrier = new SemaphoreSlim(0, list.Count);

        var inFlight = list.Select(async send =>
        {
            await barrier.WaitAsync(Ct);
            return await send();
        }).ToArray();

        barrier.Release(list.Count);
        return await Task.WhenAll(inFlight);
    }

    private static async Task AssertEveryResponseSucceededAsync(IEnumerable<HttpResponseMessage> responses)
    {
        var failures = new List<string>();
        foreach (var response in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            failures.Add($"{(int)response.StatusCode} {problem.GetProperty("errorCode").GetString()}");
        }

        failures.ShouldBeEmpty();
    }

    private static async Task AssertReconcilesAsync(HttpClient manager)
    {
        var report = await (await manager.PostAsync("/api/stock/reconcile", null, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(0);
    }

    private static async Task<Guid> CreateSalesOrderAsync(
        HttpClient client, Guid customerId, Guid warehouseId, Guid first, Guid second)
    {
        var response = await client.PostAsJsonAsync("/api/sales-orders", new
        {
            customerId,
            warehouseId,
            lines = new[]
            {
                new { productId = first, quantity = 1, unitPriceCents = 1000L },
                new { productId = second, quantity = 1, unitPriceCents = 1000L }
            }
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTransferAsync(
        HttpClient client, Guid from, Guid to, Guid inTransit, Guid productId)
    {
        var response = await client.PostAsJsonAsync("/api/transfers", new
        {
            fromWarehouseId = from,
            toWarehouseId = to,
            inTransitWarehouseId = inTransit,
            lines = new[] { new { productId, quantity = 5 } }
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }
}
