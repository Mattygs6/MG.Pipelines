using System;

using AwesomeAssertions;

using MG.Pipelines.DependencyInjection.Tests.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace MG.Pipelines.DependencyInjection.Tests;

[Collection(DiCollection.Name)]
public class ServiceProviderPipelineFactoryTests
{
    [Fact]
    public void Null_ServiceProvider_Throws()
    {
        var act = () => new ServiceProviderPipelineFactory(null!, new PipelineNameResolver());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Null_Resolver_Throws()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var act = () => new ServiceProviderPipelineFactory(provider, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_Null_Name_Throws()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var factory = new ServiceProviderPipelineFactory(provider, new PipelineNameResolver());

        var act = () => factory.Create<Args>(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
