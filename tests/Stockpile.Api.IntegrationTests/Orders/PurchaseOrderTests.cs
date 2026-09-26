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
public class PurchaseOrderTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Fixture(
        HttpClient Manager, HttpClient Operator,
        Guid SupplierId, Guid WarehouseId, Guid ProductA, Guid ProductB);

    private async Task<Fixture> ArrangeAsync()
    {
        await factory.ResetDatabaseAsync();
        var warehouseId = await factory.SeedWarehouseAsync("PO-WH");
        var productA = await factory.SeedProductAsync("PO-A");
        var productB = await factory.SeedProductAsync("PO-B");
        var supplierId = await factory.SeedSupplierAsync("SUP-01");

        return new Fixture(
            await factory.CreateClientAs(Role.WarehouseManager),
            await factory.CreateClientAs(Role.Operator),
            supplierId, warehouseId, productA, productB);
    }

    private static object CreateBody(Fixture f, int qtyA = 10, long costA = 700, int qtyB = 4) => new
    {
        supplierId = f.SupplierId,
        warehouseId = f.WarehouseId,
        lines = new[]
        {
            new { productId = f.ProductA, quantity = qtyA, unitCostCents = costA },
            new { productId = f.ProductB, quantity = qtyB, unitCostCents = 1200L }
        }
    };

    private static async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/purchase-orders", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<(Guid Id, Guid LineA, Guid LineB)> CreateSubmittedAsync(Fixture f, long costA = 700)
    {
        var created = await CreateAsync(f.Manager, CreateBody(f, costA: costA));
        var id = created.GetProperty("id").GetGuid();

        (await f.Manager.PostAsync($"/api/purchase-orders/{id}/submit", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var lines = created.GetProperty("lines").EnumerateArray().ToList();
        var lineA = lines.Single(l => l.GetProperty("productId").GetGuid() == f.ProductA).GetProperty("id").GetGuid();
        var lineB = lines.Single(l => l.GetProperty("productId").GetGuid() == f.ProductB).GetProperty("id").GetGuid();
        return (id, lineA, lineB);
    }

    private static Task<HttpResponseMessage> ReceiveAsync(
        HttpClient client, Guid id, Guid lineId, int quantity, string? key = null) =>
        client.PostAsJsonAsync($"/api/purchase-orders/{id}/receive", new
        {
            lineId,
            quantity,
            idempotencyKey = key ?? $"recv-{Guid.CreateVersion7()}"
        }, Ct);

    private async Task<string> StatusOfAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/purchase-orders/{id}", Ct))
            .GetProperty("status").GetString()!;

    [Fact]
    public async Task Create_returns201_withAGeneratedNumber()
    {
        var f = await ArrangeAsync();

        var body = await CreateAsync(f.Manager, CreateBody(f));

        body.GetProperty("number").GetString().ShouldStartWith("PO-");
        body.GetProperty("status").GetString().ShouldBe("Draft");
    }

    [Fact]
    public async Task Submit_movesToSubmitted()
    {
        var f = await ArrangeAsync();
        var (id, _, _) = await CreateSubmittedAsync(f);

        (await StatusOfAsync(f.Manager, id)).ShouldBe("Submitted");

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.StockMovements.CountAsync(m => m.ReferenceId == id, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Receive_aPartialQuantity_increasesOnHand_andMovesToPartiallyReceived()
    {
        var f = await ArrangeAsync();
        await factory.SeedStockAsync(f.ProductA, f.WarehouseId, 20, 700);
        var (id, lineA, _) = await CreateSubmittedAsync(f);

        var response = await ReceiveAsync(f.Manager, id, lineA, 4);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).OnHand.ShouldBe(24);
        (await StatusOfAsync(f.Manager, id)).ShouldBe("PartiallyReceived");

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movement = await db.StockMovements.SingleAsync(m => m.ReferenceId == id, Ct);
        movement.Type.ShouldBe(MovementType.Receipt);
        movement.OnHandDelta.ShouldBe(4);
        movement.ReservedDelta.ShouldBe(0);
        movement.UnitCostCents.ShouldBe(700);
    }

    [Fact]
    public async Task Receive_theRemainder_movesToReceived()
    {
        var f = await ArrangeAsync();
        var (id, lineA, lineB) = await CreateSubmittedAsync(f);

        await ReceiveAsync(f.Manager, id, lineA, 4);
        await ReceiveAsync(f.Manager, id, lineA, 6);
        (await StatusOfAsync(f.Manager, id)).ShouldBe("PartiallyReceived");

        (await ReceiveAsync(f.Manager, id, lineB, 4)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await StatusOfAsync(f.Manager, id)).ShouldBe("Received");
        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).OnHand.ShouldBe(10);
    }

    [Fact]
    public async Task Receive_beyondTheOutstandingQuantity_returns422()
    {
        var f = await ArrangeAsync();
        var (id, lineA, _) = await CreateSubmittedAsync(f);
        await ReceiveAsync(f.Manager, id, lineA, 8);

        var response = await ReceiveAsync(f.Manager, id, lineA, 3);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("errorCode").GetString().ShouldBe("po.receipt_exceeds_outstanding");
        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).OnHand.ShouldBe(8);
    }

    [Fact]
    public async Task Receive_recomputesTheWeightedAverageCost()
    {
        var f = await ArrangeAsync();
        await factory.SeedStockAsync(f.ProductA, f.WarehouseId, 10, 500);
        var (id, lineA, _) = await CreateSubmittedAsync(f, costA: 700);

        await ReceiveAsync(f.Manager, id, lineA, 10);

        var stock = await factory.ReadStockAsync(f.ProductA, f.WarehouseId);
        stock.OnHand.ShouldBe(20);
        stock.AverageCost.ShouldBe(600);   // (10 x 500 + 10 x 700) / 20
    }

    [Fact]
    public async Task Receive_intoAWarehouseWithNoStockRow_createsTheRow()
    {
        var f = await ArrangeAsync();   // no SeedStockAsync: the product was never stocked here
        var (id, lineA, _) = await CreateSubmittedAsync(f, costA: 700);

        (await ReceiveAsync(f.Manager, id, lineA, 4)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stock = await factory.ReadStockAsync(f.ProductA, f.WarehouseId);
        stock.OnHand.ShouldBe(4);
        stock.AverageCost.ShouldBe(700);
    }

    [Fact]
    public async Task Receive_replayedWithTheSameKey_appliesOnce()
    {
        var f = await ArrangeAsync();
        var (id, lineA, _) = await CreateSubmittedAsync(f);
        var key = $"recv-{Guid.CreateVersion7()}";

        (await ReceiveAsync(f.Manager, id, lineA, 4, key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await ReceiveAsync(f.Manager, id, lineA, 4, key);

        // An honest retry is answered as the success it already was — and neither the
        // stock row nor the order's own receipt count moves a second time.
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).OnHand.ShouldBe(4);

        var detail = await f.Manager.GetFromJsonAsync<JsonElement>($"/api/purchase-orders/{id}", Ct);
        detail.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("id").GetGuid() == lineA)
            .GetProperty("quantityReceived").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task Receive_reusingAKeyForADifferentQuantity_returns409()
    {
        var f = await ArrangeAsync();
        var (id, lineA, _) = await CreateSubmittedAsync(f);
        var key = $"recv-{Guid.CreateVersion7()}";
        await ReceiveAsync(f.Manager, id, lineA, 4, key);

        var reused = await ReceiveAsync(f.Manager, id, lineA, 5, key);

        reused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await factory.ReadStockAsync(f.ProductA, f.WarehouseId)).OnHand.ShouldBe(4);
    }

    [Fact]
    public async Task Cancel_fromSubmitted_succeeds()
    {
        var f = await ArrangeAsync();
        var (id, _, _) = await CreateSubmittedAsync(f);

        var response = await f.Manager.PostAsync($"/api/purchase-orders/{id}/cancel", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(f.Manager, id)).ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Cancel_afterFullReceipt_returns422()
    {
        var f = await ArrangeAsync();
        var (id, lineA, lineB) = await CreateSubmittedAsync(f);
        await ReceiveAsync(f.Manager, id, lineA, 10);
        await ReceiveAsync(f.Manager, id, lineB, 4);

        var response = await f.Manager.PostAsync($"/api/purchase-orders/{id}/cancel", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("errorCode").GetString().ShouldBe("po.already_received");
    }

    [Fact]
    public async Task Receive_asOperator_returns403()
    {
        // §8: receiving purchase orders is WarehouseManager and above.
        var f = await ArrangeAsync();
        var (id, lineA, _) = await CreateSubmittedAsync(f);

        var response = await ReceiveAsync(f.Operator, id, lineA, 1);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AfterReceiving_reconcileReportsZeroDiscrepancies()
    {
        var f = await ArrangeAsync();
        await factory.SeedStockAsync(f.ProductA, f.WarehouseId, 10, 500);
        var (id, lineA, lineB) = await CreateSubmittedAsync(f);
        await ReceiveAsync(f.Manager, id, lineA, 3);
        await ReceiveAsync(f.Manager, id, lineA, 7);
        await ReceiveAsync(f.Manager, id, lineB, 4);

        var report = await (await f.Manager.PostAsync("/api/stock/reconcile", null, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);

        report.GetProperty("discrepancyCount").GetInt32().ShouldBe(0);
    }
}
