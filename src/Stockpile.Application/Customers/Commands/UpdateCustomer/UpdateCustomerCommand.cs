using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Patching;
using Stockpile.Application.Customers.Queries.SearchCustomers;

namespace Stockpile.Application.Customers.Commands.UpdateCustomer;

/// <summary>
/// A PATCH. Name and IsActive cannot be cleared, so null there means "leave it alone"; the
/// three nullable fields use Patch so an explicit null can mean "clear this" — the only
/// spelling a JSON client has for it.
/// </summary>
public sealed record UpdateCustomerCommand(
    Guid Id,
    string? Name,
    Patch<string> Email,
    Patch<string> Phone,
    Patch<string> ShippingAddress,
    bool? IsActive,
    uint RowVersion) : ICommand<CustomerDto>;
