using Stockpile.Api.Authorization;
using Stockpile.Domain.Common;
using Stockpile.Infrastructure.Identity;

namespace Stockpile.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record RefreshRequest(string RefreshToken);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (
            LoginRequest request, AuthenticationService auth, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request.Email, request.Password, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Unauthorized(result.Error);
        }).AllowAnonymous();

        group.MapPost("/refresh", async (
            RefreshRequest request, AuthenticationService auth, CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(request.RefreshToken, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Unauthorized(result.Error);
        }).AllowAnonymous();

        group.MapPost("/logout", async (
            RefreshRequest request, AuthenticationService auth, CancellationToken ct) =>
        {
            await auth.LogoutAsync(request.RefreshToken, ct);
            return Results.NoContent();
        }).AllowAnonymous();

        group.MapGet("/me", async (AuthenticationService auth, CancellationToken ct) =>
        {
            var result = await auth.MeAsync(ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(
                    title: "Not found",
                    detail: result.Error.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
        }).RequireAuthorization(Policies.CanView);
    }

    /// <summary>
    /// RFC 7807 problem document carrying the machine-readable error code, and nothing
    /// that distinguishes an unknown email from a wrong password.
    /// </summary>
    private static IResult Unauthorized(Error error) =>
        Results.Problem(
            title: "Unauthorized",
            detail: error.Message,
            statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
