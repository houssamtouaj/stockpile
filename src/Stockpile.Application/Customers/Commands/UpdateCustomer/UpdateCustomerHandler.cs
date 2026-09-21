using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Customers.Queries.SearchCustomers;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Customers.Commands.UpdateCustomer;

public sealed class UpdateCustomerHandler(IAppDbContext db)
    : IRequestHandler<UpdateCustomerCommand, Result<CustomerDto>>
{
    public async Task<Result<CustomerDto>> Handle(
        UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);

        if (customer is null)
            return new NotFoundError("Customer", command.Id);

        db.Entry(customer).Property(nameof(Customer.RowVersion)).OriginalValue = command.RowVersion;

        var updated = customer.UpdateDetails(
            command.Name ?? customer.Name,
            command.Email ?? customer.Email,
            command.Phone ?? customer.Phone,
            command.ShippingAddress ?? customer.ShippingAddress);

        if (updated.IsFailure)
            return updated.Error;

        if (command.IsActive is { } isActive && isActive != customer.IsActive)
        {
            var toggled = isActive ? customer.Reactivate() : customer.Deactivate();
            if (toggled.IsFailure)
                return toggled.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ConcurrencyConflictError("Customer", command.Id);
        }

        return CustomerDto.From(customer);
    }
}
