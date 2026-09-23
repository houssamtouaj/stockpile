using NSubstitute;
using Shouldly;
using Stockpile.Application.Common.Behaviors;
using Stockpile.Application.Common.Exceptions;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.UnitTests.Behaviors;

public class TransactionBehaviorTests
{
    private sealed record TestCommand : ICommand;
    private sealed record TestQuery : IQuery<Result>;

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly INotificationPublisher _notifications = Substitute.For<INotificationPublisher>();

    public TransactionBehaviorTests()
    {
        // Run the wrapped operation for real so we can observe ordering.
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result>>>()(CancellationToken.None));
    }

    [Fact]
    public async Task Commands_areWrappedInATransaction()
    {
        var behavior = new TransactionBehavior<TestCommand, Result>(_unitOfWork, _notifications);

        await behavior.Handle(new TestCommand(), _ => Task.FromResult(Result.Ok()), CancellationToken.None);

        await _unitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Queries_areNotWrappedInATransaction()
    {
        var behavior = new TransactionBehavior<TestQuery, Result>(_unitOfWork, _notifications);

        await behavior.Handle(new TestQuery(), _ => Task.FromResult(Result.Ok()), CancellationToken.None);

        await _unitOfWork.DidNotReceive().ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>());
        _notifications.DidNotReceive().Enqueue(Arg.Any<IRealtimeNotification>());
    }

    [Fact]
    public async Task Notifications_areFlushedAfterTheTransactionCommits()
    {
        var order = new List<string>();

        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var result = await call.Arg<Func<CancellationToken, Task<Result>>>()(CancellationToken.None);
                order.Add("commit");
                return result;
            });

        _notifications.FlushAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("flush"); return Task.CompletedTask; });

        var behavior = new TransactionBehavior<TestCommand, Result>(_unitOfWork, _notifications);

        await behavior.Handle(new TestCommand(), _ =>
        {
            order.Add("handler");
            return Task.FromResult(Result.Ok());
        }, CancellationToken.None);

        order.ShouldBe(["handler", "commit", "flush"]);
    }

    [Fact]
    public async Task Notifications_areNotFlushed_whenTheHandlerReturnsAFailure()
    {
        var behavior = new TransactionBehavior<TestCommand, Result>(_unitOfWork, _notifications);

        await behavior.Handle(
            new TestCommand(),
            _ => Task.FromResult(Result.Fail(new DomainRuleError("nope", "no"))),
            CancellationToken.None);

        await _notifications.DidNotReceive().FlushAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdempotencyReplay_discardsTheLosersNotifications_andReplaysTheOperation()
    {
        // The losing attempt rolled back, so its queued notification describes a quantity
        // that never committed. Flushing it would broadcast a phantom to every connected
        // client the moment phase 04 puts SignalR behind FlushAsync.
        var attempts = 0;

        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (++attempts == 1)
                    throw new IdempotencyReplayException("ix_stock_movements_idempotency_key");

                return call.Arg<Func<CancellationToken, Task<Result>>>()(CancellationToken.None);
            });

        var behavior = new TransactionBehavior<TestCommand, Result>(_unitOfWork, _notifications);

        var response = await behavior.Handle(
            new TestCommand(), _ => Task.FromResult(Result.Ok()), CancellationToken.None);

        response.IsSuccess.ShouldBeTrue();
        attempts.ShouldBe(2);
        _notifications.Received(1).Clear();
        await _notifications.Received(1).FlushAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdempotencyReplay_isNotRetriedTwice()
    {
        // A second replay signal means the fast path cannot see a movement the unique
        // index says is committed. That is a defect, not a race, and must surface.
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new IdempotencyReplayException("ix_stock_movements_idempotency_key"));

        var behavior = new TransactionBehavior<TestCommand, Result>(_unitOfWork, _notifications);

        await Should.ThrowAsync<IdempotencyReplayException>(() =>
            behavior.Handle(new TestCommand(), _ => Task.FromResult(Result.Ok()), CancellationToken.None));

        await _notifications.DidNotReceive().FlushAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Notifications_areDiscarded_whenTheHandlerThrows()
    {
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new InvalidOperationException("boom"));

        var behavior = new TransactionBehavior<TestCommand, Result>(_unitOfWork, _notifications);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            behavior.Handle(new TestCommand(), _ => Task.FromResult(Result.Ok()), CancellationToken.None));

        await _notifications.DidNotReceive().FlushAsync(Arg.Any<CancellationToken>());
        _notifications.Received(1).Clear();
    }
}
