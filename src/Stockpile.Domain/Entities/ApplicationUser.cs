using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Domain.Entities;

/// <summary>
/// The domain's view of a user. ASP.NET Core Identity's own user type lives in
/// Infrastructure and is mapped to this; Domain must not reference Identity.
/// </summary>
public sealed class ApplicationUser : Entity
{
    private ApplicationUser() { }   // EF

    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public Role Role { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>§8: cost prices and valuation are visible to WarehouseManager and above only.</summary>
    public bool CanViewCosts() => Role >= Role.WarehouseManager;

    public static Result<ApplicationUser> Create(
        Guid id, string email, string displayName, Role role)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new DomainRuleError("user.email_required", "Email is required.");

        if (string.IsNullOrWhiteSpace(displayName))
            return new DomainRuleError("user.display_name_required", "Display name is required.");

        var user = new ApplicationUser
        {
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            Role = role,
            IsActive = true
        };
        user.SetId(id);
        return user;
    }

    /// <summary>Identity owns the user id, so it is supplied rather than generated.</summary>
    private void SetId(Guid id) => Id = id;

    public Result ChangeRole(Role role)
    {
        if (Role == role)
            return new DomainRuleError("user.role_unchanged", $"User already has the role {role}.");

        Role = role;
        return Result.Ok();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return new DomainRuleError("user.already_inactive", "User is already inactive.");

        IsActive = false;
        return Result.Ok();
    }
}
