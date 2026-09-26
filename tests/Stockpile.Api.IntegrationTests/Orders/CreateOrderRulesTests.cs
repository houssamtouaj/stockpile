using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Orders;

/// <summary>
/// What the three create endpoints refuse, and what a refusal must not cost: nextval() is
/// not rolled back, so a create refused after taking a document number leaves a gap.
/// </summary>
[Collection(nameof(ApiCollection))]
public class CreateOrderRulesTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Fixture(
        HttpClient Manager, Guid Warehouse, Guid OtherWarehouse, Guid InTransit,
        Guid Product, Guid Customer, Guid Supplier);

    private async Task<Fixture> ArrangeAsync()
    {
        await factory.ResetDatabaseAsync();
        return new Fixture(
            await factory.CreateClientAs(Role.WarehouseManager),
            await factory.SeedWarehouseAsync("CR-WH"),
            await factory.SeedWarehouseAsync("CR-WH2"),
            await factory.SeedWarehouseAsync("CR-IT", WarehouseKind.InTransit),
            await factory.SeedProductAsync("CR-001"),
            await factory.SeedCustomerAsync("Create SA"),
            await factory.SeedSupplierAsync("CR-SUP"));
    }

    private static object SalesOrder(Fixture f, params Guid[] products) => new
    {
        customerId = f.Customer,
        warehouseId = f.Warehouse,
        lines = products.Select(p => new { productId = p, quantity = 1, unitPriceCents = 100L }).ToArray()
    };

    private static object PurchaseOrder(Fixture f, params Guid[] products) => new
    {
        supplierId = f.Supplier,
        warehouseId = f.Warehouse,
        lines = products.Select(p => new { productId = p, quantity = 1, unitCostCents = 100L }).ToArray()
    };

    private static object Transfer(Fixture f, params Guid[] products) => new
    {
        fromWarehouseId = f.Warehouse,
        toWarehouseId = f.OtherWarehouse,
        inTransitWarehouseId = f.InTransit,
        lines = products.Select(p => new { productId = p, quantity = 1 }).ToArray()
    };

    public static TheoryData<string> Routes => ["/api/sales-orders", "/api/purchase-orders", "/api/transfers"];

    private static object Body(string route, Fixture f, params Guid[] products) => route switch
    {
        "/api/sales-orders" => SalesOrder(f, products),
        "/api/purchase-orders" => PurchaseOrder(f, products),
        _ => Transfer(f, products)
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task RefusedCreates_doNotUseUpADocumentNumber(string route)
    {
        var f = await ArrangeAsync();

        var empty = await f.Manager.PostAsJsonAsync(route, Body(route, f), Ct);
        var duplicate = await f.Manager.PostAsJsonAsync(route, Body(route, f, f.Product, f.Product), Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var created = await f.Manager.PostAsJsonAsync(route, Body(route, f, f.Product), Ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("number").GetString().ShouldEndWith("-00001");
    }
}
