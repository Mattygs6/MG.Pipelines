using System;
using System.Collections.Generic;
using System.Linq;

using AwesomeAssertions;

using MG.Pipelines.DependencyInjection.Tests.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace MG.Pipelines.DependencyInjection.Tests;

[Collection(DiCollection.Name)]
public class CreateArgsTests
{
    [Fact]
    public void CreateArgs_Returns_Default_Instance_When_No_Binder_Registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var args = factory.CreateArgs<Args>("arithmetic");
        args.Should().NotBeNull();
        args.Log.Should().BeEmpty();
    }

    [Fact]
    public void CreateArgs_Resolves_Constructor_Dependencies_Via_DI()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddSingleton<DependencyForArgs>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        // ArgsWithDi requires a DependencyForArgs in its constructor — ActivatorUtilities resolves it.
        var args = factory.CreateArgs<ArgsWithDi>("arithmetic");
        args.Should().NotBeNull();
        args.Dependency.Should().BeSameAs(provider.GetRequiredService<DependencyForArgs>());
    }

    [Fact]
    public void CreateArgs_Invokes_Every_Registered_Binder_In_Order()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddSingleton<IPipelineArgsBinder>(new TaggingBinder("first"));
        services.AddSingleton<IPipelineArgsBinder>(new TaggingBinder("second"));
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var args = factory.CreateArgs<Args>("arithmetic");
        args.Log.Should().Equal("first", "second");
    }

    [Fact]
    public void CreateArgs_Passes_Pipeline_Name_To_Binder()
    {
        var observed = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddSingleton<IPipelineArgsBinder>(new InspectingBinder((name, _) => observed.Add(name)));
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        factory.CreateArgs<Args>("arithmetic:specific");
        observed.Should().Equal("arithmetic:specific");
    }

    [Fact]
    public void CreateArgs_Null_Name_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var act = () => factory.CreateArgs<Args>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateArgs_Returns_Fresh_Instances_On_Each_Call()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Counter>();
        services.AddPipelines(typeof(ArithmeticPipeline).Assembly);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var a = factory.CreateArgs<Args>("arithmetic");
        var b = factory.CreateArgs<Args>("arithmetic");
        a.Should().NotBeSameAs(b);
    }

    public sealed class DependencyForArgs { }

    public sealed class ArgsWithDi
    {
        public DependencyForArgs Dependency { get; }
        public ArgsWithDi(DependencyForArgs dependency) { Dependency = dependency; }
    }

    private sealed class TaggingBinder : IPipelineArgsBinder
    {
        private readonly string tag;
        public TaggingBinder(string tag) { this.tag = tag; }
        public void Bind<T>(string pipelineName, T instance)
        {
            if (instance is Args a)
            {
                a.Log.Add(tag);
            }
        }
    }

    private sealed class InspectingBinder : IPipelineArgsBinder
    {
        private readonly Action<string, object?> callback;
        public InspectingBinder(Action<string, object?> callback) { this.callback = callback; }
        public void Bind<T>(string pipelineName, T instance) => callback(pipelineName, instance);
    }
}
