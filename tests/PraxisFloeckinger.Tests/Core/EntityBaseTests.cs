using AwesomeAssertions;
using PraxisFloeckinger.Core.Common;

namespace PraxisFloeckinger.Tests.Core;

public class EntityBaseTests
{
    // Minimale konkrete Subklasse für Tests — EntityBase ist abstrakt
    private sealed class TestEntity : SoftDeletableEntityBase { }

    [Fact]
    public void Id_IsNotEmpty_WhenCreated()
    {
        var entity = new TestEntity();
        entity.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void TwoInstances_HaveDistinctIds()
    {
        var a = new TestEntity();
        var b = new TestEntity();
        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void CreatedAt_IsSetToUtcNow_WhenCreated()
    {
        var before = DateTimeOffset.UtcNow;
        var entity = new TestEntity();
        var after = DateTimeOffset.UtcNow;

        entity.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void ModifiedAt_IsNull_WhenCreated()
    {
        var entity = new TestEntity();
        entity.ModifiedAt.Should().BeNull();
    }

    [Fact]
    public void CreatedBy_IsNull_WhenCreated()
    {
        var entity = new TestEntity();
        entity.CreatedBy.Should().BeNull();
    }

    [Fact]
    public void IsDeleted_IsFalse_WhenCreated()
    {
        var entity = new TestEntity();
        entity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void DeletedAt_IsNull_WhenCreated()
    {
        var entity = new TestEntity();
        entity.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void DeletedBy_IsNull_WhenCreated()
    {
        var entity = new TestEntity();
        entity.DeletedBy.Should().BeNull();
    }
}
