using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Entities;
using Stockpile.Domain.ValueObjects;
using Stockpile.Domain.Enums;
using Stockpile.Infrastructure.Identity;
using Stockpile.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Stockpile.Api.IntegrationTests;

public class StockpileApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The image goes to the constructor: Testcontainers 4.15 obsoletes the parameterless
    // PostgreSqlBuilder() in favour of naming the image up front.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("stockpile_test")
        .WithUsername("stockpile")
        .WithPassword("stockpile")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());

        // The Testing environment does not load appsettings.Development.json, so the JWT
        // settings are supplied here. The signing key must be at least 32 bytes for HS256.
        builder.UseSetting("Jwt:Issuer", "stockpile-test");
        builder.UseSetting("Jwt:Audience", "stockpile-test");
        builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-at-least-32-bytes-long!!");
        builder.UseSetting("Jwt:AccessTokenMinutes", "15");
        builder.UseSetting("Jwt:RefreshTokenDays", "14");

        // Routes that exercise the permission matrix and the exception-to-problem-document
        // mapping, added to the test host only; see PolicyProbeEndpoints and
        // FaultProbeEndpoints for why they exist at all.
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter>(new TestRouteStartupFilter(
                PolicyProbeEndpoints.DataSource, FaultProbeEndpoints.DataSource)));
    }

    // xUnit v3's IAsyncLifetime is ValueTask-based and inherits IAsyncDisposable, so
    // these signatures are ValueTask, not Task. Writing `Task InitializeAsync()` compiles
    // as an unrelated method, the container never starts, and every test fails on a
    // connection refused — with nothing in the output pointing at the real cause.
    // Both are virtual so the phase 04 two-instance fixture can supply its own containers.
    public virtual async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>
    /// For the few tests that need a connection the application does not own — proving a
    /// lock is actually held, for instance, which cannot be done from inside the same
    /// transaction that holds it.
    /// </summary>
    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// Truncates every table between tests. RESTART IDENTITY CASCADE keeps the
    /// append-only triggers happy: TRUNCATE is not an UPDATE or DELETE, so it is
    /// not blocked, which is exactly why the trigger is scoped to those two verbs.
    /// <para>
    /// The table list is read from the catalogue rather than hard-coded. The reason is
    /// ordering: ASP.NET Core Identity's asp_net_* tables and refresh_tokens do not
    /// exist until task 12's migration, so a literal list naming them cannot work in
    /// task 10 — and a literal list that omits them silently breaks every test seeding
    /// a FIXED email (AuthTests, the phase 07 seeder) on its second run, because the
    /// domain row is gone while the Identity credential survives. Reading pg_tables is
    /// correct at every phase and cannot drift as later phases add tables.
    /// </para>
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            DO $$
            DECLARE
                target_tables text;
            BEGIN
                SELECT string_agg(format('%I.%I', schemaname, tablename), ', ')
                INTO target_tables
                FROM pg_tables
                WHERE schemaname = 'public'
                  AND tablename <> '__EFMigrationsHistory';

                IF target_tables IS NOT NULL THEN
                    EXECUTE 'TRUNCATE TABLE ' || target_tables || ' RESTART IDENTITY CASCADE';
                END IF;
            END $$;
            """);
    }

    public async Task SeedUserAsync(string email, string password, Role role)
    {
        using var scope = CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StockpileIdentityUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var identityUser = new StockpileIdentityUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        (await users.CreateAsync(identityUser, password)).Succeeded.ShouldBeTrue();

        db.Users.Add(ApplicationUser.Create(identityUser.Id, email, email.Split('@')[0], role).Value);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The helper every later integration test uses to act as a given role. A unique email
    /// per call means tests never collide over a shared user, so the suite can run in
    /// parallel later without a mystery flake.
    /// </summary>
    public async Task<HttpClient> CreateClientAs(Role role)
    {
        var email = $"{role}-{Guid.CreateVersion7():N}@stockpile.test".ToLowerInvariant();
        const string password = "Str0ng!Passw0rd";
        await SeedUserAsync(email, password, role);

        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    public async Task<Guid> SeedWarehouseAsync(string code, WarehouseKind kind = WarehouseKind.Physical)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var warehouse = kind == WarehouseKind.Physical
            ? Warehouse.CreatePhysical(code, $"Warehouse {code}", null).Value
            : Warehouse.CreateInTransit(code, $"In transit {code}").Value;

        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();
        return warehouse.Id;
    }

    public async Task<Guid> SeedProductAsync(string sku, int reorderPoint = 0, int reorderQuantity = 1)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = Product.Create(
            Sku.Create(sku).Value, $"Product {sku}", null, "Test",
            unitPriceCents: 1000, barcode: null,
            reorderPoint: reorderPoint, reorderQuantity: reorderQuantity,
            createdBy: Guid.Empty, createdAt: DateTimeOffset.UtcNow).Value;

        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    /// <summary>
    /// Creates the stock row directly, bypassing the API, so tests can establish a precise
    /// starting quantity without depending on the receipt path — AND writes the matching
    /// opening-balance Receipt movement, so the ledger always explains the snapshot.
    /// Without that second row every reconciliation test would report a discrepancy that
    /// is an artefact of the fixture rather than a defect in the code under test.
    /// </summary>
    public async Task SeedStockAsync(Guid productId, Guid warehouseId, int onHand, long averageUnitCostCents = 1000)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO stock_items
                (id, product_id, warehouse_id, quantity_on_hand,
                 quantity_reserved, average_unit_cost_cents, bin_location, last_counted_at)
            VALUES ({0}, {1}, {2}, {3}, 0, {4}, NULL, NULL);
            """, Guid.CreateVersion7(), productId, warehouseId, onHand, averageUnitCostCents);

        if (onHand == 0)
            return;   // a zero-delta movement would breach movement_deltas_not_both_zero

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO stock_movements
                (id, product_id, warehouse_id, type, on_hand_delta, reserved_delta,
                 on_hand_after, reserved_after, unit_cost_cents, reference_type,
                 reference_id, idempotency_key, reason, occurred_at, performed_by_user_id)
            VALUES ({0}, {1}, {2}, 0, {3}, 0, {3}, 0, {4}, 'OpeningBalance',
                    NULL, {5}, 'Test fixture opening balance', now(), {6});
            """,
            Guid.CreateVersion7(), productId, warehouseId, onHand, averageUnitCostCents,
            $"opening-{Guid.CreateVersion7()}", Guid.Empty);
    }

    /// <summary>
    /// Runs a writer call inside a real ambient transaction, mirroring what
    /// TransactionBehavior does in production. Writer calls outside a transaction throw
    /// by design, so tests must not bypass this.
    /// </summary>
    public async Task<T> WithWriterAsync<T>(Func<IStockWriter, IAppDbContext, Task<T>> body)
    {
        using var scope = CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var writer = scope.ServiceProvider.GetRequiredService<IStockWriter>();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        return await unitOfWork.ExecuteInTransactionAsync(_ => body(writer, db));
    }

    public async Task<(int OnHand, int Reserved, long AverageCost)> ReadStockAsync(
        Guid productId, Guid warehouseId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.StockItems.AsNoTracking()
            .SingleAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);

        return (item.QuantityOnHand, item.QuantityReserved, item.AverageUnitCostCents);
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<StockpileApiFactory>;
