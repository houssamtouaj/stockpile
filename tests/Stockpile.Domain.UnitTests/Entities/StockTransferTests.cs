using Shouldly;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Domain.UnitTests.Entities;

public class StockTransferTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid From = Guid.CreateVersion7();
    private static readonly Guid To = Guid.CreateVersion7();
    private static readonly Guid InTransit = Guid.CreateVersion7();
    private static readonly Guid ProductA = Guid.CreateVersion7();

    private static StockTransfer Build() =>
        StockTransfer.Create("TR-2026-00001", From, To, InTransit, Now, [(ProductA, 40)]).Value;

    [Fact]
    public void Create_startsInDraft_withTheThreeWarehouses()
    {
        var transfer = Build();

        transfer.Status.ShouldBe(TransferStatus.Draft);
        transfer.FromWarehouseId.ShouldBe(From);
        transfer.ToWarehouseId.ShouldBe(To);
        transfer.InTransitWarehouseId.ShouldBe(InTransit);
        transfer.Lines.Single().Quantity.ShouldBe(40);
    }

    [Fact]
    public void Create_withNoLines_fails()
    {
        StockTransfer.Create("TR-1", From, To, InTransit, Now, [])
            .Error!.Code.ShouldBe("transfer.lines_required");
    }

    [Fact]
    public void Create_betweenTheSameWarehouse_fails()
    {
        StockTransfer.Create("TR-1", From, From, InTransit, Now, [(ProductA, 1)])
            .Error!.Code.ShouldBe("transfer.same_warehouse");
    }

    [Fact]
    public void Create_withNoInTransitWarehouse_fails()
    {
        StockTransfer.Create("TR-1", From, To, Guid.Empty, Now, [(ProductA, 1)])
            .Error!.Code.ShouldBe("transfer.in_transit_warehouse_invalid");
    }

    [Fact]
    public void Create_routingThroughAnEndpointAsInTransit_fails()
    {
        // The in-transit leg must be a third warehouse; routing through the destination
        // would book the units as arrived the moment they leave.
        StockTransfer.Create("TR-1", From, To, To, Now, [(ProductA, 1)])
            .Error!.Code.ShouldBe("transfer.in_transit_warehouse_invalid");
    }

    [Fact]
    public void Create_withTheSameProductTwice_fails()
    {
        StockTransfer.Create("TR-1", From, To, InTransit, Now, [(ProductA, 1), (ProductA, 2)])
            .Error!.Code.ShouldBe("transfer.duplicate_product_line");
    }

    [Fact]
    public void Create_withANonPositiveQuantity_fails()
    {
        StockTransfer.Create("TR-1", From, To, InTransit, Now, [(ProductA, 0)])
            .Error!.Code.ShouldBe("transfer.line_quantity_invalid");
    }

    [Fact]
    public void Dispatch_fromDraft_movesToInTransit()
    {
        var transfer = Build();

        transfer.Dispatch(Now).IsSuccess.ShouldBeTrue();

        transfer.Status.ShouldBe(TransferStatus.InTransit);
        transfer.DispatchedAt.ShouldBe(Now);
    }

    [Fact]
    public void Dispatch_twice_fails()
    {
        var transfer = Build();
        transfer.Dispatch(Now);

        transfer.Dispatch(Now).Error!.Code.ShouldBe("transfer.invalid_transition");
    }

    [Fact]
    public void Receive_beforeDispatch_fails()
    {
        Build().Receive(Now).Error!.Code.ShouldBe("transfer.invalid_transition");
    }

    [Fact]
    public void Receive_fromInTransit_movesToReceived()
    {
        var transfer = Build();
        transfer.Dispatch(Now);

        transfer.Receive(Now).IsSuccess.ShouldBeTrue();

        transfer.Status.ShouldBe(TransferStatus.Received);
        transfer.ReceivedAt.ShouldBe(Now);
    }

    [Fact]
    public void Receive_twice_fails()
    {
        var transfer = Build();
        transfer.Dispatch(Now);
        transfer.Receive(Now);

        transfer.Receive(Now).Error!.Code.ShouldBe("transfer.invalid_transition");
    }

    [Fact]
    public void Cancel_fromDraft_succeeds()
    {
        var transfer = Build();

        transfer.Cancel(Now).IsSuccess.ShouldBeTrue();

        transfer.Status.ShouldBe(TransferStatus.Cancelled);
    }

    [Fact]
    public void Cancel_afterDispatch_fails()
    {
        var transfer = Build();
        transfer.Dispatch(Now);

        transfer.Cancel(Now).Error!.Code.ShouldBe("transfer.cannot_cancel_after_dispatch");
        transfer.Status.ShouldBe(TransferStatus.InTransit);
    }

    [Fact]
    public void Cancel_afterReceipt_fails()
    {
        var transfer = Build();
        transfer.Dispatch(Now);
        transfer.Receive(Now);

        transfer.Cancel(Now).Error!.Code.ShouldBe("transfer.cannot_cancel_after_dispatch");
    }

    [Fact]
    public void Cancel_twice_fails()
    {
        var transfer = Build();
        transfer.Cancel(Now);

        transfer.Cancel(Now).Error!.Code.ShouldBe("transfer.already_cancelled");
    }

    [Fact]
    public void Dispatch_onACancelledTransfer_fails()
    {
        var transfer = Build();
        transfer.Cancel(Now);

        transfer.Dispatch(Now).Error!.Code.ShouldBe("transfer.invalid_transition");
    }

    [Fact]
    public void Dispatch_raisesOrderStatusChanged()
    {
        var transfer = Build();

        transfer.Dispatch(Now);

        transfer.DomainEvents.OfType<Events.OrderStatusChangedEvent>()
            .ShouldContain(e => e.ToStatus == "InTransit");
    }

    [Fact]
    public void RecordDispatchCost_onceDispatched_pricesTheLine()
    {
        // The cost travels with the line so the receipt can move exactly this value out of
        // the in-transit row, whatever else is in flight there.
        var transfer = Build();
        transfer.Dispatch(Now);
        var line = transfer.Lines.Single();

        transfer.RecordDispatchCost(line.Id, 250).IsSuccess.ShouldBeTrue();

        line.UnitCostCents.ShouldBe(250);
    }

    [Fact]
    public void RecordDispatchCost_beforeDispatch_fails()
    {
        var transfer = Build();

        transfer.RecordDispatchCost(transfer.Lines.Single().Id, 250)
            .Error!.Code.ShouldBe("transfer.invalid_transition");
        transfer.Lines.Single().UnitCostCents.ShouldBeNull();
    }

    [Fact]
    public void RecordDispatchCost_negative_fails()
    {
        var transfer = Build();
        transfer.Dispatch(Now);

        transfer.RecordDispatchCost(transfer.Lines.Single().Id, -1)
            .Error!.Code.ShouldBe("transfer.line_cost_invalid");
    }

    [Fact]
    public void RecordDispatchCost_forAnUnknownLine_fails()
    {
        var transfer = Build();
        transfer.Dispatch(Now);

        transfer.RecordDispatchCost(Guid.CreateVersion7(), 250).Error!.Code.ShouldBe("not_found");
    }
}
