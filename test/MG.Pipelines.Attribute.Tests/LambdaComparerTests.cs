using System;
using System.Collections.Generic;

using AwesomeAssertions;

using Xunit;

namespace MG.Pipelines.Attribute.Tests;

public class LambdaComparerTests
{
    [Fact]
    public void Equals_Delegates_To_The_Supplied_Lambda()
    {
        var comparer = new LambdaComparer<int>((x, y) => x % 10 == y % 10);
        comparer.Equals(12, 22).Should().BeTrue();
        comparer.Equals(12, 23).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_Delegates_To_Supplied_Hasher()
    {
        var comparer = new LambdaComparer<int>((x, y) => x % 10 == y % 10, obj => obj % 10);
        comparer.GetHashCode(12).Should().Be(2);
        comparer.GetHashCode(22).Should().Be(2);
        comparer.GetHashCode(27).Should().Be(7);
    }

    [Fact]
    public void Default_Hasher_Uses_EqualityComparer_Default()
    {
        var comparer = new LambdaComparer<string>((x, y) => x == y);
        comparer.GetHashCode("abc").Should().Be(EqualityComparer<string>.Default.GetHashCode("abc"));
    }

    [Fact]
    public void Null_Comparer_Throws()
    {
        var act = () => new LambdaComparer<int>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Null_Value_Hashes_To_Zero()
    {
        var comparer = new LambdaComparer<string>((x, y) => x == y);
        comparer.GetHashCode(null!).Should().Be(0);
    }

    [Fact]
    public void Works_As_A_Hashset_Key()
    {
        // Fix validation: with an agreeing hasher the comparer is correct for hash-based containers.
        var comparer = new LambdaComparer<string>((x, y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase),
                                                   obj => obj.ToLowerInvariant().GetHashCode());
        var set = new HashSet<string>(comparer) { "Hello", "WORLD" };
        set.Contains("hello").Should().BeTrue();
        set.Contains("world").Should().BeTrue();
        set.Add("HELLO").Should().BeFalse();
    }
}
