using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Products.Queries.GetProduct;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Application.Products.Commands.CreateProduct;

public sealed class CreateProductHandler(
    IAppDbContext db, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<CreateProductCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(
        CreateProductCommand command, CancellationToken cancellationToken)
    {
        var sku = Sku.Create(command.Sku);
        if (sku.IsFailure)
            return sku.Error;

        var taken = await db.Products
            .AnyAsync(p => p.Sku == sku.Value, cancellationToken);

        if (taken)
            return new DomainRuleError("product.sku_taken", $"SKU '{sku.Value.Value}' is already in use.");

        var product = Product.Create(
            sku.Value,
            command.Name,
            command.Description,
            command.Category,
            command.UnitPriceCents,
            command.Barcode,
            command.ReorderPoint,
            command.ReorderQuantity,
            currentUser.UserId ?? Guid.Empty,
            clock.UtcNow);

        if (product.IsFailure)
            return product.Error;

        db.Products.Add(product.Value);

        // SaveChangesAsync here, exceptionally. TransactionBehavior still owns the
        // COMMIT — this only flushes the INSERT so Postgres assigns xmin and EF reads
        // it back into Product.RowVersion. Projecting the DTO before the flush would
        // hand the client RowVersion = 0, and their next PATCH would 409 against a
        // product nobody else has touched (ProductConcurrencyTests, task 11).
        // Any create whose response carries a database-generated value needs this.
        await db.SaveChangesAsync(cancellationToken);

        return ProductDto.From(product.Value);
    }
}
