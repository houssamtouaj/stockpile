using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class AuthTests(StockpileApiFactory factory)
{
    private sealed record LoginRequest(string Email, string Password);
    private sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    [Fact]
    public async Task Login_withValidCredentials_returnsTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.SeedUserAsync("manager@stockpile.test", "Str0ng!Passw0rd", Role.WarehouseManager);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("manager@stockpile.test", "Str0ng!Passw0rd"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        body.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_withWrongPassword_returns401_andNoTokenLeak()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.SeedUserAsync("viewer@stockpile.test", "Str0ng!Passw0rd", Role.Viewer);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("viewer@stockpile.test", "wrong"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(ct)).ShouldNotContain("accessToken");
    }

    [Fact]
    public async Task Login_withUnknownEmail_returns401_withTheSameMessageAsAWrongPassword()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody@stockpile.test", "whatever"), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_withAValidToken_returnsTheUserAndRole()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateClientAs(Role.WarehouseManager);

        var response = await client.GetAsync("/api/auth/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(ct)).ShouldContain("WarehouseManager");
    }

    [Fact]
    public async Task Me_withoutAToken_returns401()
    {
        var ct = TestContext.Current.CancellationToken;

        (await factory.CreateClient().GetAsync("/api/auth/me", ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_withAValidRefreshToken_issuesANewAccessToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.SeedUserAsync("ops@stockpile.test", "Str0ng!Passw0rd", Role.Operator);
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("ops@stockpile.test", "Str0ng!Passw0rd"), ct))
            .Content.ReadFromJsonAsync<LoginResponse>(ct);

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login!.RefreshToken }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        refreshed!.AccessToken.ShouldNotBe(login.AccessToken);
    }

    [Fact]
    public async Task Refresh_withAnAlreadyUsedRefreshToken_returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.SeedUserAsync("rot@stockpile.test", "Str0ng!Passw0rd", Role.Operator);
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("rot@stockpile.test", "Str0ng!Passw0rd"), ct))
            .Content.ReadFromJsonAsync<LoginResponse>(ct);

        await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login!.RefreshToken }, ct);
        var second = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.RefreshToken }, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
