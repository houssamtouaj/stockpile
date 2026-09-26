using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Stock;

/// <summary>
/// Units in the in-transit warehouse belong to transfers, and only the transfer legs may
/// move them. A manual reservation or adjustment there leaves a later receipt unable to
/// take its units out, stranding the transfer InTransit with no way to cancel it.
/// </summary>
[Collection(nameof(ApiCollection))]
public class InTransitStockTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Commands => ["reserve", "release", "adjust", "count"];

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task ManualStockCommand_onTheInTransitWarehouse_returns422_andChangesNothing(string command)
    {
        await factory.ResetDatabaseAsync();
        var inTransit = await factory.SeedWarehouseAsync("IT-ONLY", WarehouseKind.InTransit);
        var productId = await factory.SeedProductAsync("IT-001");
        await factory.SeedStockAsync(productId, inTransit, 40);
        var manager = await factory.CreateClientAs(Role.WarehouseManager);
        var key = $"it-{Guid.CreateVersion7()}";

        object body = command switch
        {
            "reserve" or "release" => new { productId, warehouseId = inTransit, quantity = 5, idempotencyKey = key },
            "adjust" => new { productId, warehouseId = inTransit, onHandDelta = -5, reason = "shrinkage", idempotencyKey = key },
            _ => new { productId, warehouseId = inTransit, observedOnHand = 35, reason = "count", idempotencyKey = key }
        };

        var response = await manager.PostAsJsonAsync($"/api/stock/{command}", body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("stock.warehouse_not_physical");

        var stock = await factory.ReadStockAsync(productId, inTransit);
        stock.OnHand.ShouldBe(40);
        stock.Reserved.ShouldBe(0);
    }
}
