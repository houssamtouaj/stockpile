using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Stock;

[Collection(nameof(ApiCollection))]
public class MovementQueryTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, Guid ProductId, Guid WarehouseId)> ArrangeAsync(int movements)
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("MOV-001");
        var warehouseId = await factory.SeedWarehouseAsync("MOV-WH");
        await factory.SeedStockAsync(productId, warehouseId, 1000);

        var client = await factory.CreateClientAs(Role.Operator);
        for (var i = 0; i < movements; i++)
        {
            await client.PostAsJsonAsync("/api/stock/reserve", new
            {
                productId, warehouseId, quantity = 1,
                idempotencyKey = $"mov-{i}-{Guid.CreateVersion7()}"
            }, ct);
        }

        return (client, productId, warehouseId);
    }

    [Fact]
    public async Task Movements_areReturnedNewestFirst()
    {
        var ct = Ct;
        var (client, productId, _) = await ArrangeAsync(movements: 5);

        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/stock/movements?productId={productId}&size=10", ct);

        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(6);   // 5 holds + the seeded opening receipt

        var timestamps = items.Select(i => i.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        timestamps.ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task Movements_pageForwardWithTheCursor_withoutRepeatingOrSkipping()
    {
        var ct = Ct;
        var (client, productId, _) = await ArrangeAsync(movements: 24);

        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var url = $"/api/stock/movements?productId={productId}&size=10"
                      + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await client.GetFromJsonAsync<JsonElement>(url, ct);

            seen.AddRange(page.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetGuid()));

            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        seen.Count.ShouldBe(25);                 // 24 holds + opening receipt
        seen.Distinct().Count().ShouldBe(25);    // no repeats
    }

    [Fact]
    public async Task Movements_filterByType()
    {
        var ct = Ct;
        var (client, productId, _) = await ArrangeAsync(movements: 3);

        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/stock/movements?productId={productId}&type=ReservationHold&size=50", ct);

        page.GetProperty("items").EnumerateArray()
            .ShouldAllBe(i => i.GetProperty("type").GetString() == "ReservationHold");
    }

    [Fact]
    public async Task Movements_filterByDateRange()
    {
        var ct = Ct;
        var (client, productId, _) = await ArrangeAsync(movements: 3);
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToString("O");

        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/stock/movements?productId={productId}&from={Uri.EscapeDataString(tomorrow)}&size=50", ct);

        page.GetProperty("items").GetArrayLength().ShouldBe(0);
        page.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task StockLevels_reportAvailableAsOnHandMinusReserved()
    {
        var ct = Ct;
        var (client, productId, warehouseId) = await ArrangeAsync(movements: 4);

        var levels = await client.GetFromJsonAsync<JsonElement>(
            $"/api/stock?productId={productId}&warehouseId={warehouseId}", ct);

        var row = levels.EnumerateArray().Single();
        row.GetProperty("quantityOnHand").GetInt32().ShouldBe(1000);
        row.GetProperty("quantityReserved").GetInt32().ShouldBe(4);
        row.GetProperty("quantityAvailable").GetInt32().ShouldBe(996);
    }

    [Fact]
    public async Task WarehouseStock_withLowStockOnly_returnsOnlyItemsAtOrBelowTheReorderPoint()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var warehouseId = await factory.SeedWarehouseAsync("LOW-WH");
        var lowProduct = await factory.SeedProductAsync("LOW-001", reorderPoint: 50);
        var healthyProduct = await factory.SeedProductAsync("LOW-002", reorderPoint: 5);
        await factory.SeedStockAsync(lowProduct, warehouseId, 10);
        await factory.SeedStockAsync(healthyProduct, warehouseId, 100);

        var client = await factory.CreateClientAs(Role.Viewer);
        var levels = await client.GetFromJsonAsync<JsonElement>(
            $"/api/warehouses/{warehouseId}/stock?lowStockOnly=true", ct);

        var rows = levels.EnumerateArray().ToList();
        rows.Count.ShouldBe(1);
        rows[0].GetProperty("sku").GetString().ShouldBe("LOW-001");
        rows[0].GetProperty("isLowStock").GetBoolean().ShouldBeTrue();
    }
}
