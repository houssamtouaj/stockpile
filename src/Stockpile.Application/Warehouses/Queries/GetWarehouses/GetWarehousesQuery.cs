using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Warehouses.Queries.GetWarehouses;

public sealed record GetWarehousesQuery : IQuery<Result<IReadOnlyList<WarehouseDto>>>;
