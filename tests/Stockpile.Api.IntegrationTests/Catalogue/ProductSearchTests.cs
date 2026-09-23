using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests.Catalogue;

[Collection(nameof(ApiCollection))]
public class ProductSearchTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Product(string sku, string name, string category, string? barcode = null) => new
    {
        sku,
        name,
        description = (string?)null,
        category,
        unitPriceCents = 500L,
        barcode,
        reorderPoint = 5,
        reorderQuantity = 10
    };

    private async Task<HttpClient> SeedCatalogueAsync()
    {
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        await client.PostAsJsonAsync("/api/products",
            Product("HEX-BOLT-01", "Hex Bolt M8", "Fasteners", "5011111111111"), ct);
        await client.PostAsJsonAsync("/api/products",
            Product("HEX-NUT-01", "Hex Nut M8", "Fasteners"), ct);
        await client.PostAsJsonAsync("/api/products",
            Product("PAINT-RED-5L", "Red Paint 5L", "Coatings"), ct);

        return client;
    }

    [Fact]
    public async Task Search_byName_isCaseInsensitive()
    {
        var ct = Ct;
        var client = await SeedCatalogueAsync();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/products?search=hex%20nut", ct);

        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
        page.GetProperty("items").EnumerateArray().Single()
            .GetProperty("sku").GetString().ShouldBe("HEX-NUT-01");
    }

    /// <summary>
    /// SKU is matched exactly, on a normalised term, rather than with a LIKE: the column
    /// is value-converted, so EF coerces any operand compared against it through the Sku
    /// converter and a '%term%' pattern cannot survive that. Partial code entry is served
    /// by the dedicated /by-barcode fast path instead.
    /// </summary>
    [Fact]
    public async Task Search_bySku_isCaseInsensitive()
    {
        var ct = Ct;
        var client = await SeedCatalogueAsync();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/products?search=paint-red-5l", ct);

        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
        page.GetProperty("items").EnumerateArray().Single()
            .GetProperty("sku").GetString().ShouldBe("PAINT-RED-5L");
    }

    [Fact]
    public async Task Search_filtersByCategory()
    {
        var ct = Ct;
        var client = await SeedCatalogueAsync();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/products?category=Fasteners", ct);

        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        page.GetProperty("items").EnumerateArray()
            .ShouldAllBe(i => i.GetProperty("category").GetString() == "Fasteners");
    }

    [Fact]
    public async Task Search_pagesWithoutRepeating()
    {
        var ct = Ct;
        var client = await SeedCatalogueAsync();

        var first = await client.GetFromJsonAsync<JsonElement>("/api/products?page=1&size=2", ct);
        var second = await client.GetFromJsonAsync<JsonElement>("/api/products?page=2&size=2", ct);

        first.GetProperty("items").GetArrayLength().ShouldBe(2);
        first.GetProperty("hasMore").GetBoolean().ShouldBeTrue();
        second.GetProperty("items").GetArrayLength().ShouldBe(1);
        second.GetProperty("hasMore").GetBoolean().ShouldBeFalse();

        var ids = first.GetProperty("items").EnumerateArray()
            .Concat(second.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("id").GetGuid())
            .ToList();

        ids.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Search_treatsWildcardsAsLiteralCharacters()
    {
        // An unescaped pattern hands the user's own characters to the matcher: "%" on its
        // own returns the whole catalogue a page at a time, and "_" matches any character.
        // A search box means the text that was typed.
        var ct = Ct;
        var client = await SeedCatalogueAsync();
        await client.PostAsJsonAsync("/api/products",
            Product("DISC-50", "50% Off Sticker", "Labels"), ct);

        var percent = await client.GetFromJsonAsync<JsonElement>("/api/products?search=50%25", ct);
        percent.GetProperty("totalCount").GetInt32().ShouldBe(1);
        percent.GetProperty("items").EnumerateArray().Single()
            .GetProperty("sku").GetString().ShouldBe("DISC-50");

        // A lone wildcard finds the one name that literally contains a percent sign,
        // rather than paging through the entire catalogue.
        var lone = await client.GetFromJsonAsync<JsonElement>("/api/products?search=%25", ct);
        lone.GetProperty("totalCount").GetInt32().ShouldBe(1);

        // Underscore is a single-character wildcard unescaped, so "He_ Nut" would match
        // "Hex Nut M8".
        var underscore = await client.GetFromJsonAsync<JsonElement>("/api/products?search=He_%20Nut", ct);
        underscore.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Search_pagesStably_whenNamesCollide()
    {
        // OrderBy(Name) alone leaves rows sharing a name in whatever order the plan
        // happens to produce, and that order need not hold across the two queries a client
        // makes for page 1 and page 2: one row comes back twice and another never appears.
        var ct = Ct;
        await factory.ResetDatabaseAsync();
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        foreach (var suffix in new[] { "A", "B", "C", "D" })
        {
            await client.PostAsJsonAsync("/api/products",
                Product($"DUPE-{suffix}", "Identical Name", "Fasteners"), ct);
        }

        var ids = new List<Guid>();
        for (var page = 1; page <= 2; page++)
        {
            var body = await client.GetFromJsonAsync<JsonElement>(
                $"/api/products?search=identical&page={page}&size=2", ct);
            ids.AddRange(body.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetGuid()));
        }

        ids.Count.ShouldBe(4);
        ids.Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task ByBarcode_returnsTheProduct()
    {
        var ct = Ct;
        var client = await SeedCatalogueAsync();

        var response = await client.GetAsync("/api/products/by-barcode/5011111111111", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("sku").GetString().ShouldBe("HEX-BOLT-01");
    }

    [Fact]
    public async Task ByBarcode_whenUnknown_returns404()
    {
        var ct = Ct;
        var client = await SeedCatalogueAsync();

        var response = await client.GetAsync("/api/products/by-barcode/0000000000000", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("errorCode").GetString().ShouldBe("not_found");
    }

    [Fact]
    public async Task Search_asViewer_isAllowed()
    {
        var ct = Ct;
        await SeedCatalogueAsync();
        var viewer = await factory.CreateClientAs(Role.Viewer);

        (await viewer.GetAsync("/api/products?search=hex", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
