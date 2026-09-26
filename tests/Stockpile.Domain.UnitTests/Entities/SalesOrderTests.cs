using Shouldly;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Domain.UnitTests.Entities;

public class SalesOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProductA = Guid.CreateVersion7();
    private static readonly Guid ProductB = Guid.CreateVersion7();

    private static SalesOrder Build() =>
        SalesOrder.Create(
            "SO-2026-00001",
            customerId: Guid.CreateVersion7(),
            warehouseId: Guid.CreateVersion7(),
            createdAt: Now,
            lines:
            [
                (ProductA, 5, 1000),
                (ProductB, 3, 2500)
            ]).Value;

    [Fact]
    public void Create_startsInDraft_withTheGivenLines()
    {
        var order = Build();

        order.Status.ShouldBe(SalesOrderStatus.Draft);
        order.Lines.Count.ShouldBe(2);
        order.Lines.ShouldAllBe(l => l.QuantityPicked == 0);
    }

    [Fact]
    public void Create_withNoLines_fails()
    {
        var result = SalesOrder.Create(
            "SO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), Now, []);

        result.Error!.Code.ShouldBe("so.lines_required");
    }

    [Fact]
    public void Create_withTheSameProductTwice_fails()
    {
        // Two lines for one product would need two reservations against one stock row,
        // and cancellation would have to release both. Merge them at the edge instead.
        var result = SalesOrder.Create(
            "SO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), Now,
            [(ProductA, 1, 100), (ProductA, 2, 100)]);

        result.Error!.Code.ShouldBe("so.duplicate_product_line");
    }

    [Fact]
    public void Confirm_fromDraft_movesToConfirmed()
    {
        var order = Build();

        order.Confirm(Now).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(SalesOrderStatus.Confirmed);
    }

    [Fact]
    public void Confirm_twice_fails()
    {
        var order = Build();
        order.Confirm(Now);

        order.Confirm(Now).Error!.Code.ShouldBe("so.invalid_transition");
    }

    [Fact]
    public void Pick_beforeConfirm_fails()
    {
        var order = Build();

        order.Pick(order.Lines[0].Id, 1).Error!.Code.ShouldBe("so.invalid_transition");
    }

    [Fact]
    public void Pick_movesToPicking_andAccumulates()
    {
        var order = Build();
        order.Confirm(Now);

        order.Pick(order.Lines[0].Id, 2).IsSuccess.ShouldBeTrue();
        order.Pick(order.Lines[0].Id, 3).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(SalesOrderStatus.Picking);
        order.Lines[0].QuantityPicked.ShouldBe(5);
    }

    [Fact]
    public void Pick_beyondTheOrderedQuantity_fails()
    {
        var order = Build();
        order.Confirm(Now);

        order.Pick(order.Lines[0].Id, 6).Error!.Code.ShouldBe("so.pick_exceeds_ordered");
    }

    [Fact]
    public void Pick_anUnknownLine_fails()
    {
        var order = Build();
        order.Confirm(Now);

        order.Pick(Guid.CreateVersion7(), 1).Error!.Code.ShouldBe("not_found");
    }

    [Fact]
    public void Pack_withPartiallyPickedLines_fails()
    {
        var order = Build();
        order.Confirm(Now);
        order.Pick(order.Lines[0].Id, 5);

        order.Pack().Error!.Code.ShouldBe("so.lines_not_fully_picked");
    }

    [Fact]
    public void Pack_whenEveryLineIsFullyPicked_succeeds()
    {
        var order = FullyPicked();

        order.Pack().IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(SalesOrderStatus.Packed);
    }

    [Fact]
    public void Ship_fromPacked_movesToShipped_andStampsShippedAt()
    {
        var order = FullyPicked();
        order.Pack();

        order.Ship(Now).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(SalesOrderStatus.Shipped);
        order.ShippedAt.ShouldBe(Now);
    }

    [Fact]
    public void Ship_beforePack_fails()
    {
        var order = FullyPicked();

        order.Ship(Now).Error!.Code.ShouldBe("so.invalid_transition");
    }

    [Fact]
    public void Cancel_fromDraft_releasesNothing()
    {
        var order = Build();

        var result = order.Cancel();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        order.Status.ShouldBe(SalesOrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_whenConfirmed_returnsEveryLineForRelease()
    {
        // The §14 item, at the domain level. The handler in task 3 turns this list
        // into actual release movements.
        var order = Build();
        order.Confirm(Now);

        var result = order.Cancel();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Select(l => l.QuantityOrdered).ShouldBe([5, 3]);
        order.Status.ShouldBe(SalesOrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_whenPacked_stillReturnsEveryLineForRelease()
    {
        var order = FullyPicked();
        order.Pack();

        order.Cancel().Value.Count.ShouldBe(2);
    }

    [Fact]
    public void Cancel_afterShipping_fails()
    {
        var order = FullyPicked();
        order.Pack();
        order.Ship(Now);

        order.Cancel().Error!.Code.ShouldBe("so.already_shipped");
    }

    [Fact]
    public void Ship_raisesOrderStatusChanged()
    {
        var order = FullyPicked();
        order.Pack();
        order.ClearDomainEvents();

        order.Ship(Now);

        order.DomainEvents.OfType<Events.OrderStatusChangedEvent>()
            .ShouldContain(e => e.ToStatus == "Shipped");
    }

    private static SalesOrder FullyPicked()
    {
        var order = Build();
        order.Confirm(Now);
        foreach (var line in order.Lines)
            order.Pick(line.Id, line.QuantityOrdered);
        return order;
    }

    [Fact]
    public void ValidateLines_refusesWhatCreateRefuses_withoutANumber()
    {
        SalesOrder.ValidateLines([]).Error!.Code.ShouldBe("so.lines_required");
        SalesOrder.ValidateLines([(ProductA, 1, 100), (ProductA, 2, 100)])
            .Error!.Code.ShouldBe("so.duplicate_product_line");
        SalesOrder.ValidateLines([(ProductA, 1, 100)]).IsSuccess.ShouldBeTrue();
    }
}
