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
public class SalesOrderTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Fixture(
        HttpClient Manager, HttpClient Operator,
        Guid CustomerId, Guid WarehouseId, Guid ProductA, Guid ProductB);

    private async Task<Fixture> ArrangeAsync(int stockA = 100, int stockB = 100)
    {
        await factory.ResetDatabaseAsync();
        var warehouseId = await factory.SeedWarehouseAsync("SO-WH");
        var productA = await factory.SeedProductAsync("SO-A");
        var productB = await factory.SeedProductAsync("SO-B");
        await factory.SeedStockAsync(productA, warehouseId, stockA);
        await factory.SeedStockAsync(productB, warehouseId, stockB);
        var customerId = await factory.SeedCustomerAsync("Acme SA");

        return new Fixture(
            await factory.CreateClientAs(Role.WarehouseManager),
            await factory.CreateClientAs(Role.Operator),
            customerId, warehouseId, productA, productB);
    }

    private static object CreateBody(Fixture f, int qtyA = 5, int qtyB = 3) => new
    {
        customerId = f.CustomerId,
        warehouseId = f.WarehouseId,
        lines = new[]
        {
            new { productId = f.ProductA, quantity = qtyA, unitPriceCents = 1000L },
            new { productId = f.ProductB, quantity = qtyB, unitPriceCents = 2500L }
        }
    };

    private static async Task<Guid> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/sales-orders", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Create_returns201_withAGeneratedNumber()
    {
        var f = await ArrangeAsync();

        var response = await f.Manager.PostAsJsonAsync("/api/sales-orders", CreateBody(f), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("number").GetString().ShouldStartWith("SO-");
        body.GetProperty("status").GetString().ShouldBe("Draft");
    }

    [Fact]
    public async Task Confirm_reservesEveryLine()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f.Manager, CreateBody(f, qtyA: 5, qtyB: 3));

        var response = await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
            new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).Reserved.ShouldBe(5);
        (await factory.ReadStockAsync(f.ProductB, f.WarehouseId)).Reserved.ShouldBe(3);
    }

    [Fact]
    public async Task Confirm_withInsufficientStockOnOneLine_reservesNothing()
    {
        // All-or-nothing: the whole confirm runs in one transaction, so a refusal on the
        // second line must roll the first line's reservation back.
        var f = await ArrangeAsync(stockA: 100, stockB: 1);
        var id = await CreateAsync(f.Manager, CreateBody(f, qtyA: 5, qtyB: 3));

        var response = await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
            new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).Reserved.ShouldBe(0);
        (await factory.ReadStockAsync(f.ProductB, f.WarehouseId)).Reserved.ShouldBe(0);

        var detail = await f.Manager.GetFromJsonAsync<JsonElement>($"/api/sales-orders/{id}", Ct);
        detail.GetProperty("status").GetString().ShouldBe("Draft");
    }

    [Fact]
    public async Task Ship_convertsReservationsIntoIssues()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f.Manager, CreateBody(f, qtyA: 5, qtyB: 3));
        await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
            new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct);

        var detail = await f.Manager.GetFromJsonAsync<JsonElement>($"/api/sales-orders/{id}", Ct);
        foreach (var line in detail.GetProperty("lines").EnumerateArray())
        {
            await f.Operator.PostAsJsonAsync($"/api/sales-orders/{id}/pick", new
            {
                lineId = line.GetProperty("id").GetGuid(),
                quantity = line.GetProperty("quantityOrdered").GetInt32()
            }, Ct);
        }

        await f.Operator.PostAsync($"/api/sales-orders/{id}/pack", null, Ct);
        var ship = await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/ship",
            new { idempotencyKey = $"ship-{Guid.CreateVersion7()}" }, Ct);

        ship.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stockA = await factory.ReadStockAsync(f.ProductA, f.WarehouseId);
        stockA.OnHand.ShouldBe(95);     // 100 - 5 issued
        stockA.Reserved.ShouldBe(0);    // the reservation became an issue
    }

    [Fact]
    public async Task Cancel_whenConfirmed_releasesEveryOutstandingReservation()
    {
        // §14. The headline item for this phase.
        var f = await ArrangeAsync();
        var id = await CreateAsync(f.Manager, CreateBody(f, qtyA: 5, qtyB: 3));
        await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
            new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct);

        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).Reserved.ShouldBe(5);

        var cancel = await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/cancel",
            new { idempotencyKey = $"canc-{Guid.CreateVersion7()}" }, Ct);

        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);

        var a = await factory.ReadStockAsync(f.ProductA, f.WarehouseId);
        var b = await factory.ReadStockAsync(f.ProductB, f.WarehouseId);

        a.Reserved.ShouldBe(0);
        b.Reserved.ShouldBe(0);
        a.OnHand.ShouldBe(100);   // cancelling never touches on-hand
        b.OnHand.ShouldBe(100);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var releases = await db.StockMovements
            .Where(m => m.Type == MovementType.ReservationRelease && m.ReferenceId == id)
            .ToListAsync(Ct);
        releases.Count.ShouldBe(2);
        releases.Sum(m => m.ReservedDelta).ShouldBe(-8);
    }

    [Fact]
    public async Task Cancel_whenPacked_stillReleasesEverything()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f.Manager, CreateBody(f, qtyA: 5, qtyB: 3));
        await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
            new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct);

        var detail = await f.Manager.GetFromJsonAsync<JsonElement>($"/api/sales-orders/{id}", Ct);
        foreach (var line in detail.GetProperty("lines").EnumerateArray())
        {
            await f.Operator.PostAsJsonAsync($"/api/sales-orders/{id}/pick", new
            {
                lineId = line.GetProperty("id").GetGuid(),
                quantity = line.GetProperty("quantityOrdered").GetInt32()
            }, Ct);
        }
        await f.Operator.PostAsync($"/api/sales-orders/{id}/pack", null, Ct);

        await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/cancel",
            new { idempotencyKey = $"canc-{Guid.CreateVersion7()}" }, Ct);

        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).Reserved.ShouldBe(0);
    }

    [Fact]
    public async Task Cancel_afterShipping_returns422_andLeavesStockAlone()
    {
        var f = await ArrangeAsync();
        var id = await ShipAnOrderAsync(f);

        var cancel = await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/cancel",
            new { idempotencyKey = $"canc-{Guid.CreateVersion7()}" }, Ct);

        cancel.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await cancel.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("so.already_shipped");

        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).OnHand.ShouldBe(95);
    }

    [Fact]
    public async Task Pick_asViewer_returns403()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f.Manager, CreateBody(f));
        await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
            new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct);

        var viewer = await factory.CreateClientAs(Role.Viewer);
        var response = await viewer.PostAsJsonAsync($"/api/sales-orders/{id}/pick",
            new { lineId = Guid.CreateVersion7(), quantity = 1 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Confirm_replayedWithTheSameKey_reservesOnce()
    {
        var f = await ArrangeAsync();
        var id = await CreateAsync(f.Manager, CreateBody(f, qtyA: 5, qtyB: 3));
        var key = $"conf-{Guid.CreateVersion7()}";

        await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm", new { idempotencyKey = key }, Ct);
        var second = await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm", new { idempotencyKey = key }, Ct);

        second.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity);
        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).Reserved.ShouldBe(5);
    }

    [Fact]
    public async Task Confirm_raced_reservesOnce_andNeverFailsWithA500()
    {
        // Every racer reserves before any of them writes the order row, so the losers
        // only find out at SaveChanges, on the order's xmin. That has to surface as a 409
        // (or a 422 for a racer that loaded the order after the winner committed) with
        // the losers' reservations rolled back - never as an unhandled 500.
        var f = await ArrangeAsync();
        var id = await CreateAsync(f.Manager, CreateBody(f, qtyA: 5, qtyB: 3));

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
                new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.ShouldAllBe(r =>
            r.StatusCode == HttpStatusCode.OK
            || r.StatusCode == HttpStatusCode.Conflict
            || r.StatusCode == HttpStatusCode.UnprocessableEntity);

        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).Reserved.ShouldBe(5);
        (await factory.ReadStockAsync(f.ProductB, f.WarehouseId)).Reserved.ShouldBe(3);
    }

    [Fact]
    public async Task AfterAFullLifecycle_reconcileReportsZeroDiscrepancies()
    {
        var f = await ArrangeAsync();
        await ShipAnOrderAsync(f);

        var report = await (await f.Manager.PostAsync("/api/stock/reconcile", null, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(0);
    }

    private async Task<Guid> ShipAnOrderAsync(Fixture f)
    {
        var id = await CreateAsync(f.Manager, CreateBody(f, qtyA: 5, qtyB: 3));
        await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
            new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct);

        var detail = await f.Manager.GetFromJsonAsync<JsonElement>($"/api/sales-orders/{id}", Ct);
        foreach (var line in detail.GetProperty("lines").EnumerateArray())
        {
            await f.Operator.PostAsJsonAsync($"/api/sales-orders/{id}/pick", new
            {
                lineId = line.GetProperty("id").GetGuid(),
                quantity = line.GetProperty("quantityOrdered").GetInt32()
            }, Ct);
        }

        await f.Operator.PostAsync($"/api/sales-orders/{id}/pack", null, Ct);
        await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/ship",
            new { idempotencyKey = $"ship-{Guid.CreateVersion7()}" }, Ct);

        return id;
    }
}
