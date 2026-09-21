using Stockpile.Domain.Entities;

namespace Stockpile.Application.Suppliers.Queries.SearchSuppliers;

public sealed record SupplierDto(
    Guid Id,
    string Code,
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive,
    uint RowVersion)
{
    public static SupplierDto From(Supplier s) =>
        new(s.Id, s.Code, s.Name, s.Email, s.Phone, s.Address, s.IsActive, s.RowVersion);
}
