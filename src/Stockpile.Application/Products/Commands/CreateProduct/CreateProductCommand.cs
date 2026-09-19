using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Products.Queries.GetProduct;

namespace Stockpile.Application.Products.Commands.CreateProduct;

public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string? Description,
    string Category,
    long UnitPriceCents,
    string? Barcode,
    int ReorderPoint,
    int ReorderQuantity) : ICommand<ProductDto>;
