using System.Net;
using System.Text.Json;
using Shouldly;

namespace Stockpile.Api.IntegrationTests.Common;

[Collection(nameof(ApiCollection))]
public class OpenApiTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Document_isServed_andIsValidJson()
    {
        var ct = Ct;
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        document.RootElement.GetProperty("info").GetProperty("title").GetString()
            .ShouldBe("Stockpile API");
    }

    [Fact]
    public async Task Document_describesTheStockMutationEndpoints()
    {
        var ct = Ct;
        var document = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/openapi/v1.json", ct));

        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/stock/reserve", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/stock/reconcile", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Document_advertisesThe422OnInsufficientStock()
    {
        // A client integrating against this needs to know 422 is an expected outcome
        // of a correct request, not a bug. If it is missing from the document, the
        // .ProducesProblem() convention has been dropped from an endpoint.
        var ct = Ct;
        var document = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/openapi/v1.json", ct));

        document.RootElement
            .GetProperty("paths").GetProperty("/api/stock/reserve")
            .GetProperty("post").GetProperty("responses")
            .TryGetProperty("422", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Document_declaresBearerSecurity()
    {
        var ct = Ct;
        var document = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/openapi/v1.json", ct));

        document.RootElement.GetProperty("components")
            .GetProperty("securitySchemes").TryGetProperty("Bearer", out var scheme)
            .ShouldBeTrue();
        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");
    }

    [Fact]
    public async Task ScalarUi_isServed()
    {
        var ct = Ct;
        var response = await factory.CreateClient().GetAsync("/scalar/v1", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain("Stockpile");
    }
}
