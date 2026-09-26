using Shouldly;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.PurchaseOrders.Commands.ReceivePurchaseOrder;

namespace Stockpile.Application.UnitTests.Stock;

public class DerivedIdempotencyKeyTests
{
    public static TheoryData<string> Legs => [.. DerivedIdempotencyKey.Legs];

    [Theory]
    [MemberData(nameof(Legs))]
    public void TheLongestAcceptedClientKey_stillFitsTheColumn_onEveryLeg(string leg)
    {
        // A derived key longer than stock_movements.idempotency_key fails the INSERT as a
        // 500 on a request the validator accepted.
        var longest = new string('k', DerivedIdempotencyKey.MaxClientKeyLength);

        DerivedIdempotencyKey.For(longest, leg, Guid.CreateVersion7()).Length
            .ShouldBeLessThanOrEqualTo(DerivedIdempotencyKey.MaxKeyLength);
    }

    [Fact]
    public void For_refusesALegTheLengthTestDoesNotCover()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            DerivedIdempotencyKey.For("key", "an-unlisted-and-much-longer-leg", Guid.CreateVersion7()));
    }

    [Fact]
    public void PurchaseOrderReceipt_capsClientKeys_likeEveryOtherOrderTransition()
    {
        var validator = new ReceivePurchaseOrderValidator();
        var key = new string('k', DerivedIdempotencyKey.MaxClientKeyLength + 1);

        validator.Validate(new ReceivePurchaseOrderCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, key))
            .IsValid.ShouldBeFalse();
    }
}
