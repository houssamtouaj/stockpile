using FluentValidation;

namespace Stockpile.Application.Stock.Commands.AdjustStock;

public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();

        // A zero adjustment would write a movement that moves nothing, which the
        // movement_deltas_not_both_zero constraint exists to forbid.
        RuleFor(x => x.OnHandDelta).NotEqual(0);

        // §8: an adjustment without a reason is an unexplained stock change, which is
        // exactly what the audit trail is meant to make impossible.
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}
