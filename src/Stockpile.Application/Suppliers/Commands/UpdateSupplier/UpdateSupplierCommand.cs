using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Suppliers.Queries.SearchSuppliers;

namespace Stockpile.Application.Suppliers.Commands.UpdateSupplier;

public sealed record UpdateSupplierCommand(
    Guid Id,
    string? Name,
    string? Email,
    string? Phone,
    string? Address,
    bool? IsActive,
    uint RowVersion) : ICommand<SupplierDto>;
