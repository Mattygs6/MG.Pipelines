using System;

using AwesomeAssertions;

using MG.Pipelines.Attribute;
using MG.Pipelines.DependencyInjection.Tests.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace MG.Pipelines.DependencyInjection.Tests;

[Collection(DiCollection.Name)]
public class AddPipelinesTests
{
    public AddPipelinesTests()
    {
        Registration.Clear();
    }

    [Fact]
    public void AddPipelines_Registers_Factory_And_Resolver_With_Defaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IPipelineNameResolver>().Should().BeOfType<PipelineNameResolver>();
        provider.GetRequiredService<IPipelineFactory>().Should().BeOfType<ServiceProviderPipelineFactory>();
    }

    [Fact]
    public void AddPipelines_Does_Not_Overwrite_Existing_Resolver()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddSingleton<IPipelineNameResolver, PrefixResolver>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPipelineNameResolver>().Should().BeOfType<PrefixResolver>();
    }

    [Fact]
    public void Factory_Resolves_Pipeline_And_Executes_Tasks_Through_DI()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var counter = provider.GetRequiredService<Counter>();
        counter.Value = 3;

        var factory = provider.GetRequiredService<IPipelineFactory>();
        var pipeline = factory.Create<Args>("arithmetic");
        pipeline.Should().NotBeNull();

        var args = new Args();
        pipeline!.Execute(args).Should().Be(PipelineResult.Ok);

        counter.Value.Should().Be(8); // (3+1)*2
        args.Log.Should().Equal("inc=4", "dbl=8");
    }

    [Fact]
    public void Factory_Returns_Null_For_Unknown_Pipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        factory.Create<Args>("no-such-pipeline").Should().BeNull();
    }

    [Fact]
    public void Factory_Consults_Custom_Resolver_In_Order()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddSingleton<IPipelineNameResolver, PrefixResolver>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var counter = provider.GetRequiredService<Counter>();
        counter.Value = 5;

        var factory = provider.GetRequiredService<IPipelineFactory>();

        // PrefixResolver returns ["arithmetic:specific", "arithmetic"] — the ":specific" variant wins.
        // SpecificArithmeticPipeline is Double then Increment: (5*2)+1 = 11.
        var args = new Args();
        factory.Create<Args>("arithmetic")!.Execute(args).Should().Be(PipelineResult.Ok);

        counter.Value.Should().Be(11);
        args.Log.Should().Equal("dbl=10", "inc=11");
    }

    [Fact]
    public void AllPipelinesFor_Returns_Registered_Names()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        factory.AllPipelinesFor<Args>().Should().BeEquivalentTo(new[] { "arithmetic", "arithmetic:specific" });
    }

    [Fact]
    public void Each_Resolution_Creates_Fresh_Task_Instances()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var p1 = factory.Create<Args>("arithmetic")!;
        var p2 = factory.Create<Args>("arithmetic")!;

        p1.Should().NotBeSameAs(p2);
        p1.Tasks[0].Should().NotBeSameAs(p2.Tasks[0]);
    }

    [Fact]
    public void Null_ServiceCollection_Throws()
    {
        var act = () => ServiceCollectionExtensions.AddPipelines(null!, typeof(ArithmeticPipeline).Assembly);
        act.Should().Throw<ArgumentNullException>();
    }
}
