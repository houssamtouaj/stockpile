using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Application.Stock.Queries.GetMovements;

public sealed record GetMovementsQuery(
    Guid? ProductId = null,
    Guid? WarehouseId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    MovementType? Type = null,
    string? Cursor = null,
    int Size = 50) : IQuery<Result<CursorPage<MovementDto>>>;
