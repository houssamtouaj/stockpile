using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Customers.Queries.SearchCustomers;

namespace Stockpile.Application.Customers.Commands.CreateCustomer;

public sealed record CreateCustomerCommand(
    string Name,
    string? Email,
    string? Phone,
    string? ShippingAddress) : ICommand<CustomerDto>;
