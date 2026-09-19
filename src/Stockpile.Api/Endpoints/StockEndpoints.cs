using MediatR;
using Stockpile.Api.Authorization;
using Stockpile.Api.Common;
using Stockpile.Application.Stock.Commands.AdjustStock;
using Stockpile.Application.Stock.Commands.CountStock;
using Stockpile.Application.Stock.Commands.ReconcileStock;
using Stockpile.Application.Stock.Commands.ReleaseStock;
using Stockpile.Application.Stock.Commands.ReserveStock;
using Stockpile.Application.Stock.Queries.GetMovements;
using Stockpile.Application.Stock.Queries.GetStockLevels;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.Endpoints;

public static class StockEndpoints
{
    public static void MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        var stock = app.MapGroup("/api/stock").WithTags("Stock");

        stock.MapPost("/reserve", async (ReserveStockCommand command, ISender sender) =>
                (await sender.Send(command)).ToOk())
            .RequireAuthorization(Policies.CanOperate)
            .WithName("ReserveStock")
            .WithSummary("Hold stock for an order. Requires an idempotency key.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapPost("/release", async (ReleaseStockCommand command, ISender sender) =>
                (await sender.Send(command)).ToOk())
            .RequireAuthorization(Policies.CanOperate)
            .WithName("ReleaseStock")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapPost("/adjust", async (AdjustStockCommand command, ISender sender) =>
                (await sender.Send(command)).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("AdjustStock")
            .WithSummary("Signed stock adjustment. Requires a reason and an idempotency key.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapPost("/count", async (CountStockCommand command, ISender sender) =>
                (await sender.Send(command)).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CountStock")
            .WithSummary("Cycle count. Takes the observed absolute quantity, not a delta.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapPost("/reconcile", async (bool? repair, ISender sender) =>
                (await sender.Send(new ReconcileStockCommand(repair ?? false))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("ReconcileStock")
            .WithSummary("Recompute every stock snapshot from the ledger and report discrepancies.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        stock.MapGet("/", async (
                Guid? productId, Guid? warehouseId, ISender sender) =>
                (await sender.Send(new GetStockLevelsQuery(productId, warehouseId))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetStockLevels")
            .WithSummary("Current stock levels, with availability as on-hand minus reserved.");

        stock.MapGet("/movements", async (
                Guid? productId, Guid? warehouseId,
                DateTimeOffset? from, DateTimeOffset? to,
                MovementType? type, string? cursor, int? size,
                ISender sender) =>
                (await sender.Send(new GetMovementsQuery(
                    productId, warehouseId, from, to, type, cursor, size ?? 50))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetMovements")
            .WithSummary("Cursor-paginated movement ledger, newest first.");
    }
}
