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
public class AdjustAndCountTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, Guid ProductId, Guid WarehouseId)> ArrangeAsync(
        int onHand, Role role = Role.WarehouseManager)
    {
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("ADJ-001");
        var warehouseId = await factory.SeedWarehouseAsync("ADJ-WH");
        await factory.SeedStockAsync(productId, warehouseId, onHand);
        return (await factory.CreateClientAs(role), productId, warehouseId);
    }

    [Fact]
    public async Task Adjust_downward_reducesOnHand_andWritesASignedMovement()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var response = await client.PostAsJsonAsync("/api/stock/adjust", new
        {
            productId, warehouseId, onHandDelta = -2,
            reason = "Damaged in handling",
            idempotencyKey = $"adj-{Guid.CreateVersion7()}"
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ReadStockAsync(productId, warehouseId)).OnHand.ShouldBe(8);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Filtered by type: SeedStockAsync also writes the opening-balance Receipt that
        // keeps the ledger explaining the snapshot, so an unfiltered Single() would find
        // two rows and fail on the fixture rather than on the code under test.
        var movement = await db.StockMovements.SingleAsync(m => m.Type == MovementType.Adjustment, ct);
        movement.OnHandDelta.ShouldBe(-2);
        movement.ReservedDelta.ShouldBe(0);
        movement.OnHandAfter.ShouldBe(8);
        movement.Reason.ShouldBe("Damaged in handling");
    }

    [Fact]
    public async Task Adjust_withoutAReason_returns400()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var response = await client.PostAsJsonAsync("/api/stock/adjust", new
        {
            productId, warehouseId, onHandDelta = -2, reason = "",
            idempotencyKey = $"adj-{Guid.CreateVersion7()}"
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Adjust_belowReserved_returns422()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);
        var operatorClient = await factory.CreateClientAs(Role.Operator);
        await operatorClient.PostAsJsonAsync("/api/stock/reserve",
            new { productId, warehouseId, quantity = 8, idempotencyKey = $"r-{Guid.CreateVersion7()}" }, ct);

        var response = await client.PostAsJsonAsync("/api/stock/adjust", new
        {
            productId, warehouseId, onHandDelta = -5, reason = "Shrinkage",
            idempotencyKey = $"adj-{Guid.CreateVersion7()}"
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await factory.ReadStockAsync(productId, warehouseId)).OnHand.ShouldBe(10);
    }

    [Fact]
    public async Task Adjust_asOperator_returns403()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10, role: Role.Operator);

        var response = await client.PostAsJsonAsync("/api/stock/adjust", new
        {
            productId, warehouseId, onHandDelta = -1, reason = "Nope",
            idempotencyKey = $"adj-{Guid.CreateVersion7()}"
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Count_setsTheAbsoluteQuantity_andWritesTheDifferenceAsTheDelta()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var response = await client.PostAsJsonAsync("/api/stock/count", new
        {
            productId, warehouseId, observedOnHand = 7,
            reason = "Quarterly cycle count",
            idempotencyKey = $"cnt-{Guid.CreateVersion7()}"
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movement = await db.StockMovements.SingleAsync(m => m.Type == MovementType.Count, ct);
        movement.OnHandDelta.ShouldBe(-3);
        movement.OnHandAfter.ShouldBe(7);
    }

    [Fact]
    public async Task Count_stampsLastCountedAt()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);

        await client.PostAsJsonAsync("/api/stock/count", new
        {
            productId, warehouseId, observedOnHand = 9, reason = "Count",
            idempotencyKey = $"cnt-{Guid.CreateVersion7()}"
        }, ct);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.StockItems.AsNoTracking()
            .SingleAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId, ct);
        item.LastCountedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Count_belowReserved_namesTheReservedQuantity_notTheAvailableOne()
    {
        // The operator is being told to release reservations, so the number in the message
        // has to be the number of reservations. Reporting availability (on-hand minus
        // reserved) under the label "currently reserved" gives them a figure that appears
        // nowhere in the row and cannot tell them how much to release.
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);
        var operatorClient = await factory.CreateClientAs(Role.Operator);
        await operatorClient.PostAsJsonAsync("/api/stock/reserve",
            new { productId, warehouseId, quantity = 8, idempotencyKey = $"r-{Guid.CreateVersion7()}" }, ct);

        var response = await client.PostAsJsonAsync("/api/stock/count", new
        {
            productId, warehouseId, observedOnHand = 5, reason = "Count",
            idempotencyKey = $"cnt-{Guid.CreateVersion7()}"
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("stock.count_refused");

        var detail = problem.GetProperty("detail").GetString()!;
        detail.ShouldContain("8 unit(s)");
        detail.ShouldNotContain("2 unit(s)");
    }

    [Fact]
    public async Task Mutating_aPairingWithNoStockRow_returns404_namingBothIds()
    {
        // The missing thing is the (product, warehouse) pairing. "StockItem '<a product
        // id>' was not found" sends whoever reads it looking up a StockItem by an id that
        // is not a StockItem id, and never mentions which warehouse was asked for.
        var ct = Ct;
        var (client, productId, _) = await ArrangeAsync(onHand: 10);
        var elsewhere = await factory.SeedWarehouseAsync("ADJ-WH-EMPTY");

        var response = await client.PostAsJsonAsync("/api/stock/adjust", new
        {
            productId, warehouseId = elsewhere, onHandDelta = -1, reason = "Nothing here",
            idempotencyKey = $"adj-{Guid.CreateVersion7()}"
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("not_found");
        problem.GetProperty("productId").GetGuid().ShouldBe(productId);
        problem.GetProperty("warehouseId").GetGuid().ShouldBe(elsewhere);

        var detail = problem.GetProperty("detail").GetString()!;
        detail.ShouldContain(productId.ToString());
        detail.ShouldContain(elsewhere.ToString());
    }

    [Fact]
    public async Task Count_toTheSameQuantity_isRejectedRatherThanWritingAZeroDeltaMovement()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var response = await client.PostAsJsonAsync("/api/stock/count", new
        {
            productId, warehouseId, observedOnHand = 10, reason = "No change",
            idempotencyKey = $"cnt-{Guid.CreateVersion7()}"
        }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("stock.count_unchanged");
    }
}
