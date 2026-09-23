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

        // Or(), not ??: an explicit null in the payload has to reach UpdateDetails, which
        // already knows how to clear a field. Coalescing here is what made it unreachable.
        var updated = customer.UpdateDetails(
            command.Name ?? customer.Name,
            command.Email.Or(customer.Email),
            command.Phone.Or(customer.Phone),
            command.ShippingAddress.Or(customer.ShippingAddress));

        if (updated.IsFailure)
            return updated.Error;

        if (command.IsActive is { } isActive && isActive != customer.IsActive)
        {
            var toggled = isActive ? customer.Reactivate() : customer.Deactivate();
            if (toggled.IsFailure)
                return toggled.Error;
        }

        // EF only emits `WHERE id = @id AND xmin = @original` when at least one property is
        // actually modified. A PATCH whose every value already matches what is stored
        // modifies nothing, SaveChanges returns 0 without raising
        // DbUpdateConcurrencyException, and the stale token the client sent is never
        // checked — so a client whose read is out of date is told their write landed while
        // holding someone else's data. Asking the loaded token the same question the WHERE
        // clause would have asked closes that path.
        // The token is excluded: pinning its OriginalValue above is itself seen as a
        // change, so counting it would make this test never fire.
        var changed = db.Entry(customer).Properties
            .Any(p => p.IsModified && !p.Metadata.IsConcurrencyToken);

        if (!changed && customer.RowVersion != command.RowVersion)
        {
            return new ConcurrencyConflictError("Customer", command.Id);
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
