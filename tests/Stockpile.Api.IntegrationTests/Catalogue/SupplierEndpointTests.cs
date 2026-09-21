using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Catalogue;

[Collection(nameof(ApiCollection))]
public class SupplierEndpointTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object ValidSupplier(string code) => new
    {
        code,
        name = $"Supplier {code}",
        email = "orders@supplier.test",
        phone = "+31 20 123 4567",
        address = "1 Dock Road, Rotterdam"
    };

    [Fact]
    public async Task Create_asWarehouseManager_returns201()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var response = await client.PostAsJsonAsync("/api/suppliers", ValidSupplier("SUP-A1"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("code").GetString().ShouldBe("SUP-A1");
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        response.Headers.Location.ShouldNotBeNull();
    }

    [Fact]
    public async Task Create_withADuplicateCode_returns422()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);
        await client.PostAsJsonAsync("/api/suppliers", ValidSupplier("SUP-DUP"), ct);

        var second = await client.PostAsJsonAsync("/api/suppliers", ValidSupplier("SUP-DUP"), ct);

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("supplier.code_taken");
    }

    [Fact]
    public async Task Create_asOperator_returns403()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.Operator);

        (await client.PostAsJsonAsync("/api/suppliers", ValidSupplier("SUP-OPS"), ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patch_updatesOnlyTheFieldsSent()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);
        var created = await (await client.PostAsJsonAsync("/api/suppliers", ValidSupplier("SUP-PAT"), ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        var id = created.GetProperty("id").GetGuid();
        var rowVersion = created.GetProperty("rowVersion").GetUInt32();

        var patched = await client.PatchAsJsonAsync($"/api/suppliers/{id}",
            new { name = "Renamed Supplier", rowVersion }, ct);

        patched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await patched.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("name").GetString().ShouldBe("Renamed Supplier");

        // Untouched fields survive the partial update.
        body.GetProperty("email").GetString().ShouldBe("orders@supplier.test");
        body.GetProperty("code").GetString().ShouldBe("SUP-PAT");
    }

    [Fact]
    public async Task Patch_withAStaleRowVersion_returns409()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.WarehouseManager);
        var created = await (await client.PostAsJsonAsync("/api/suppliers", ValidSupplier("SUP-CNC"), ct))
            .Content.ReadFromJsonAsync<JsonElement>(ct);

        var id = created.GetProperty("id").GetGuid();
        var staleVersion = created.GetProperty("rowVersion").GetUInt32();

        (await client.PatchAsJsonAsync($"/api/suppliers/{id}",
            new { name = "First", rowVersion = staleVersion }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await client.PatchAsJsonAsync($"/api/suppliers/{id}",
            new { name = "Second", rowVersion = staleVersion }, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("entityType").GetString().ShouldBe("Supplier");
    }

    [Fact]
    public async Task Search_matchesCodeAndName_caseInsensitively()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        await client.PostAsJsonAsync("/api/suppliers", ValidSupplier("NORTHWIND"), ct);
        await client.PostAsJsonAsync("/api/suppliers", ValidSupplier("SOUTHSEA"), ct);

        var page = await client.GetFromJsonAsync<JsonElement>("/api/suppliers?search=northw", ct);

        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
        page.GetProperty("items").EnumerateArray().Single()
            .GetProperty("code").GetString().ShouldBe("NORTHWIND");
    }

    [Fact]
    public async Task Search_asViewer_isAllowed()
    {
        var ct = Ct;
        var client = await factory.CreateClientAs(Role.Viewer);

        (await client.GetAsync("/api/suppliers", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
