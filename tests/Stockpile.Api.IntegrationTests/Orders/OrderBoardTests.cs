using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests.Orders;

[Collection(nameof(ApiCollection))]
public class OrderBoardTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] OpenColumns = ["Draft", "Confirmed", "Picking", "Packed"];

    private sealed record Fixture(HttpClient Manager, Guid WarehouseId, Guid CustomerId, Guid ProductId);

    private async Task<Fixture> ArrangeAsync()
    {
        await factory.ResetDatabaseAsync();
        var warehouseId = await factory.SeedWarehouseAsync("BRD-WH");
        var productId = await factory.SeedProductAsync("BRD-A");
        await factory.SeedStockAsync(productId, warehouseId, 1000);
        var customerId = await factory.SeedCustomerAsync("Board SA");

        return new Fixture(
            await factory.CreateClientAs(Role.WarehouseManager), warehouseId, customerId, productId);
    }

    private static async Task<Guid> CreateViaApiAsync(Fixture f, int quantity = 2)
    {
        var response = await f.Manager.PostAsJsonAsync("/api/sales-orders", new
        {
            customerId = f.CustomerId,
            warehouseId = f.WarehouseId,
            lines = new[] { new { productId = f.ProductId, quantity, unitPriceCents = 1000L } }
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Writes orders straight through the aggregate: the board is a read model, and
    /// driving twenty orders through confirm/pick/pack over HTTP would test the lifecycle
    /// again rather than the query.
    /// </summary>
    private async Task SeedOrderAsync(Fixture f, Guid warehouseId, SalesOrderStatus target)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var order = SalesOrder.Create(
            $"SO-SEED-{Guid.CreateVersion7():N}"[..20], f.CustomerId, warehouseId, now,
            [(f.ProductId, 3, 1000)]).Value;

        if (target != SalesOrderStatus.Draft && target != SalesOrderStatus.Cancelled)
            order.Confirm(now);
        if (target is SalesOrderStatus.Picking or SalesOrderStatus.Packed or SalesOrderStatus.Shipped)
            order.Pick(order.Lines[0].Id, 3);
        if (target is SalesOrderStatus.Packed or SalesOrderStatus.Shipped)
            order.Pack();
        if (target == SalesOrderStatus.Shipped)
            order.Ship(now);
        if (target == SalesOrderStatus.Cancelled)
            order.Cancel();

        order.Status.ShouldBe(target);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<JsonElement> BoardAsync(Fixture f, Guid? warehouseId = null)
    {
        var url = warehouseId is null ? "/api/sales-orders/board" : $"/api/sales-orders/board?warehouseId={warehouseId}";
        var response = await f.Manager.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("columns");
    }

    private static List<Guid> IdsIn(JsonElement columns, string status) =>
        columns.GetProperty(status).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task ANewOrder_appearsUnderDraft_asACard()
    {
        var f = await ArrangeAsync();
        var id = await CreateViaApiAsync(f, quantity: 4);

        var columns = await BoardAsync(f, f.WarehouseId);

        var card = columns.GetProperty("Draft").EnumerateArray().Single();
        card.GetProperty("id").GetGuid().ShouldBe(id);
        card.GetProperty("number").GetString().ShouldStartWith("SO-");
        card.GetProperty("customerName").GetString().ShouldBe("Board SA");
        card.GetProperty("lineCount").GetInt32().ShouldBe(1);
        card.GetProperty("totalUnits").GetInt32().ShouldBe(4);
        card.GetProperty("status").GetString().ShouldBe("Draft");
    }

    [Fact]
    public async Task Confirming_movesTheCardToConfirmed_onTheNextFetch()
    {
        var f = await ArrangeAsync();
        var id = await CreateViaApiAsync(f);

        (await f.Manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm",
            new { idempotencyKey = $"conf-{Guid.CreateVersion7()}" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var columns = await BoardAsync(f, f.WarehouseId);
        IdsIn(columns, "Draft").ShouldBeEmpty();
        IdsIn(columns, "Confirmed").ShouldBe([id]);
    }

    [Fact]
    public async Task TerminalOrders_doNotAppear_butEveryOpenColumnDoes()
    {
        // A board of terminal states is a report, not a board. Every open column is present
        // even when empty, so the client never has to invent one.
        var f = await ArrangeAsync();
        await SeedOrderAsync(f, f.WarehouseId, SalesOrderStatus.Shipped);
        await SeedOrderAsync(f, f.WarehouseId, SalesOrderStatus.Cancelled);

        var columns = await BoardAsync(f, f.WarehouseId);

        columns.EnumerateObject().Select(p => p.Name).ShouldBe(OpenColumns, ignoreOrder: true);
        foreach (var status in OpenColumns)
            IdsIn(columns, status).ShouldBeEmpty();
    }

    [Fact]
    public async Task FilteringByWarehouse_excludesOtherWarehouses()
    {
        var f = await ArrangeAsync();
        var other = await factory.SeedWarehouseAsync("BRD-OTHER");
        await SeedOrderAsync(f, f.WarehouseId, SalesOrderStatus.Draft);
        await SeedOrderAsync(f, other, SalesOrderStatus.Draft);

        (await BoardAsync(f, f.WarehouseId)).GetProperty("Draft").GetArrayLength().ShouldBe(1);
        (await BoardAsync(f)).GetProperty("Draft").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task TwentyOrdersAcrossFourStatuses_arriveInOneCall()
    {
        var f = await ArrangeAsync();
        foreach (var status in new[]
                 {
                     SalesOrderStatus.Draft, SalesOrderStatus.Confirmed,
                     SalesOrderStatus.Picking, SalesOrderStatus.Packed
                 })
        {
            for (var i = 0; i < 5; i++)
                await SeedOrderAsync(f, f.WarehouseId, status);
        }

        var columns = await BoardAsync(f, f.WarehouseId);

        foreach (var status in OpenColumns)
            columns.GetProperty(status).GetArrayLength().ShouldBe(5);
        columns.EnumerateObject().Sum(c => c.Value.GetArrayLength()).ShouldBe(20);
    }
}
