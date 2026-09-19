using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Catalogue;

[Collection(nameof(ApiCollection))]
public class ProductEndpointTests(StockpileApiFactory factory)
{
    private static object ValidProduct(string sku = "WID-001") => new
    {
        sku,
        name = "Steel Widget",
        description = "A widget made of steel",
        category = "Hardware",
        unitPriceCents = 1999L,
        barcode = "5012345678900",
        reorderPoint = 20,
        reorderQuantity = 100
    };

    [Fact]
    public async Task Create_asWarehouseManager_returns201_withTheCreatedProduct()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var response = await client.PostAsJsonAsync("/api/products", ValidProduct(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("sku").GetString().ShouldBe("WID-001");
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        response.Headers.Location.ShouldNotBeNull();
    }

    [Fact]
    public async Task Create_withADuplicateSku_returns422()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateClientAs(Role.WarehouseManager);
        await client.PostAsJsonAsync("/api/products", ValidProduct("DUP-001"), ct);

        var second = await client.PostAsJsonAsync("/api/products", ValidProduct("DUP-001"), ct);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("product.sku_taken");
    }

    [Fact]
    public async Task Create_asOperator_returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateClientAs(Role.Operator);

        (await client.PostAsJsonAsync("/api/products", ValidProduct("OPS-001"), ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_asViewer_returnsTheProduct()
    {
        var ct = TestContext.Current.CancellationToken;
        var manager = await factory.CreateClientAs(Role.WarehouseManager);
        var created = await (await manager.PostAsJsonAsync("/api/products", ValidProduct("VIEW-001"), ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = created.GetProperty("id").GetGuid();

        var viewer = await factory.CreateClientAs(Role.Viewer);
        var response = await viewer.GetAsync($"/api/products/{id}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
