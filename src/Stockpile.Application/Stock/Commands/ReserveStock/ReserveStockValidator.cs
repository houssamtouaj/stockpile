using FluentValidation;

namespace Stockpile.Application.Stock.Commands.ReserveStock;

public sealed class ReserveStockValidator : AbstractValidator<ReserveStockCommand>
{
    public ReserveStockValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);

        // §6: not optional. Clients retry on timeout, and without a key a retry
        // double-reserves.
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}
