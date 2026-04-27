using System;

using AwesomeAssertions;

using Xunit;

namespace MG.Pipelines.Tests;

public class PipelineNameResolverTests
{
    [Fact]
    public void Returns_Supplied_Name_As_Only_Candidate()
    {
        var resolver = new PipelineNameResolver();
        resolver.ResolveNames("my-pipeline").Should().Equal("my-pipeline");
    }

    [Fact]
    public void Null_Name_Throws()
    {
        var resolver = new PipelineNameResolver();
        var act = () => resolver.ResolveNames(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
