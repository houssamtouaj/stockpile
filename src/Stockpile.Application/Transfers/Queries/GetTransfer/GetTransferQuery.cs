using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Transfers.Queries.GetTransfer;

public sealed record GetTransferQuery(Guid Id) : IQuery<Result<StockTransferDto>>;

public sealed class GetTransferHandler(IAppDbContext db)
    : IRequestHandler<GetTransferQuery, Result<StockTransferDto>>
{
    public async Task<Result<StockTransferDto>> Handle(
        GetTransferQuery query, CancellationToken cancellationToken)
    {
        var transfer = await db.StockTransfers
            .AsNoTracking()
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == query.Id, cancellationToken);

        return transfer is null
            ? new NotFoundError("StockTransfer", query.Id)
            : StockTransferDto.From(transfer);
    }
}
