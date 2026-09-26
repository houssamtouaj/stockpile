using Shouldly;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Domain.UnitTests.Entities;

public class PurchaseOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProductA = Guid.CreateVersion7();
    private static readonly Guid ProductB = Guid.CreateVersion7();

    private static PurchaseOrder Build() =>
        PurchaseOrder.Create(
            "PO-2026-00001",
            supplierId: Guid.CreateVersion7(),
            warehouseId: Guid.CreateVersion7(),
            expectedAt: Now.AddDays(7),
            createdAt: Now,
            lines:
            [
                (ProductA, 10, 500),
                (ProductB, 4, 1200)
            ]).Value;

    private static PurchaseOrder Submitted()
    {
        var order = Build();
        order.Submit(Now);
        return order;
    }

    [Fact]
    public void Create_startsInDraft_withNothingReceived()
    {
        var order = Build();

        order.Status.ShouldBe(PurchaseOrderStatus.Draft);
        order.Lines.Count.ShouldBe(2);
        order.Lines.ShouldAllBe(l => l.QuantityReceived == 0);
        order.Lines[0].UnitCostCents.ShouldBe(500);
    }

    [Fact]
    public void Create_withNoLines_fails()
    {
        var result = PurchaseOrder.Create(
            "PO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), null, Now, []);

        result.Error!.Code.ShouldBe("po.lines_required");
    }

    [Fact]
    public void Create_withTheSameProductTwice_fails()
    {
        var result = PurchaseOrder.Create(
            "PO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), null, Now,
            [(ProductA, 1, 100), (ProductA, 2, 100)]);

        result.Error!.Code.ShouldBe("po.duplicate_product_line");
    }

    [Fact]
    public void Create_withANonPositiveQuantity_fails()
    {
        var result = PurchaseOrder.Create(
            "PO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), null, Now,
            [(ProductA, 0, 100)]);

        result.Error!.Code.ShouldBe("po.line_quantity_invalid");
    }

    [Fact]
    public void Create_withANegativeUnitCost_fails()
    {
        var result = PurchaseOrder.Create(
            "PO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), null, Now,
            [(ProductA, 1, -1)]);

        result.Error!.Code.ShouldBe("po.line_cost_invalid");
    }

    [Fact]
    public void Submit_fromDraft_movesToSubmitted()
    {
        var order = Build();

        order.Submit(Now).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Submitted);
    }

    [Fact]
    public void Submit_twice_fails()
    {
        var order = Submitted();

        order.Submit(Now).Error!.Code.ShouldBe("po.invalid_transition");
    }

    [Fact]
    public void ReceiveLine_onADraft_fails()
    {
        var order = Build();

        order.ReceiveLine(order.Lines[0].Id, 1, Now).Error!.Code.ShouldBe("po.invalid_transition");
    }

    [Fact]
    public void ReceiveLine_aPartialQuantity_movesToPartiallyReceived()
    {
        var order = Submitted();

        var result = order.ReceiveLine(order.Lines[0].Id, 4, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(order.Lines[0]);
        order.Lines[0].QuantityReceived.ShouldBe(4);
        order.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public void ReceiveLine_repeatedPartials_accumulate()
    {
        var order = Submitted();

        order.ReceiveLine(order.Lines[0].Id, 4, Now);
        order.ReceiveLine(order.Lines[0].Id, 3, Now);

        order.Lines[0].QuantityReceived.ShouldBe(7);
        order.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public void ReceiveLine_fullyReceivingOneLineOfTwo_staysPartiallyReceived()
    {
        var order = Submitted();

        order.ReceiveLine(order.Lines[0].Id, 10, Now);

        order.Lines[0].IsFullyReceived.ShouldBeTrue();
        order.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public void ReceiveLine_theLastOutstandingUnit_movesToReceived()
    {
        var order = Submitted();
        order.ReceiveLine(order.Lines[0].Id, 10, Now);
        order.ReceiveLine(order.Lines[1].Id, 3, Now);

        order.ReceiveLine(order.Lines[1].Id, 1, Now).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void ReceiveLine_beyondTheOutstandingQuantity_fails_andRecordsNothing()
    {
        var order = Submitted();
        order.ReceiveLine(order.Lines[0].Id, 8, Now);

        order.ReceiveLine(order.Lines[0].Id, 3, Now).Error!.Code.ShouldBe("po.receipt_exceeds_outstanding");

        order.Lines[0].QuantityReceived.ShouldBe(8);
    }

    [Fact]
    public void ReceiveLine_aNonPositiveQuantity_fails()
    {
        var order = Submitted();

        order.ReceiveLine(order.Lines[0].Id, 0, Now).Error!.Code.ShouldBe("po.receipt_quantity_invalid");
    }

    [Fact]
    public void ReceiveLine_anUnknownLine_fails()
    {
        var order = Submitted();

        order.ReceiveLine(Guid.CreateVersion7(), 1, Now).Error!.Code.ShouldBe("not_found");
    }

    [Fact]
    public void ReceiveLine_onACancelledOrder_fails()
    {
        var order = Submitted();
        order.Cancel(Now);

        order.ReceiveLine(order.Lines[0].Id, 1, Now).Error!.Code.ShouldBe("po.invalid_transition");
    }

    [Fact]
    public void Cancel_fromDraft_succeeds()
    {
        var order = Build();

        order.Cancel(Now).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_fromSubmitted_succeeds()
    {
        var order = Submitted();

        order.Cancel(Now).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_fromPartiallyReceived_succeeds()
    {
        var order = Submitted();
        order.ReceiveLine(order.Lines[0].Id, 1, Now);

        order.Cancel(Now).IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_afterFullReceipt_fails()
    {
        var order = Submitted();
        foreach (var line in order.Lines)
            order.ReceiveLine(line.Id, line.QuantityOrdered, Now);

        order.Cancel(Now).Error!.Code.ShouldBe("po.already_received");
        order.Status.ShouldBe(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void Cancel_twice_fails()
    {
        var order = Build();
        order.Cancel(Now);

        order.Cancel(Now).Error!.Code.ShouldBe("po.already_cancelled");
    }

    [Fact]
    public void Submit_raisesOrderStatusChanged()
    {
        var order = Build();

        order.Submit(Now);

        order.DomainEvents.OfType<Events.OrderStatusChangedEvent>()
            .ShouldContain(e => e.FromStatus == "Draft" && e.ToStatus == "Submitted");
    }

    [Fact]
    public void ValidateLines_refusesWhatCreateRefuses_withoutANumber()
    {
        PurchaseOrder.ValidateLines([]).Error!.Code.ShouldBe("po.lines_required");
        PurchaseOrder.ValidateLines([(ProductA, 1, -1)]).Error!.Code.ShouldBe("po.line_cost_invalid");
        PurchaseOrder.ValidateLines([(ProductA, 1, 100)]).IsSuccess.ShouldBeTrue();
    }
}
