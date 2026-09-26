namespace Stockpile.Api.Endpoints;

/// <summary>
/// The body of every order or transfer transition that moves stock; the id is in the route.
/// One type for all of them, so the OpenAPI document carries one schema rather than two
/// identically named ones competing for the same id.
/// </summary>
public sealed record IdempotentRequest(string IdempotencyKey);
