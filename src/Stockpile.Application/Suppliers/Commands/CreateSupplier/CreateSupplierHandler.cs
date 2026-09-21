using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Suppliers.Queries.SearchSuppliers;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Suppliers.Commands.CreateSupplier;

public sealed class CreateSupplierHandler(IAppDbContext db)
    : IRequestHandler<CreateSupplierCommand, Result<SupplierDto>>
{
    public async Task<Result<SupplierDto>> Handle(
        CreateSupplierCommand command, CancellationToken cancellationToken)
    {
        var code = command.Code.Trim().ToUpperInvariant();

        if (await db.Suppliers.AnyAsync(s => s.Code == code, cancellationToken))
            return new DomainRuleError("supplier.code_taken", $"Supplier code {code} is already in use.");

        var supplier = Supplier.Create(
            command.Code, command.Name, command.Email, command.Phone, command.Address);

        if (supplier.IsFailure)
            return supplier.Error;

        db.Suppliers.Add(supplier.Value);

        // Flushed so Postgres assigns xmin and the response carries a usable RowVersion.
        await db.SaveChangesAsync(cancellationToken);

        return SupplierDto.From(supplier.Value);
    }
}
