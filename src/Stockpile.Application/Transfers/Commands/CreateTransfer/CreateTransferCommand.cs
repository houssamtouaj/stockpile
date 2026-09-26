using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Orders;
using Stockpile.Application.Transfers.Queries.GetTransfer;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.Transfers.Commands.CreateTransfer;

public sealed record CreateTransferLine(Guid ProductId, int Quantity);

public sealed record CreateTransferCommand(
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    Guid InTransitWarehouseId,
    IReadOnlyList<CreateTransferLine> Lines) : ICommand<StockTransferDto>;

public sealed class CreateTransferValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferValidator()
    {
        RuleFor(x => x.FromWarehouseId).NotEmpty();
        RuleFor(x => x.ToWarehouseId).NotEmpty();
        RuleFor(x => x.InTransitWarehouseId).NotEmpty();

        // Same-warehouse, duplicate-product and empty transfers are domain rules (422).
        RuleFor(x => x.Lines).NotNull().Must(l => l is null || l.Count <= 500)
            .WithMessage("A transfer may have at most 500 lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        });
    }
}

public sealed class CreateTransferHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numbers,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<CreateTransferCommand, Result<StockTransferDto>>
{
    public async Task<Result<StockTransferDto>> Handle(
        CreateTransferCommand command, CancellationToken cancellationToken)
    {
        var ids = new[] { command.FromWarehouseId, command.ToWarehouseId, command.InTransitWarehouseId };
        var warehouses = await db.Warehouses
            .AsNoTracking()
            .Where(w => ids.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken);

        foreach (var id in ids)
        {
            if (!warehouses.ContainsKey(id))
                return new NotFoundError("Warehouse", id);
        }

        // The in-transit leg must be a warehouse that exists to hold units on the road;
        // routing through a physical one would book the stock as sitting on its shelves.
        if (warehouses[command.InTransitWarehouseId].Kind != WarehouseKind.InTransit)
            return new DomainRuleError(
                "transfer.in_transit_warehouse_invalid",
                "The in-transit leg must use a warehouse with Kind = InTransit.");

        if (warehouses[command.FromWarehouseId].Kind != WarehouseKind.Physical
            || warehouses[command.ToWarehouseId].Kind != WarehouseKind.Physical)
            return new DomainRuleError(
                "transfer.warehouse_not_physical",
                "A transfer moves stock between two physical warehouses.");

        var lines = command.Lines.Select(l => (l.ProductId, l.Quantity)).ToList();

        var valid = StockTransfer.Validate(
            command.FromWarehouseId, command.ToWarehouseId, command.InTransitWarehouseId, lines);
        if (valid.IsFailure)
            return valid.Error;

        if (await OrderProducts.CheckAsync(db, command.Lines.Select(l => l.ProductId), cancellationToken)
            is { } productRefused)
            return productRefused;

        // Numbered after every check that could refuse: nextval() is not rolled back.
        var number = await numbers.NextAsync("TR", cancellationToken);
        var transfer = StockTransfer.Create(
            number, command.FromWarehouseId, command.ToWarehouseId,
            command.InTransitWarehouseId, clock.UtcNow, lines).Value;

        db.StockTransfers.Add(transfer);
        await db.SaveChangesAsync(cancellationToken);

        notifications.Enqueue(new OrderStatusChangedNotification(
            transfer.FromWarehouseId, transfer.Id, nameof(StockTransfer), transfer.Status.ToString()));

        return StockTransferDto.From(transfer);
    }
}
