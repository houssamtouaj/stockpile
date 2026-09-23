using MediatR;
using Stockpile.Api.Authorization;
using Stockpile.Api.Common;
using Stockpile.Application.Common.Patching;
using Stockpile.Application.Customers.Commands.CreateCustomer;
using Stockpile.Application.Customers.Commands.UpdateCustomer;
using Stockpile.Application.Customers.Queries.SearchCustomers;
using Stockpile.Application.Products.Commands.CreateProduct;
using Stockpile.Application.Products.Commands.UpdateProduct;
using Stockpile.Application.Products.Queries.GetProduct;
using Stockpile.Application.Products.Queries.GetProductByBarcode;
using Stockpile.Application.Products.Queries.SearchProducts;
using Stockpile.Application.Stock.Queries.GetStockLevels;
using Stockpile.Application.Suppliers.Commands.CreateSupplier;
using Stockpile.Application.Suppliers.Commands.UpdateSupplier;
using Stockpile.Application.Suppliers.Queries.SearchSuppliers;
using Stockpile.Application.Warehouses.Commands.CreateWarehouse;
using Stockpile.Application.Warehouses.Queries.GetWarehouses;

namespace Stockpile.Api.Endpoints;

public static class CatalogueEndpoints
{
    /// <summary>
    /// A PATCH body carries only the fields being changed plus the RowVersion the client
    /// last saw; the id comes from the route, so the wire shape and the command differ by
    /// exactly that one field.
    /// </summary>
    public sealed record PatchProductRequest(
        string? Name, string? Description, string? Category, long? UnitPriceCents,
        int? ReorderPoint, int? ReorderQuantity, bool? IsActive, uint RowVersion);

    // Patch<string> on the nullable fields: an omitted field and an explicit null are
    // different instructions, and string? cannot tell them apart.
    public sealed record PatchSupplierRequest(
        string? Name, Patch<string> Email, Patch<string> Phone, Patch<string> Address,
        bool? IsActive, uint RowVersion);

    public sealed record PatchCustomerRequest(
        string? Name, Patch<string> Email, Patch<string> Phone, Patch<string> ShippingAddress,
        bool? IsActive, uint RowVersion);

    public static void MapCatalogueEndpoints(this IEndpointRouteBuilder app)
    {
        MapProducts(app);
        MapWarehouses(app);
        MapSuppliers(app);
        MapCustomers(app);
    }

    private static void MapProducts(IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/products").WithTags("Products");

        products.MapPost("/", async (CreateProductCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(p => $"/api/products/{p.Id}"))
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CreateProduct")
            .WithSummary("Create a product. The SKU must be unique.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        products.MapGet("/", async (
                string? search, string? category, int? page, int? size, ISender sender) =>
                (await sender.Send(new SearchProductsQuery(
                    search, category, page ?? 1, size ?? 25))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("SearchProducts")
            .WithSummary("Search products by SKU or name, optionally filtered by category.");

        // Ahead of /{id:guid} in source order, though the route constraint already keeps
        // the two from competing: "by-barcode" is not a Guid.
        products.MapGet("/by-barcode/{barcode}", async (string barcode, ISender sender) =>
                (await sender.Send(new GetProductByBarcodeQuery(barcode))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetProductByBarcode")
            .WithSummary("The scanner's fast path: a single indexed lookup by barcode.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
                (await sender.Send(new GetProductQuery(id))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetProduct")
            .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapPatch("/{id:guid}", async (
                Guid id, PatchProductRequest request, ISender sender) =>
                (await sender.Send(new UpdateProductCommand(
                    id, request.Name, request.Description, request.Category,
                    request.UnitPriceCents, request.ReorderPoint, request.ReorderQuantity,
                    request.IsActive, request.RowVersion))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("UpdateProduct")
            .WithSummary("Partial update. Send the rowVersion you last read; a stale one returns 409.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapWarehouses(IEndpointRouteBuilder app)
    {
        var warehouses = app.MapGroup("/api/warehouses").WithTags("Warehouses");

        warehouses.MapPost("/", async (CreateWarehouseCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(w => $"/api/warehouses/{w.Id}"))
            .RequireAuthorization(Policies.CanAdminister)
            .WithName("CreateWarehouse")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        warehouses.MapGet("/", async (ISender sender) =>
                (await sender.Send(new GetWarehousesQuery())).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetWarehouses");

        warehouses.MapGet("/{id:guid}/stock", async (
                Guid id, bool? lowStockOnly, ISender sender) =>
                (await sender.Send(new GetStockLevelsQuery(
                    ProductId: null, WarehouseId: id, LowStockOnly: lowStockOnly ?? false))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("GetWarehouseStock")
            .WithSummary("Stock in one warehouse, optionally only items at or below the reorder point.");
    }

    private static void MapSuppliers(IEndpointRouteBuilder app)
    {
        var suppliers = app.MapGroup("/api/suppliers").WithTags("Suppliers");

        suppliers.MapGet("/", async (string? search, int? page, int? size, ISender sender) =>
                (await sender.Send(new SearchSuppliersQuery(search, page ?? 1, size ?? 25))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("SearchSuppliers");

        suppliers.MapPost("/", async (CreateSupplierCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(s => $"/api/suppliers/{s.Id}"))
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CreateSupplier")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        suppliers.MapPatch("/{id:guid}", async (
                Guid id, PatchSupplierRequest request, ISender sender) =>
                (await sender.Send(new UpdateSupplierCommand(
                    id, request.Name, request.Email, request.Phone, request.Address,
                    request.IsActive, request.RowVersion))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("UpdateSupplier")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapCustomers(IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/api/customers").WithTags("Customers");

        customers.MapGet("/", async (string? search, int? page, int? size, ISender sender) =>
                (await sender.Send(new SearchCustomersQuery(search, page ?? 1, size ?? 25))).ToOk())
            .RequireAuthorization(Policies.CanView)
            .WithName("SearchCustomers");

        customers.MapPost("/", async (CreateCustomerCommand command, ISender sender) =>
                (await sender.Send(command)).ToCreated(c => $"/api/customers/{c.Id}"))
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("CreateCustomer")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        customers.MapPatch("/{id:guid}", async (
                Guid id, PatchCustomerRequest request, ISender sender) =>
                (await sender.Send(new UpdateCustomerCommand(
                    id, request.Name, request.Email, request.Phone, request.ShippingAddress,
                    request.IsActive, request.RowVersion))).ToOk())
            .RequireAuthorization(Policies.CanManageStock)
            .WithName("UpdateCustomer")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
