using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Suppliers.Queries.SearchSuppliers;

namespace Stockpile.Application.Suppliers.Commands.CreateSupplier;

public sealed record CreateSupplierCommand(
    string Code,
    string Name,
    string? Email,
    string? Phone,
    string? Address) : ICommand<SupplierDto>;
