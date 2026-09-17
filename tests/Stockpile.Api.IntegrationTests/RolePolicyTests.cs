using System.Net;
using Shouldly;
using Stockpile.Api.Authorization;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.IntegrationTests;

/// <summary>
/// The §8 permission matrix, end to end: seed a user with a role, log in, present the access
/// token to a route guarded by a policy. A failure here is a claims problem, not an endpoint
/// problem — which is precisely why it is worth pinning before phase 02 puts
/// <c>RequireAuthorization(Policies.CanManageStock)</c> on a real route and the same failure
/// arrives looking like a broken endpoint.
/// <para>
/// Proven to fire: removing the role claim from <c>JwtTokenService.CreateAccessToken</c> turns
/// every OK row red (4 failed, 5 passed) while the Forbidden rows keep passing — they would
/// pass against a system that admits nobody, so they are not the ones carrying the proof.
/// </para>
/// </summary>
[Collection(nameof(ApiCollection))]
public class RolePolicyTests(StockpileApiFactory factory)
{
    [Theory]
    // CanOperate — Operator and up.
    [InlineData(Policies.CanOperate, Role.Viewer, HttpStatusCode.Forbidden)]
    [InlineData(Policies.CanOperate, Role.Operator, HttpStatusCode.OK)]
    [InlineData(Policies.CanOperate, Role.WarehouseManager, HttpStatusCode.OK)]
    // CanManageStock — WarehouseManager and up. The pair phase 02 depends on.
    [InlineData(Policies.CanManageStock, Role.Operator, HttpStatusCode.Forbidden)]
    [InlineData(Policies.CanManageStock, Role.WarehouseManager, HttpStatusCode.OK)]
    // CanAdminister — Admin only.
    [InlineData(Policies.CanAdminister, Role.WarehouseManager, HttpStatusCode.Forbidden)]
    [InlineData(Policies.CanAdminister, Role.Admin, HttpStatusCode.OK)]
    // CanView — any authenticated user, regardless of role.
    [InlineData(Policies.CanView, Role.Viewer, HttpStatusCode.OK)]
    public async Task Policy_admitsExactlyTheRolesTheMatrixNames(
        string policy, Role role, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await factory.CreateClientAs(role);

        var response = await client.GetAsync(PolicyProbeEndpoints.RouteFor(policy), ct);

        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task ARolePolicy_withoutAToken_returns401_not403()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await factory.CreateClient()
            .GetAsync(PolicyProbeEndpoints.RouteFor(Policies.CanManageStock), ct);

        // 401, not 403: the caller has not been told who they are yet. Getting this wrong
        // is what makes a client show "access denied" where it should show a login prompt.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
