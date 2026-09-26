using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Orders;

/// <summary>
/// Domain events are never dispatched, so the order board's live updates depend on every
/// transition handler enqueueing OrderStatusChangedNotification itself — the producer the
/// phase 04 broadcast tests are waiting on. A spy publisher records what is flushed, which
/// only happens after the transaction commits, so a refused transition shows up as missing.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class OrderStatusNotificationTests(StockpileApiFactory factory) : IAsyncDisposable
{
    private readonly ConcurrentQueue<IRealtimeNotification> _flushed = new();
    private WebApplicationFactory<Program>? _app;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EverySalesOrderTransition_flushesItsResultingStatus()
    {
        var (manager, ops) = await ArrangeAsync();
        var warehouse = await factory.SeedWarehouseAsync("NT-WH");
        var product = await factory.SeedProductAsync("NT-001");
        await factory.SeedStockAsync(product, warehouse, 50);
        var customer = await factory.SeedCustomerAsync("Notify SA");
        object body = new
        {
            customerId = customer,
            warehouseId = warehouse,
            lines = new[] { new { productId = product, quantity = 5, unitPriceCents = 100L } }
        };

        var shipped = await IdOfAsync(await manager.PostAsJsonAsync("/api/sales-orders", body, Ct));
        await OkAsync(manager, $"/api/sales-orders/{shipped}/confirm", Key());
        var line = (await manager.GetFromJsonAsync<JsonElement>($"/api/sales-orders/{shipped}", Ct))
            .GetProperty("lines")[0].GetProperty("id").GetGuid();
        await OkAsync(ops, $"/api/sales-orders/{shipped}/pick", new { lineId = line, quantity = 5 });
        await OkAsync(ops, $"/api/sales-orders/{shipped}/pack", null);
        await OkAsync(manager, $"/api/sales-orders/{shipped}/ship", Key());

        var cancelled = await IdOfAsync(await manager.PostAsJsonAsync("/api/sales-orders", body, Ct));
        await OkAsync(manager, $"/api/sales-orders/{cancelled}/cancel", Key());

        StatusesOf(shipped).ShouldBe(["Draft", "Confirmed", "Picking", "Packed", "Shipped"]);
        StatusesOf(cancelled).ShouldBe(["Draft", "Cancelled"]);
    }

    [Fact]
    public async Task EveryPurchaseOrderTransition_flushesItsResultingStatus()
    {
        var (manager, _) = await ArrangeAsync();
        var warehouse = await factory.SeedWarehouseAsync("NT-WH");
        var product = await factory.SeedProductAsync("NT-002");
        var supplier = await factory.SeedSupplierAsync("NT-SUP");
        object body = new
        {
            supplierId = supplier,
            warehouseId = warehouse,
            lines = new[] { new { productId = product, quantity = 10, unitCostCents = 100L } }
        };

        var received = await IdOfAsync(await manager.PostAsJsonAsync("/api/purchase-orders", body, Ct));
        await OkAsync(manager, $"/api/purchase-orders/{received}/submit", null);
        var line = (await manager.GetFromJsonAsync<JsonElement>($"/api/purchase-orders/{received}", Ct))
            .GetProperty("lines")[0].GetProperty("id").GetGuid();
        await OkAsync(manager, $"/api/purchase-orders/{received}/receive",
            new { lineId = line, quantity = 4, idempotencyKey = $"nt-{Guid.CreateVersion7()}" });
        await OkAsync(manager, $"/api/purchase-orders/{received}/receive",
            new { lineId = line, quantity = 6, idempotencyKey = $"nt-{Guid.CreateVersion7()}" });

        var cancelled = await IdOfAsync(await manager.PostAsJsonAsync("/api/purchase-orders", body, Ct));
        await OkAsync(manager, $"/api/purchase-orders/{cancelled}/cancel", null);

        StatusesOf(received).ShouldBe(["Draft", "Submitted", "PartiallyReceived", "Received"]);
        StatusesOf(cancelled).ShouldBe(["Draft", "Cancelled"]);
    }

    [Fact]
    public async Task EveryTransferTransition_flushesItsResultingStatus_toTheBoardsItChanges()
    {
        var (manager, ops) = await ArrangeAsync();
        var from = await factory.SeedWarehouseAsync("NT-FROM");
        var to = await factory.SeedWarehouseAsync("NT-TO");
        var inTransit = await factory.SeedWarehouseAsync("NT-IT", WarehouseKind.InTransit);
        var product = await factory.SeedProductAsync("NT-003");
        await factory.SeedStockAsync(product, from, 50);
        object body = new
        {
            fromWarehouseId = from,
            toWarehouseId = to,
            inTransitWarehouseId = inTransit,
            lines = new[] { new { productId = product, quantity = 5 } }
        };

        var moved = await IdOfAsync(await manager.PostAsJsonAsync("/api/transfers", body, Ct));
        await OkAsync(ops, $"/api/transfers/{moved}/dispatch", Key());
        await OkAsync(ops, $"/api/transfers/{moved}/receive", Key());

        var cancelled = await IdOfAsync(await manager.PostAsJsonAsync("/api/transfers", body, Ct));
        await OkAsync(manager, $"/api/transfers/{cancelled}/cancel", null);

        // Dispatch changes both boards: the source's stock left, the destination's is inbound.
        Notifications(moved).Select(n => (n.Status, n.WarehouseId)).ShouldBe(
        [
            ("Draft", from), ("InTransit", from), ("InTransit", to), ("Received", to)
        ]);
        StatusesOf(cancelled).ShouldBe(["Draft", "Cancelled"]);
    }

    [Fact]
    public async Task ARefusedTransition_flushesNothing()
    {
        var (manager, _) = await ArrangeAsync();
        var warehouse = await factory.SeedWarehouseAsync("NT-WH");
        var product = await factory.SeedProductAsync("NT-004");
        await factory.SeedStockAsync(product, warehouse, 1);
        var customer = await factory.SeedCustomerAsync("Notify SA");

        var id = await IdOfAsync(await manager.PostAsJsonAsync("/api/sales-orders", new
        {
            customerId = customer,
            warehouseId = warehouse,
            lines = new[] { new { productId = product, quantity = 5, unitPriceCents = 100L } }
        }, Ct));

        (await manager.PostAsJsonAsync($"/api/sales-orders/{id}/confirm", Key(), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        StatusesOf(id).ShouldBe(["Draft"]);
    }

    private async Task<(HttpClient Manager, HttpClient Operator)> ArrangeAsync()
    {
        await factory.ResetDatabaseAsync();

        _app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<INotificationPublisher>(_ => new RecordingPublisher(_flushed))));

        return (await ClientAsAsync(Role.WarehouseManager), await ClientAsAsync(Role.Operator));
    }

    private async Task<HttpClient> ClientAsAsync(Role role)
    {
        var email = $"{role}-{Guid.CreateVersion7():N}@stockpile.test".ToLowerInvariant();
        const string password = "Str0ng!Passw0rd";
        await factory.SeedUserAsync(email, password, role);

        var client = _app!.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private IEnumerable<OrderStatusChangedNotification> Notifications(Guid orderId) =>
        _flushed.OfType<OrderStatusChangedNotification>().Where(n => n.OrderId == orderId);

    private string[] StatusesOf(Guid orderId) => Notifications(orderId).Select(n => n.Status).ToArray();

    private static object Key() => new { idempotencyKey = $"nt-{Guid.CreateVersion7()}" };

    private static async Task OkAsync(HttpClient client, string route, object? body)
    {
        var response = body is null
            ? await client.PostAsync(route, null, Ct)
            : await client.PostAsJsonAsync(route, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    /// <summary>Scoped like the real queue; hands what it flushes to a log the test reads.</summary>
    private sealed class RecordingPublisher(ConcurrentQueue<IRealtimeNotification> flushed) : INotificationPublisher
    {
        private readonly List<IRealtimeNotification> _pending = [];

        public void Enqueue(IRealtimeNotification notification) => _pending.Add(notification);

        public void Clear() => _pending.Clear();

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            foreach (var notification in _pending)
                flushed.Enqueue(notification);

            _pending.Clear();
            return Task.CompletedTask;
        }
    }
}
