using MediatR;
using Stockpile.Api.Authorization;
using Stockpile.Api.Common;
using Stockpile.Application.Products.Commands.CreateProduct;
using Stockpile.Application.Products.Queries.GetProduct;
using Stockpile.Application.Warehouses.Commands.CreateWarehouse;
using Stockpile.Application.Warehouses.Queries.GetWarehouses;

namespace Stockpile.Api.Endpoints;

public static class CatalogueEndpoints
{
    public static void MapCatalogueEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/products").WithTags("Products");

        products.MapPost("/", async (CreateProductCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(p => $"/api/products/{p.Id}"))
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CreateProduct");

        products.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
                (await sender.Send(new GetProductQuery(id))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetProduct");

        var warehouses = app.MapGroup("/api/warehouses").WithTags("Warehouses");

        warehouses.MapPost("/", async (CreateWarehouseCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(w => $"/api/warehouses/{w.Id}"))
            .RequireAuthorization(Policies.CanAdminister);

        warehouses.MapGet("/", async (ISender sender) =>
                (await sender.Send(new GetWarehousesQuery())).ToOk())
            .RequireAuthorization(Policies.CanView);
    }
}
