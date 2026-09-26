using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Orders;

/// <summary>
/// Total valuation — SUM(on_hand x average cost) — must survive every transfer step, not
/// just the simple one-transfer cycle in TransferTests: a cost read that races a receipt,
/// and two transfers of one product in flight at different costs.
/// </summary>
[Collection(nameof(ApiCollection))]
public class TransferValuationTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dispatch_racingAReceiptAtTheSource_removesAndAddsValueAtTheSameCost()
    {
        // The source's cost must be read under the same lock as the outbound write. Read
        // unlocked, a receipt that commits in between moves the average: the outbound leg
        // then removes 40 units at the NEW average while the inbound leg adds them at the
        // OLD one, and value is created or destroyed by the transfer itself.
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var from = await factory.SeedWarehouseAsync("VAL-FROM");
        var to = await factory.SeedWarehouseAsync("VAL-TO");
        var inTransit = await factory.SeedWarehouseAsync("VAL-IT", WarehouseKind.InTransit);
        var productId = await factory.SeedProductAsync("VAL-001");
        await factory.SeedStockAsync(productId, from, 100, averageUnitCostCents: 250);
        var manager = await factory.CreateClientAs(Role.WarehouseManager);
        var ops = await factory.CreateClientAs(Role.Operator);

        var id = await CreateTransferAsync(manager, from, to, inTransit, productId, 40);

        // Stand in for a purchase-order receipt in flight at the source: hold its row.
        await using var receipt = new NpgsqlConnection(factory.ConnectionString);
        await receipt.OpenAsync(ct);
        await using var held = await receipt.BeginTransactionAsync(ct);
        await ExecuteAsync(receipt, held,
            $"SELECT 1 FROM stock_items WHERE product_id = '{productId}' AND warehouse_id = '{from}' FOR UPDATE");

        var dispatch = ops.PostAsJsonAsync($"/api/transfers/{id}/dispatch",
            new { idempotencyKey = $"disp-{Guid.CreateVersion7()}" }, ct);
        await WaitUntilSomeoneIsBlockedOnALockAsync(dispatch);

        // 100 more units at 650 land while the dispatch waits: average 250 -> 450.
        await ExecuteAsync(receipt, held,
            $"UPDATE stock_items SET quantity_on_hand = 200, average_unit_cost_cents = 450 "
            + $"WHERE product_id = '{productId}' AND warehouse_id = '{from}'");
        await held.CommitAsync(ct);
        var afterReceipt = await factory.TotalValuationAsync();
        afterReceipt.ShouldBe(200 * 450);

        (await dispatch).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await factory.ReadStockAsync(productId, inTransit)).AverageCost.ShouldBe(450);
        (await factory.TotalValuationAsync()).ShouldBe(afterReceipt);
    }

    private async Task WaitUntilSomeoneIsBlockedOnALockAsync(Task request)
    {
        await using var probe = new NpgsqlConnection(factory.ConnectionString);
        await probe.OpenAsync(Ct);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            request.IsCompleted.ShouldBeFalse("the dispatch finished without waiting for the held source row");

            await using var command = probe.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM pg_stat_activity "
                + "WHERE wait_event_type = 'Lock' AND datname = current_database()";
            if ((long)(await command.ExecuteScalarAsync(Ct))! > 0)
                return;

            await Task.Delay(50, Ct);
        }

        throw new ShouldAssertException("the dispatch never blocked on the held source row");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<Guid> CreateTransferAsync(
        HttpClient client, Guid from, Guid to, Guid inTransit, Guid productId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/api/transfers", new
        {
            fromWarehouseId = from,
            toWarehouseId = to,
            inTransitWarehouseId = inTransit,
            lines = new[] { new { productId, quantity } }
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }
}
