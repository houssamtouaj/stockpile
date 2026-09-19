using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Warehouses.Queries.GetWarehouses;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.Warehouses.Commands.CreateWarehouse;

public sealed record CreateWarehouseCommand(
    string Code,
    string Name,
    string? Address,
    WarehouseKind Kind = WarehouseKind.Physical) : ICommand<WarehouseDto>;
