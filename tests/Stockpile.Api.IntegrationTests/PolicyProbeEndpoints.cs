using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Stockpile.Api.Authorization;

namespace Stockpile.Api.IntegrationTests;

/// <summary>
/// Test-only routes, one per role-bearing policy in <see cref="Policies"/>, registered on the
/// test host through DI so the real <c>Program</c> keeps no test scaffolding.
/// <para>
/// They exist because phase 01 ships the permission matrix but no endpoint that uses it, and the
/// matrix rests on a claim type surviving the trip from <c>JwtTokenService</c> to
/// <c>RequireRole</c>. <c>GET /api/auth/me</c> does not cover it — the role in that response is
/// read from the database, not from the claim.
/// </para>
/// <para>
/// What the trip actually looks like today, checked rather than assumed: the token is built by
/// the <c>JwtSecurityToken</c> constructor, which writes each claim type verbatim and does NOT
/// apply the outbound claim-type map, so the payload carries the full
/// <c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c> name rather than the
/// compact <c>role</c>. Inbound that already equals <c>ClaimTypes.Role</c>, which is the default
/// <c>TokenValidationParameters.RoleClaimType</c> — so nothing depends on
/// <c>MapInboundClaims</c> (verified: setting it to <c>false</c> changes no result here).
/// Switching to <c>SecurityTokenDescriptor</c> / <c>JsonWebTokenHandler</c> WOULD apply the
/// outbound map and emit <c>role</c> instead, at which point the two halves have to agree
/// again. These tests are what turns that into a red build rather than a 403 in phase 02.
/// </para>
/// <para>
/// Delete these once phase 02 maps real endpoints behind the same policies, and point
/// <see cref="RolePolicyTests"/> at those instead.
/// </para>
/// </summary>
internal static class PolicyProbeEndpoints
{
    public static string RouteFor(string policy) => $"/test/policy/{policy}";

    /// <summary>
    /// Adds the probe routes to the endpoint route builder the app itself used. An
    /// <see cref="EndpointDataSource"/> registered in DI is NOT picked up — the routing
    /// middleware matches against the data sources collected by <c>UseEndpoints</c>, so the
    /// routes have to join that collection. It is an <see cref="ObservableCollection{T}"/>
    /// on purpose, which is what makes this late addition work.
    /// </summary>
    internal sealed class StartupFilter : IStartupFilter
    {
        // The key WebApplication stashes itself under, as IEndpointRouteBuilder. Internal to
        // ASP.NET Core (EndpointRoutingApplicationBuilderExtensions), hence the literal.
        private const string EndpointRouteBuilderKey = "__EndpointRouteBuilder";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The app's own configuration runs first: it is what puts the route builder in
            // Properties and maps the real endpoints.
            next(app);

            if (app.Properties.TryGetValue(EndpointRouteBuilderKey, out var value) &&
                value is IEndpointRouteBuilder routes)
            {
                routes.DataSources.Add(DataSource);
            }
            else
            {
                throw new InvalidOperationException(
                    $"No '{EndpointRouteBuilderKey}' in IApplicationBuilder.Properties — ASP.NET " +
                    "Core changed how WebApplication exposes its endpoint route builder, and the " +
                    "permission-matrix probes would silently 404 instead of failing here.");
            }
        };
    }

    public static EndpointDataSource DataSource { get; } = new DefaultEndpointDataSource(
    [
        Probe(Policies.CanView),
        Probe(Policies.CanOperate),
        Probe(Policies.CanManageStock),
        Probe(Policies.CanViewCosts),
        Probe(Policies.CanAdminister)
    ]);

    private static Endpoint Probe(string policy) =>
        new RouteEndpointBuilder(
            requestDelegate: context => context.Response.WriteAsync("ok"),
            routePattern: RoutePatternFactory.Parse(RouteFor(policy)),
            order: 0)
        {
            DisplayName = $"policy-probe:{policy}",
            Metadata =
            {
                new HttpMethodMetadata([HttpMethods.Get]),
                new AuthorizeAttribute(policy)
            }
        }.Build();
}
