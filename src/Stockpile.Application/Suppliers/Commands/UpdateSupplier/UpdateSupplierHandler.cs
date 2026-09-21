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

        var updated = supplier.UpdateDetails(
            command.Name ?? supplier.Name,
            command.Email ?? supplier.Email,
            command.Phone ?? supplier.Phone,
            command.Address ?? supplier.Address);

        if (updated.IsFailure)
            return updated.Error;

        if (command.IsActive is { } isActive && isActive != supplier.IsActive)
        {
            var toggled = isActive ? supplier.Reactivate() : supplier.Deactivate();
            if (toggled.IsFailure)
                return toggled.Error;
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
