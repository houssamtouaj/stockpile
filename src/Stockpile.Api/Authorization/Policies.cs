using Microsoft.AspNetCore.Authorization;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.Authorization;

/// <summary>
/// The §8 permission matrix, expressed once. Roles are cumulative — each tier includes
/// everything the tier below can do, matching the actor generalisation in the use-case
/// diagram (Viewer &lt;|-- Operator &lt;|-- WarehouseManager &lt;|-- Admin).
/// </summary>
public static class Policies
{
    /// <summary>Read stock, products and orders. Every authenticated user.</summary>
    public const string CanView = nameof(CanView);

    /// <summary>Reserve, release, pick, pack, dispatch and receive transfers.</summary>
    public const string CanOperate = nameof(CanOperate);

    /// <summary>Adjust stock, cycle count, run reconciliation, manage the catalogue,
    /// create and receive purchase orders, cancel orders, view the audit log.</summary>
    public const string CanManageStock = nameof(CanManageStock);

    /// <summary>
    /// See cost prices and valuation. Same tier as CanManageStock but named separately,
    /// because it is the one an enterprise client asks about first.
    /// <para>
    /// This governs ENDPOINTS that are entirely about cost, such as the valuation report.
    /// It does NOT cover the harder half of §8: cost fields embedded in a response both
    /// roles may fetch. That is a query-handler concern, built in phase 05.
    /// </para>
    /// </summary>
    public const string CanViewCosts = nameof(CanViewCosts);

    /// <summary>Manage users, roles and warehouses.</summary>
    public const string CanAdminister = nameof(CanAdminister);

    public static AuthorizationBuilder AddStockpilePolicies(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy(CanView, p => p.RequireAuthenticatedUser())
            .AddPolicy(CanOperate, p => p.RequireRole(
                nameof(Role.Operator), nameof(Role.WarehouseManager), nameof(Role.Admin)))
            .AddPolicy(CanManageStock, p => p.RequireRole(
                nameof(Role.WarehouseManager), nameof(Role.Admin)))
            .AddPolicy(CanViewCosts, p => p.RequireRole(
                nameof(Role.WarehouseManager), nameof(Role.Admin)))
            .AddPolicy(CanAdminister, p => p.RequireRole(nameof(Role.Admin)));
}
