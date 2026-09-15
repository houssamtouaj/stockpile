using Stockpile.Domain.Common;

namespace Stockpile.Domain.Entities;

public sealed class Customer : Entity
{
    private Customer() { }   // EF

    public string Name { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? ShippingAddress { get; private set; }
    public bool IsActive { get; private set; }
    public uint RowVersion { get; private set; }

    public static Result<Customer> Create(
        string name, string? email, string? phone, string? shippingAddress)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new DomainRuleError("customer.name_required", "Customer name is required.");

        return new Customer
        {
            Name = name.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            ShippingAddress = string.IsNullOrWhiteSpace(shippingAddress) ? null : shippingAddress.Trim(),
            IsActive = true
        };
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return new DomainRuleError("customer.already_inactive", "Customer is already inactive.");

        IsActive = false;
        return Result.Ok();
    }
}
