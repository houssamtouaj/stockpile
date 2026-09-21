using Stockpile.Domain.Common;

namespace Stockpile.Domain.Entities;

public sealed class Supplier : Entity
{
    private Supplier() { }   // EF

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }
    public uint RowVersion { get; private set; }

    public static Result<Supplier> Create(
        string code, string name, string? email, string? phone, string? address)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new DomainRuleError("supplier.code_required", "Supplier code is required.");

        if (string.IsNullOrWhiteSpace(name))
            return new DomainRuleError("supplier.name_required", "Supplier name is required.");

        return new Supplier
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            IsActive = true
        };
    }

    public Result UpdateDetails(string name, string? email, string? phone, string? address)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new DomainRuleError("supplier.name_required", "Supplier name is required.");

        Name = name.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        return Result.Ok();
    }

    public Result Reactivate()
    {
        if (IsActive)
            return new DomainRuleError("supplier.already_active", "Supplier is already active.");

        IsActive = true;
        return Result.Ok();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return new DomainRuleError("supplier.already_inactive", "Supplier is already inactive.");

        IsActive = false;
        return Result.Ok();
    }
}
