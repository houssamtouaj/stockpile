using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Stockpile.Domain.Enums;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests.Stock;

/// <summary>
/// The §14 definition-of-done item this project is built around.
///
/// Fifty operators try to reserve one unit each, simultaneously, against ten units of
/// stock. The correct outcome is ten successes and forty refusals — NOT one success and
/// forty-nine conflicts, which is what the instinctive read-check-write-with-xmin design
/// produces, and not a nondeterministic number, which is what staggered timing produces
/// on a design that is only accidentally correct.
/// </summary>
[Collection(nameof(ApiCollection))]
public class ConcurrentReservationTests(StockpileApiFactory factory, ITestOutputHelper output)
{
    private const int Contenders = 50;
    private const int UnitsInStock = 10;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FiftyReservations_againstTenUnits_sellExactlyTen()
    {
        var ct = Ct;

        // Arrange
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("RACE-001");
        var warehouseId = await factory.SeedWarehouseAsync("RACE-WH");
        await factory.SeedStockAsync(productId, warehouseId, UnitsInStock);
        var client = await factory.CreateClientAs(Role.Operator);

        // Release all 50 at once rather than letting them trickle out: a barrier is what
        // makes this a concurrency test instead of a fast sequential one.
        using var barrier = new SemaphoreSlim(0, Contenders);

        var attempts = Enumerable.Range(0, Contenders).Select(async i =>
        {
            await barrier.WaitAsync(ct);
            return await client.PostAsJsonAsync("/api/stock/reserve", new
            {
                productId,
                warehouseId,
                quantity = 1,
                idempotencyKey = $"race-{i}-{Guid.CreateVersion7()}"
            }, ct);
        }).ToArray();

        barrier.Release(Contenders);
        var responses = await Task.WhenAll(attempts);

        // Assert — counts
        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var refused = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        var conflicted = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        var failed = responses.Count(r => (int)r.StatusCode >= 500);

        output.WriteLine($"200 OK                     : {succeeded}");
        output.WriteLine($"422 InsufficientStock      : {refused}");
        output.WriteLine($"409 Conflict               : {conflicted}");
        output.WriteLine($"5xx                        : {failed}");

        succeeded.ShouldBe(UnitsInStock);
        refused.ShouldBe(Contenders - UnitsInStock);

        // §6: with this design there is no conflict, only a correct refusal.
        conflicted.ShouldBe(0);
        failed.ShouldBe(0);

        // Assert — the refusals carry a machine-readable code, not just a status
        var firstRefusal = responses.First(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        var problem = await firstRefusal.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("stock.insufficient");

        // Assert — final state. On-hand stays at 10: reservations move `reserved`, not
        // `on_hand`. On-hand only drops at ship time, when the reservation becomes an issue.
        var stock = await factory.ReadStockAsync(productId, warehouseId);
        stock.OnHand.ShouldBe(10);
        stock.Reserved.ShouldBe(10);
        (stock.OnHand - stock.Reserved).ShouldBe(0);

        output.WriteLine($"final on_hand={stock.OnHand} reserved={stock.Reserved} "
                         + $"available={stock.OnHand - stock.Reserved}");
    }

    [Fact]
    public async Task FiftyReservations_writeExactlyTenLedgerRows()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("RACE-002");
        var warehouseId = await factory.SeedWarehouseAsync("RACE-WH2");
        await factory.SeedStockAsync(productId, warehouseId, UnitsInStock);
        var client = await factory.CreateClientAs(Role.Operator);

        await Task.WhenAll(Enumerable.Range(0, Contenders).Select(i =>
            client.PostAsJsonAsync("/api/stock/reserve", new
            {
                productId, warehouseId, quantity = 1,
                idempotencyKey = $"ledger-race-{i}-{Guid.CreateVersion7()}"
            }, ct)));

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var holds = await db.StockMovements
            .Where(m => m.ProductId == productId && m.Type == MovementType.ReservationHold)
            .ToListAsync(ct);

        // A refused reservation must leave no trace in the ledger.
        holds.Count.ShouldBe(UnitsInStock);
        holds.Sum(m => m.ReservedDelta).ShouldBe(UnitsInStock);
        holds.Sum(m => m.OnHandDelta).ShouldBe(0);
    }

    [Fact]
    public async Task UnderMixedConcurrentLoad_stockNeverGoesNegative()
    {
        // Reserve, release and adjust all at once against a thin buffer. The point is not
        // the final number — it is that no combination of interleavings can break the
        // invariant, and that the CHECK constraint (layer 1) never has to fire.
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("RACE-003");
        var warehouseId = await factory.SeedWarehouseAsync("RACE-WH3");
        await factory.SeedStockAsync(productId, warehouseId, 20);

        var operatorClient = await factory.CreateClientAs(Role.Operator);
        var managerClient = await factory.CreateClientAs(Role.WarehouseManager);

        var work = new List<Task<HttpResponseMessage>>();

        for (var i = 0; i < 30; i++)
        {
            work.Add(operatorClient.PostAsJsonAsync("/api/stock/reserve", new
            {
                productId, warehouseId, quantity = 1,
                idempotencyKey = $"mix-res-{i}-{Guid.CreateVersion7()}"
            }, ct));

            work.Add(managerClient.PostAsJsonAsync("/api/stock/adjust", new
            {
                productId, warehouseId, onHandDelta = i % 2 == 0 ? 1 : -1,
                reason = "Load test",
                idempotencyKey = $"mix-adj-{i}-{Guid.CreateVersion7()}"
            }, ct));
        }

        var responses = await Task.WhenAll(work);

        responses.Count(r => (int)r.StatusCode >= 500).ShouldBe(0,
            "a 5xx here means the CHECK constraint fired, which is a logic bug, not a race");

        var stock = await factory.ReadStockAsync(productId, warehouseId);
        stock.OnHand.ShouldBeGreaterThanOrEqualTo(0);
        stock.Reserved.ShouldBeGreaterThanOrEqualTo(0);
        stock.Reserved.ShouldBeLessThanOrEqualTo(stock.OnHand);
    }
}
