using MediatR;
using Stockpile.Api.Authorization;
using Stockpile.Api.Common;
using Stockpile.Application.SalesOrders.Commands.CancelSalesOrder;
using Stockpile.Application.SalesOrders.Commands.ConfirmSalesOrder;
using Stockpile.Application.SalesOrders.Commands.CreateSalesOrder;
using Stockpile.Application.SalesOrders.Commands.PackSalesOrder;
using Stockpile.Application.SalesOrders.Commands.PickSalesOrder;
using Stockpile.Application.SalesOrders.Commands.ShipSalesOrder;
using Stockpile.Application.SalesOrders.Queries.GetOrderBoard;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrders;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.Endpoints;

public static class SalesOrderEndpoints
{
    /// <summary>
    /// IdempotencyKey is optional so the §8 body stays valid, but a client that retries a
    /// pick must send one: picks accumulate, and a keyless retry counts the units twice.
    /// </summary>
    public sealed record PickRequest(Guid LineId, int Quantity, string? IdempotencyKey = null);

    /// <summary>
    /// §8's matrix: confirm, ship and cancel are the manager's calls because they commit or
    /// release stock; pick and pack are the Operator's job on the floor.
    /// </summary>
    public static void MapSalesOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/sales-orders").WithTags("Sales orders");

        orders.MapGet("/", async (
                Guid? warehouseId, SalesOrderStatus? status, int? page, int? size, ISender sender) =>
                (await sender.Send(new GetSalesOrdersQuery(warehouseId, status, page ?? 1, size ?? 25))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetSalesOrders")
            .WithSummary("Sales orders, newest first, optionally filtered by warehouse and status.");

        orders.MapGet("/board", async (Guid? warehouseId, ISender sender) =>
                (await sender.Send(new GetOrderBoardQuery(warehouseId))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetOrderBoard")
            .WithSummary("Every open sales order grouped by status, one column per open status.")
            .Produces<OrderBoardDto>();

        orders.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
                (await sender.Send(new GetSalesOrderQuery(id))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetSalesOrder")
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPost("/", async (CreateSalesOrderCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(o => $"/api/sales-orders/{o.Id}"))
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CreateSalesOrder")
            .WithSummary("Create a draft sales order. Nothing is reserved until it is confirmed.")
            .Produces<SalesOrderDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/confirm", async (Guid id, IdempotentRequest request, ISender sender) =>
                (await sender.Send(new ConfirmSalesOrderCommand(id, request.IdempotencyKey))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("ConfirmSalesOrder")
            .WithSummary("Reserve every line, all or nothing. Requires an idempotency key.")
            .Produces<SalesOrderDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/pick", async (Guid id, PickRequest request, ISender sender) =>
                (await sender.Send(new PickSalesOrderCommand(
                    id, request.LineId, request.Quantity, request.IdempotencyKey))).ToOk())
            .RequireAuthorization(Policies.CanOperate)
            .WithName("PickSalesOrder")
            .WithSummary("Record units picked for one line. Moves no stock. Send an idempotency key to make retries safe.")
            .Produces<SalesOrderDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/pack", async (Guid id, ISender sender) =>
                (await sender.Send(new PackSalesOrderCommand(id))).ToOk())
            .RequireAuthorization(Policies.CanOperate)
            .WithName("PackSalesOrder")
            .WithSummary("Mark a fully picked order packed. Moves no stock.")
            .Produces<SalesOrderDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/ship", async (Guid id, IdempotentRequest request, ISender sender) =>
                (await sender.Send(new ShipSalesOrderCommand(id, request.IdempotencyKey))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("ShipSalesOrder")
            .WithSummary("Convert every reservation into an issue. Requires an idempotency key.")
            .Produces<SalesOrderDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/cancel", async (Guid id, IdempotentRequest request, ISender sender) =>
                (await sender.Send(new CancelSalesOrderCommand(id, request.IdempotencyKey))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CancelSalesOrder")
            .WithSummary("Cancel, releasing every outstanding reservation. Refused once shipped.")
            .Produces<SalesOrderDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
