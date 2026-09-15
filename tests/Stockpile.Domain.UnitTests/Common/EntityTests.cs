using Shouldly;
using Stockpile.Domain.Common;
using Stockpile.Domain.Events;

namespace Stockpile.Domain.UnitTests.Common;

public class EntityTests
{
    private sealed class TestEntity : Entity
    {
        public void RaiseSomething() =>
            Raise(new StockDepletedEvent(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void NewEntity_hasNoDomainEvents()
    {
        new TestEntity().DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Raise_appendsToDomainEvents()
    {
        var entity = new TestEntity();

        entity.RaiseSomething();

        entity.DomainEvents.Count.ShouldBe(1);
        entity.DomainEvents[0].ShouldBeOfType<StockDepletedEvent>();
    }

    [Fact]
    public void ClearDomainEvents_emptiesTheList()
    {
        var entity = new TestEntity();
        entity.RaiseSomething();

        entity.ClearDomainEvents();

        entity.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void DomainEvents_isNotDirectlyMutableByCallers()
    {
        var entity = new TestEntity();

        entity.DomainEvents.ShouldBeAssignableTo<IReadOnlyList<DomainEvent>>();
    }
}
