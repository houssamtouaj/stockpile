using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests;

/// <summary>
/// Test-only routes that throw, so the exception-to-problem-document mapping can be asserted.
/// <para>
/// They exist because the exceptions worth mapping are the ones no well-formed request can
/// reach: <c>StockInvariantViolatedException</c> fires only when a CHECK constraint catches
/// something the conditional UPDATE predicates were supposed to make impossible, so there is
/// no request that produces it while the writers are correct. Throwing it directly is the
/// honest way to cover the handler without first breaking the code it is a backstop for.
/// </para>
/// </summary>
internal static class FaultProbeEndpoints
{
    public const string StockInvariantRoute = "/test/fault/stock-invariant";
    public const string UnexpectedRoute = "/test/fault/unexpected";

    public const string ConstraintName = "stock_never_negative";

    public static EndpointDataSource DataSource { get; } = new DefaultEndpointDataSource(
    [
        Throws(StockInvariantRoute, () => new StockInvariantViolatedException(ConstraintName)),
        Throws(UnexpectedRoute, () => new InvalidOperationException("probe: unmapped failure"))
    ]);

    private static Endpoint Throws(string route, Func<Exception> exception) =>
        new RouteEndpointBuilder(
            requestDelegate: _ => throw exception(),
            routePattern: RoutePatternFactory.Parse(route),
            order: 0)
        {
            DisplayName = $"fault-probe:{route}",
            Metadata = { new HttpMethodMetadata([HttpMethods.Get]) }
        }.Build();
}
