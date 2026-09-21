using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Products.Queries.GetProduct;

namespace Stockpile.Application.Products.Commands.UpdateProduct;

/// <summary>
/// A PATCH: every field is optional and null means "leave it alone". RowVersion is not
/// optional — it is the version the client last saw, and it is what turns a lost update
/// into a 409 the user can resolve.
/// </summary>
public sealed record UpdateProductCommand(
    Guid Id,
    string? Name,
    string? Description,
    string? Category,
    long? UnitPriceCents,
    int? ReorderPoint,
    int? ReorderQuantity,
    bool? IsActive,
    uint RowVersion) : ICommand<ProductDto>;
