using Stockpile.Domain.Entities;

namespace Stockpile.Application.Customers.Queries.SearchCustomers;

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? ShippingAddress,
    bool IsActive,
    uint RowVersion)
{
    public static CustomerDto From(Customer c) =>
        new(c.Id, c.Name, c.Email, c.Phone, c.ShippingAddress, c.IsActive, c.RowVersion);
}
