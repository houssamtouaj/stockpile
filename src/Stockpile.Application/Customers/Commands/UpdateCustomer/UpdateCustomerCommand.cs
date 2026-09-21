using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Customers.Queries.SearchCustomers;

namespace Stockpile.Application.Customers.Commands.UpdateCustomer;

public sealed record UpdateCustomerCommand(
    Guid Id,
    string? Name,
    string? Email,
    string? Phone,
    string? ShippingAddress,
    bool? IsActive,
    uint RowVersion) : ICommand<CustomerDto>;
