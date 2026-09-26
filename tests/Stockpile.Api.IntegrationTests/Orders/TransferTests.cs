using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Stockpile.Domain.Enums;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests.Orders;

[Collection(nameof(ApiCollection))]
public class TransferTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Fixture(
        HttpClient Manager, HttpClient Operator,
        Guid From, Guid To, Guid InTransit, Guid ProductId);

    private async Task<Fixture> ArrangeAsync(int stockAtSource = 100, long unitCost = 250)
    {
        await factory.ResetDatabaseAsync();
        var from = await factory.SeedWarehouseAsync("TR-FROM");
        var to = await factory.SeedWarehouseAsync("TR-TO");
        var inTransit = await factory.SeedWarehouseAsync("TR-TRANSIT", WarehouseKind.InTransit);
        var productId = await factory.SeedProductAsync("TR-001");
        await factory.SeedStockAsync(productId, from, stockAtSource, unitCost);

        return new Fixture(
            await factory.CreateClientAs(Role.WarehouseManager),
            await factory.CreateClientAs(Role.Operator),
            from, to, inTransit, productId);
    }

    private async Task<Guid> CreateAsync(Fixture f, int quantity = 40)
    {
        var response = await f.Manager.PostAsJsonAsync("/api/transfers", new
        {
            fromWarehouseId = f.From,
            toWarehouseId = f.To,
            inTransitWarehouseId = f.InTransit,
            lines = new[] { new { productId = f.ProductId, quantity } }
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Dispatch_movesStockFromTheSourceIntoTheInTransitWarehouse()
    {
        var f = await ArrangeAsync(stockAtSource: 100);
        var id = await CreateAsync(f, quantity: 40);

        var response = await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/dispatch",
            new { idempotencyKey = $"disp-{Guid.CreateVersion7()}" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ReadStockAsync(f.ProductId, f.From)).OnHand.ShouldBe(60);
        (await factory.ReadStockAsync(f.ProductId, f.InTransit)).OnHand.ShouldBe(40);
    }

    [Fact]
    public async Task Receive_movesStockFromInTransitIntoTheDestination()
    {
        var f = await ArrangeAsync(stockAtSource: 100);
        var id = await CreateAsync(f, quantity: 40);
        await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/dispatch",
            new { idempotencyKey = $"disp-{Guid.CreateVersion7()}" }, Ct);

        await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/receive",
            new { idempotencyKey = $"recv-{Guid.CreateVersion7()}" }, Ct);

        (await factory.ReadStockAsync(f.ProductId, f.From)).OnHand.ShouldBe(60);
        (await factory.ReadStockAsync(f.ProductId, f.InTransit)).OnHand.ShouldBe(0);
        (await factory.ReadStockAsync(f.ProductId, f.To)).OnHand.ShouldBe(40);
    }

    [Fact]
    public async Task DispatchThenReceive_conservesTotalValuation()
    {
        // §14. The property a client will actually check.
        var f = await ArrangeAsync(stockAtSource: 100, unitCost: 250);
        var before = await factory.TotalValuationAsync();
        before.ShouldBe(25_000);   // 100 x 250

        var id = await CreateAsync(f, quantity: 40);

        await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/dispatch",
            new { idempotencyKey = $"disp-{Guid.CreateVersion7()}" }, Ct);
        var midFlight = await factory.TotalValuationAsync();

        await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/receive",
            new { idempotencyKey = $"recv-{Guid.CreateVersion7()}" }, Ct);
        var after = await factory.TotalValuationAsync();

        // Conserved at EVERY step, not just at the end. Mid-flight is the step the
        // original model got wrong: without an in-transit warehouse those 40 units
        // exist in no stock row and 10,000 cents of value vanishes from the report.
        midFlight.ShouldBe(before);
        after.ShouldBe(before);
    }

    [Fact]
    public async Task Dispatch_withMoreThanIsAvailableAtTheSource_returns422()
    {
        var f = await ArrangeAsync(stockAtSource: 10);
        var id = await CreateAsync(f, quantity: 40);

        var response = await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/dispatch",
            new { idempotencyKey = $"disp-{Guid.CreateVersion7()}" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await factory.ReadStockAsync(f.ProductId, f.From)).OnHand.ShouldBe(10);
    }

    [Fact]
    public async Task Dispatch_writesPairedTransferOutAndTransferInMovements()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f, quantity: 40);

        await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/dispatch",
            new { idempotencyKey = $"disp-{Guid.CreateVersion7()}" }, Ct);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movements = await db.StockMovements
            .Where(m => m.ReferenceId == id)
            .ToListAsync(Ct);

        movements.Count.ShouldBe(2);
        movements.Sum(m => m.OnHandDelta).ShouldBe(0);   // nothing created, nothing destroyed
        movements.ShouldContain(m => m.Type == MovementType.TransferOut && m.WarehouseId == f.From);
        movements.ShouldContain(m => m.Type == MovementType.TransferIn && m.WarehouseId == f.InTransit);
    }

    [Fact]
    public async Task Cancel_beforeDispatch_succeeds()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f);

        var response = await f.Manager.PostAsync($"/api/transfers/{id}/cancel", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ReadStockAsync(f.ProductId, f.From)).OnHand.ShouldBe(100);
    }

    [Fact]
    public async Task Cancel_afterDispatch_returns422_withGuidance()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f);
        await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/dispatch",
            new { idempotencyKey = $"disp-{Guid.CreateVersion7()}" }, Ct);

        var response = await f.Manager.PostAsync($"/api/transfers/{id}/cancel", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString()
            .ShouldBe("transfer.cannot_cancel_after_dispatch");
    }

    [Fact]
    public async Task Create_betweenTheSameWarehouse_returns422()
    {
        var f = await ArrangeAsync();

        var response = await f.Manager.PostAsJsonAsync("/api/transfers", new
        {
            fromWarehouseId = f.From,
            toWarehouseId = f.From,
            inTransitWarehouseId = f.InTransit,
            lines = new[] { new { productId = f.ProductId, quantity = 1 } }
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_withAPhysicalWarehouseAsInTransit_returns422()
    {
        var f = await ArrangeAsync();

        var response = await f.Manager.PostAsJsonAsync("/api/transfers", new
        {
            fromWarehouseId = f.From,
            toWarehouseId = f.To,
            inTransitWarehouseId = f.To,   // not Kind = InTransit
            lines = new[] { new { productId = f.ProductId, quantity = 1 } }
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("transfer.in_transit_warehouse_invalid");
    }

    [Fact]
    public async Task AfterAFullTransferCycle_reconcileReportsZeroDiscrepancies()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f);
        await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/dispatch",
            new { idempotencyKey = $"disp-{Guid.CreateVersion7()}" }, Ct);
        await f.Operator.PostAsJsonAsync($"/api/transfers/{id}/receive",
            new { idempotencyKey = $"recv-{Guid.CreateVersion7()}" }, Ct);

        var report = await (await f.Manager.PostAsync("/api/stock/reconcile", null, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(0);
    }
}
