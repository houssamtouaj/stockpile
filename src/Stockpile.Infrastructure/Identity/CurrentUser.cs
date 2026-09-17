using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Enums;

namespace Stockpile.Infrastructure.Identity;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string CorrelationHeader = "X-Correlation-Id";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Principal?.FindFirstValue("sub"), out var id)
            ? id
            : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email)
                            ?? Principal?.FindFirstValue("email");

    public Role? Role =>
        Enum.TryParse<Role>(Principal?.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;

    public bool CanViewCosts => Role >= Domain.Enums.Role.WarehouseManager;

    public string CorrelationId =>
        accessor.HttpContext?.Request.Headers[CorrelationHeader].FirstOrDefault()
        ?? accessor.HttpContext?.TraceIdentifier
        ?? "unknown";
}
