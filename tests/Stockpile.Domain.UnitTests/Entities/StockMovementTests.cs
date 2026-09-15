using Shouldly;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Domain.UnitTests.Entities;

public class StockMovementTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private static MovementContext Context() => new(
        ProductId: Guid.CreateVersion7(),
        WarehouseId: Guid.CreateVersion7(),
        ReferenceType: "SalesOrder",
        ReferenceId: Guid.CreateVersion7(),
        IdempotencyKey: "key-1",
        Reason: null,
        OccurredAt: Now,
        PerformedByUserId: Guid.CreateVersion7());

    [Fact]
    public void ReservationHold_movesReservedOnly()
    {
        var m = StockMovement.ReservationHold(Context(), quantity: 4, onHandAfter: 10, reservedAfter: 4);

        m.Type.ShouldBe(MovementType.ReservationHold);
        m.OnHandDelta.ShouldBe(0);
        m.ReservedDelta.ShouldBe(4);
        m.OnHandAfter.ShouldBe(10);
        m.ReservedAfter.ShouldBe(4);
    }

    [Fact]
    public void ReservationRelease_movesReservedNegatively()
    {
        var m = StockMovement.ReservationRelease(Context(), quantity: 3, onHandAfter: 10, reservedAfter: 1);

        m.OnHandDelta.ShouldBe(0);
        m.ReservedDelta.ShouldBe(-3);
    }

    [Fact]
    public void Receipt_movesOnHandPositively_andCarriesUnitCost()
    {
        var m = StockMovement.Receipt(Context(), quantity: 20, unitCostCents: 750,
                                      onHandAfter: 20, reservedAfter: 0);

        m.Type.ShouldBe(MovementType.Receipt);
        m.OnHandDelta.ShouldBe(20);
        m.ReservedDelta.ShouldBe(0);
        m.UnitCostCents.ShouldBe(750);
    }

    [Fact]
    public void Issue_movesBothDeltasNegatively()
    {
        // Shipping converts a reservation into an outbound issue: both counters drop.
        var m = StockMovement.Issue(Context(), quantity: 6, onHandAfter: 4, reservedAfter: 0);

        m.Type.ShouldBe(MovementType.Issue);
        m.OnHandDelta.ShouldBe(-6);
        m.ReservedDelta.ShouldBe(-6);
    }

    [Fact]
    public void Adjustment_carriesASignedOnHandDelta_andRequiresAReason()
    {
        var context = Context() with { Reason = "Damaged in handling" };

        var m = StockMovement.Adjustment(context, onHandDelta: -2, onHandAfter: 8, reservedAfter: 0);

        m.OnHandDelta.ShouldBe(-2);
        m.ReservedDelta.ShouldBe(0);
        m.Reason.ShouldBe("Damaged in handling");
    }

    [Fact]
    public void Count_deltaIsTheDifferenceBetweenObservedAndPrevious()
    {
        var m = StockMovement.Count(Context(), previousOnHand: 10, observedOnHand: 7, reservedAfter: 0);

        m.Type.ShouldBe(MovementType.Count);
        m.OnHandDelta.ShouldBe(-3);
        m.OnHandAfter.ShouldBe(7);
    }

    [Fact]
    public void TransferOut_and_TransferIn_areMirrorImages()
    {
        var outbound = StockMovement.TransferOut(Context(), quantity: 5, onHandAfter: 5, reservedAfter: 0);
        var inbound = StockMovement.TransferIn(Context(), quantity: 5, unitCostCents: 400,
                                               onHandAfter: 5, reservedAfter: 0);

        outbound.OnHandDelta.ShouldBe(-5);
        inbound.OnHandDelta.ShouldBe(5);
        (outbound.OnHandDelta + inbound.OnHandDelta).ShouldBe(0);
    }

    [Fact]
    public void EveryFactory_stampsTheActingUserAndIdempotencyKey()
    {
        var context = Context();

        var m = StockMovement.ReservationHold(context, 1, 1, 1);

        m.PerformedByUserId.ShouldBe(context.PerformedByUserId);
        m.IdempotencyKey.ShouldBe("key-1");
        m.OccurredAt.ShouldBe(Now);
        m.ReferenceType.ShouldBe("SalesOrder");
    }
}
