using MediatR;
using Stockpile.Api.Authorization;
using Stockpile.Api.Common;
using Stockpile.Application.PurchaseOrders.Commands.CancelPurchaseOrder;
using Stockpile.Application.PurchaseOrders.Commands.CreatePurchaseOrder;
using Stockpile.Application.PurchaseOrders.Commands.ReceivePurchaseOrder;
using Stockpile.Application.PurchaseOrders.Commands.SubmitPurchaseOrder;
using Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;
using Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrders;
using Stockpile.Domain.Enums;

namespace Stockpile.Api.Endpoints;

public static class PurchaseOrderEndpoints
{
    public sealed record ReceiveRequest(Guid LineId, int Quantity, string IdempotencyKey);

    /// <summary>§8: every purchase-order write is WarehouseManager and above.</summary>
    public static void MapPurchaseOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/purchase-orders").WithTags("Purchase orders");

        orders.MapGet("/", async (
                Guid? warehouseId, Guid? supplierId, PurchaseOrderStatus? status,
                int? page, int? size, ISender sender) =>
                (await sender.Send(new GetPurchaseOrdersQuery(
                    warehouseId, supplierId, status, page ?? 1, size ?? 25))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetPurchaseOrders")
            .WithSummary("Purchase orders, newest first, filterable by warehouse, supplier and status.");

        orders.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
                (await sender.Send(new GetPurchaseOrderQuery(id))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetPurchaseOrder")
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPost("/", async (CreatePurchaseOrderCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(o => $"/api/purchase-orders/{o.Id}"))
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CreatePurchaseOrder")
            .Produces<PurchaseOrderDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/submit", async (Guid id, ISender sender) =>
                (await sender.Send(new SubmitPurchaseOrderCommand(id))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("SubmitPurchaseOrder")
            .Produces<PurchaseOrderDto>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/receive", async (Guid id, ReceiveRequest request, ISender sender) =>
                (await sender.Send(new ReceivePurchaseOrderCommand(
                    id, request.LineId, request.Quantity, request.IdempotencyKey))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("ReceivePurchaseOrder")
            .WithSummary("Receive a delivery against one line; repeatable for partial deliveries. Requires an idempotency key.")
            .Produces<PurchaseOrderDto>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapPost("/{id:guid}/cancel", async (Guid id, ISender sender) =>
                (await sender.Send(new CancelPurchaseOrderCommand(id))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CancelPurchaseOrder")
            .WithSummary("Cancel the outstanding remainder. Refused once fully received.")
            .Produces<PurchaseOrderDto>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
