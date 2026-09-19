using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Warehouses.Queries.GetWarehouses;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.Warehouses.Commands.CreateWarehouse;

public sealed class CreateWarehouseHandler(IAppDbContext db)
    : IRequestHandler<CreateWarehouseCommand, Result<WarehouseDto>>
{
    public async Task<Result<WarehouseDto>> Handle(
        CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        var code = command.Code.Trim().ToUpperInvariant();

        var taken = await db.Warehouses.AnyAsync(w => w.Code == code, cancellationToken);
        if (taken)
            return new DomainRuleError("warehouse.code_taken", $"Warehouse code '{code}' is already in use.");

        var warehouse = command.Kind == WarehouseKind.InTransit
            ? Warehouse.CreateInTransit(command.Code, command.Name)
            : Warehouse.CreatePhysical(command.Code, command.Name, command.Address);

        if (warehouse.IsFailure)
            return warehouse.Error;

        db.Warehouses.Add(warehouse.Value);
        await db.SaveChangesAsync(cancellationToken);

        return WarehouseDto.From(warehouse.Value);
    }
}
