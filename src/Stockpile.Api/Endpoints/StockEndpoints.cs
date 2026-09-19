using MediatR;
using Stockpile.Api.Authorization;
using Stockpile.Api.Common;
using Stockpile.Application.Stock.Commands.ReleaseStock;
using Stockpile.Application.Stock.Commands.ReserveStock;

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
    }
}
