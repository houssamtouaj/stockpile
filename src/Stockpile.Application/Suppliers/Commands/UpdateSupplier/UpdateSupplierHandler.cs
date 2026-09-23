using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Suppliers.Queries.SearchSuppliers;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Suppliers.Commands.UpdateSupplier;

public sealed class UpdateSupplierHandler(IAppDbContext db)
    : IRequestHandler<UpdateSupplierCommand, Result<SupplierDto>>
{
    public async Task<Result<SupplierDto>> Handle(
        UpdateSupplierCommand command, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);

        if (supplier is null)
            return new NotFoundError("Supplier", command.Id);

        db.Entry(supplier).Property(nameof(Supplier.RowVersion)).OriginalValue = command.RowVersion;

        // Or(), not ??: an explicit null in the payload has to reach UpdateDetails, which
        // already knows how to clear a field. Coalescing here is what made it unreachable.
        var updated = supplier.UpdateDetails(
            command.Name ?? supplier.Name,
            command.Email.Or(supplier.Email),
            command.Phone.Or(supplier.Phone),
            command.Address.Or(supplier.Address));

        if (updated.IsFailure)
            return updated.Error;

        if (command.IsActive is { } isActive && isActive != supplier.IsActive)
        {
            var toggled = isActive ? supplier.Reactivate() : supplier.Deactivate();
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
        var changed = db.Entry(supplier).Properties
            .Any(p => p.IsModified && !p.Metadata.IsConcurrencyToken);

        if (!changed && supplier.RowVersion != command.RowVersion)
        {
            return new ConcurrencyConflictError("Supplier", command.Id);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ConcurrencyConflictError("Supplier", command.Id);
        }

        return SupplierDto.From(supplier);
    }
}
