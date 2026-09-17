using NSubstitute;
using Shouldly;
using Stockpile.Application.Common.Behaviors;
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
