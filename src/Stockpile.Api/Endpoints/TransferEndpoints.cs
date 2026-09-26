using MediatR;
using Stockpile.Api.Authorization;
using Stockpile.Api.Common;
using Stockpile.Application.Transfers.Commands.CancelTransfer;
using Stockpile.Application.Transfers.Commands.CreateTransfer;
using Stockpile.Application.Transfers.Commands.DispatchTransfer;
using Stockpile.Application.Transfers.Commands.ReceiveTransfer;
using Stockpile.Application.Transfers.Queries.GetTransfer;
using Stockpile.Application.Transfers.Queries.GetTransfers;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.Endpoints;

public static class TransferEndpoints
{
    /// <summary>§8: managers authorise a transfer (create, cancel); warehouse staff move
    /// the stock (dispatch, receive).</summary>
    public static void MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var transfers = app.MapGroup("/api/transfers").WithTags("Transfers");

        transfers.MapGet("/", async (
                Guid? warehouseId, TransferStatus? status, int? page, int? size, ISender sender) =>
                (await sender.Send(new GetTransfersQuery(warehouseId, status, page ?? 1, size ?? 25))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetTransfers")
            .WithSummary("Transfers, newest first; warehouseId matches either end.");

        transfers.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
                (await sender.Send(new GetTransferQuery(id))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetTransfer")
            .ProducesProblem(StatusCodes.Status404NotFound);

        transfers.MapPost("/", async (CreateTransferCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(t => $"/api/transfers/{t.Id}"))
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CreateTransfer")
            .WithSummary("Create a draft transfer routed through an in-transit warehouse.")
            .Produces<StockTransferDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapPost("/{id:guid}/dispatch", async (Guid id, IdempotentRequest request, ISender sender) =>
                (await sender.Send(new DispatchTransferCommand(id, request.IdempotencyKey))).ToOk())
            .RequireAuthorization(Policies.CanOperate)
            .WithName("DispatchTransfer")
            .WithSummary("Move every line from the source into the in-transit warehouse. Requires an idempotency key.")
            .Produces<StockTransferDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapPost("/{id:guid}/receive", async (Guid id, IdempotentRequest request, ISender sender) =>
                (await sender.Send(new ReceiveTransferCommand(id, request.IdempotencyKey))).ToOk())
            .RequireAuthorization(Policies.CanOperate)
            .WithName("ReceiveTransfer")
            .WithSummary("Move every line from the in-transit warehouse into the destination. Requires an idempotency key.")
            .Produces<StockTransferDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        transfers.MapPost("/{id:guid}/cancel", async (Guid id, ISender sender) =>
                (await sender.Send(new CancelTransferCommand(id))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CancelTransfer")
            .WithSummary("Cancel a draft. Refused after dispatch: receive it, then transfer it back.")
            .Produces<StockTransferDto>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
