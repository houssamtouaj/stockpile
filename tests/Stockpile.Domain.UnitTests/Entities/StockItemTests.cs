using Shouldly;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Events;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Domain.UnitTests.Entities;

public class StockItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private static StockItem WithStock(int onHand, int reserved = 0, long avgCost = 1000)
    {
        var item = StockItem.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "A-01-01");
        if (onHand > 0)
            item.Receive(Quantity.Create(onHand).Value, Money.FromCents(avgCost), Now).IsSuccess.ShouldBeTrue();
        if (reserved > 0)
            item.Reserve(Quantity.Create(reserved).Value, Now).IsSuccess.ShouldBeTrue();
        item.ClearDomainEvents();
        return item;
    }

    [Fact]
    public void Create_startsEmpty()
    {
        var item = StockItem.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), null);

        item.QuantityOnHand.ShouldBe(0);
        item.QuantityReserved.ShouldBe(0);
        item.QuantityAvailable.ShouldBe(0);
        item.AverageUnitCostCents.ShouldBe(0);
    }

    [Fact]
    public void QuantityAvailable_isOnHandMinusReserved()
    {
        WithStock(onHand: 10, reserved: 4).QuantityAvailable.ShouldBe(6);
    }

    [Fact]
    public void Reserve_withinAvailable_increasesReservedOnly()
    {
        var item = WithStock(onHand: 10);

        var result = item.Reserve(Quantity.Create(4).Value, Now);

        result.IsSuccess.ShouldBeTrue();
        item.QuantityOnHand.ShouldBe(10);
        item.QuantityReserved.ShouldBe(4);
        item.QuantityAvailable.ShouldBe(6);
    }

    [Fact]
    public void Reserve_beyondAvailable_failsWithInsufficientStock()
    {
        var item = WithStock(onHand: 10, reserved: 8);

        var result = item.Reserve(Quantity.Create(3).Value, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("stock.insufficient");
        item.QuantityReserved.ShouldBe(8);
    }

    [Fact]
    public void Release_reducesReserved()
    {
        var item = WithStock(onHand: 10, reserved: 6);

        item.Release(Quantity.Create(4).Value, Now).IsSuccess.ShouldBeTrue();

        item.QuantityReserved.ShouldBe(2);
        item.QuantityOnHand.ShouldBe(10);
    }

    [Fact]
    public void Release_moreThanReserved_fails()
    {
        var item = WithStock(onHand: 10, reserved: 2);

        var result = item.Release(Quantity.Create(3).Value, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("stock.release_exceeds_reserved");
    }

    [Fact]
    public void Issue_reducesBothOnHandAndReserved()
    {
        var item = WithStock(onHand: 10, reserved: 6);

        item.Issue(Quantity.Create(6).Value, Now).IsSuccess.ShouldBeTrue();

        item.QuantityOnHand.ShouldBe(4);
        item.QuantityReserved.ShouldBe(0);
        item.QuantityAvailable.ShouldBe(4);
    }

    [Fact]
    public void Issue_withoutEnoughReserved_fails()
    {
        var item = WithStock(onHand: 10, reserved: 2);

        var result = item.Issue(Quantity.Create(5).Value, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("stock.issue_exceeds_reserved");
    }

    [Fact]
    public void Issue_emptyingTheShelf_raisesStockDepleted()
    {
        var item = WithStock(onHand: 5, reserved: 5);

        item.Issue(Quantity.Create(5).Value, Now);

        item.DomainEvents.ShouldContain(e => e is StockDepletedEvent);
    }

    [Fact]
    public void Receive_intoEmptyStock_setsAverageCostToTheReceiptCost()
    {
        var item = StockItem.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), null);

        item.Receive(Quantity.Create(10).Value, Money.FromCents(500), Now);

        item.QuantityOnHand.ShouldBe(10);
        item.AverageUnitCostCents.ShouldBe(500);
    }

    [Fact]
    public void Receive_recomputesWeightedAverageCost()
    {
        // 10 units at 500 = 5000; receive 10 at 700 = 7000; 12000 / 20 = 600
        var item = WithStock(onHand: 10, avgCost: 500);

        item.Receive(Quantity.Create(10).Value, Money.FromCents(700), Now);

        item.QuantityOnHand.ShouldBe(20);
        item.AverageUnitCostCents.ShouldBe(600);
    }

    [Fact]
    public void Receive_roundsTheWeightedAverageToTheNearestCent()
    {
        // 3 units at 100 = 300; receive 1 at 101 = 101; 401 / 4 = 100.25 -> 100
        var item = WithStock(onHand: 3, avgCost: 100);

        item.Receive(Quantity.Create(1).Value, Money.FromCents(101), Now);

        item.AverageUnitCostCents.ShouldBe(100);
    }

    [Fact]
    public void Receive_raisesStockChanged()
    {
        var item = WithStock(onHand: 1);

        item.Receive(Quantity.Create(1).Value, Money.FromCents(100), Now);

        item.DomainEvents.ShouldContain(e => e is StockChangedEvent);
    }

    [Fact]
    public void AdjustTo_aLowerCount_reducesOnHand_andStampsLastCountedAt()
    {
        var item = WithStock(onHand: 10, reserved: 2);

        item.AdjustTo(7, Now).IsSuccess.ShouldBeTrue();

        item.QuantityOnHand.ShouldBe(7);
        item.LastCountedAt.ShouldBe(Now);
    }

    [Fact]
    public void AdjustTo_belowReserved_fails()
    {
        var item = WithStock(onHand: 10, reserved: 8);

        var result = item.AdjustTo(5, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("stock.count_below_reserved");
        item.QuantityOnHand.ShouldBe(10);
    }

    [Fact]
    public void AdjustTo_aNegativeCount_fails()
    {
        var result = WithStock(onHand: 10).AdjustTo(-1, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("stock.count_negative");
    }
}
