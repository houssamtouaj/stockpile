using Microsoft.AspNetCore.Identity;

namespace Stockpile.Infrastructure.Identity;

/// <summary>
/// Identity's storage type. The domain's ApplicationUser is a separate class with the same
/// Id — Domain must not reference ASP.NET Core Identity, and the architecture test enforces it.
/// </summary>
public sealed class StockpileIdentityUser : IdentityUser<Guid>;
