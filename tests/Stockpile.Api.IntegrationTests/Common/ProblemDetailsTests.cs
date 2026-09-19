using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Common;

[Collection(nameof(ApiCollection))]
public class ProblemDetailsTests(StockpileApiFactory factory)
{
    [Fact]
    public async Task ValidationFailure_returns400_withFieldLevelErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var response = await client.PostAsJsonAsync("/api/products",
            new { sku = "", name = "", category = "", unitPriceCents = -5, reorderPoint = 0, reorderQuantity = 0 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("validation.failed");
        problem.GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task NotFound_returns404_withTheErrorCode()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var response = await client.GetAsync($"/api/products/{Guid.CreateVersion7()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("not_found");
    }

    [Fact]
    public async Task ForbiddenRole_returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateClientAs(Role.Viewer);

        var response = await client.PostAsJsonAsync("/api/products",
            new { sku = "VIEW-01", name = "Nope", category = "X", unitPriceCents = 100, reorderPoint = 1, reorderQuantity = 1 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
