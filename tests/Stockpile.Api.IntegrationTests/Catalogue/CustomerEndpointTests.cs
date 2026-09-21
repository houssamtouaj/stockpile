using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Catalogue;

[Collection(nameof(ApiCollection))]
public class CustomerEndpointTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object ValidCustomer(string name) => new
    {
        name,
        email = $"{name.Replace(' ', '.').ToLowerInvariant()}@customer.test",
        phone = "+44 20 7946 0000",
        shippingAddress = "12 Harbour Street, Bristol"
    };

    [Fact]
    public async Task Create_asWarehouseManager_returns201()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var response = await client.PostAsJsonAsync("/api/customers", ValidCustomer("Acme Retail"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("name").GetString().ShouldBe("Acme Retail");
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        response.Headers.Location.ShouldNotBeNull();
    }

    [Fact]
    public async Task Create_withoutAName_returns400()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var response = await client.PostAsJsonAsync("/api/customers",
            new { name = "", email = "nobody@customer.test" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("validation.failed");
    }

    [Fact]
    public async Task Create_asOperator_returns403()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.Operator);

        (await client.PostAsJsonAsync("/api/customers", ValidCustomer("Nope Ltd"), ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patch_updatesOnlyTheFieldsSent()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);
        var created = await (await client.PostAsJsonAsync("/api/customers", ValidCustomer("Patch Co"), ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        var id = created.GetProperty("id").GetGuid();
        var rowVersion = created.GetProperty("rowVersion").GetUInt32();
        var originalEmail = created.GetProperty("email").GetString();

        var patched = await client.PatchAsJsonAsync($"/api/customers/{id}",
            new { shippingAddress = "99 New Wharf, Leeds", rowVersion }, ct);

        patched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await patched.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("shippingAddress").GetString().ShouldBe("99 New Wharf, Leeds");
        body.GetProperty("name").GetString().ShouldBe("Patch Co");
        body.GetProperty("email").GetString().ShouldBe(originalEmail);
    }

    [Fact]
    public async Task Patch_withAStaleRowVersion_returns409()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);
        var created = await (await client.PostAsJsonAsync("/api/customers", ValidCustomer("Conflict Co"), ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        var id = created.GetProperty("id").GetGuid();
        var staleVersion = created.GetProperty("rowVersion").GetUInt32();

        (await client.PatchAsJsonAsync($"/api/customers/{id}",
            new { name = "First", rowVersion = staleVersion }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await client.PatchAsJsonAsync($"/api/customers/{id}",
            new { name = "Second", rowVersion = staleVersion }, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("entityType").GetString().ShouldBe("Customer");
    }

    [Fact]
    public async Task Patch_anUnknownCustomer_returns404()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var response = await client.PatchAsJsonAsync($"/api/customers/{Guid.CreateVersion7()}",
            new { name = "Ghost", rowVersion = 1u }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_matchesNameCaseInsensitively_andPages()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        await client.PostAsJsonAsync("/api/customers", ValidCustomer("Riverside Traders"), ct);
        await client.PostAsJsonAsync("/api/customers", ValidCustomer("Hillside Traders"), ct);
        await client.PostAsJsonAsync("/api/customers", ValidCustomer("Unrelated Buyer"), ct);

        var page = await client.GetFromJsonAsync<JsonElement>(
            "/api/customers?search=TRADERS&page=1&size=1", ct);

        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        page.GetProperty("items").GetArrayLength().ShouldBe(1);
        page.GetProperty("hasMore").GetBoolean().ShouldBeTrue();
    }
}
