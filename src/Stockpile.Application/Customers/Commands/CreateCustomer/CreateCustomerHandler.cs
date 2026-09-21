using MediatR;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Customers.Queries.SearchCustomers;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Customers.Commands.CreateCustomer;

public sealed class CreateCustomerHandler(IAppDbContext db)
    : IRequestHandler<CreateCustomerCommand, Result<CustomerDto>>
{
    public async Task<Result<CustomerDto>> Handle(
        CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = Customer.Create(
            command.Name, command.Email, command.Phone, command.ShippingAddress);

        if (customer.IsFailure)
            return customer.Error;

        db.Customers.Add(customer.Value);

        // Flushed so Postgres assigns xmin and the response carries a usable RowVersion.
        await db.SaveChangesAsync(cancellationToken);

        return CustomerDto.From(customer.Value);
    }
}
