using System;
using System.Collections.Generic;

using AwesomeAssertions;

using MG.Pipelines.Attribute.Tests.TestSupport;

using Xunit;

namespace MG.Pipelines.Attribute.Tests;

[Collection(RegistrationCollection.Name)]
public class PipelineFactoryTests
{
    public PipelineFactoryTests()
    {
        Registration.Clear();
        Registration.RegisterPipelines(new[] { typeof(PipelineA), typeof(PipelineB) });
    }

    [Fact]
    public void Create_Resolves_Registered_Pipeline()
    {
        var factory = new PipelineFactory();
        var pipeline = factory.Create<ArgsA>("pipeline-a");

        pipeline.Should().NotBeNull();
        pipeline!.Tasks.Should().HaveCount(2);

        var args = new ArgsA();
        pipeline.Execute(args).Should().Be(PipelineResult.Ok);
        args.Log.Should().Equal("A1", "A2");
    }

    [Fact]
    public void Create_Returns_Null_For_Unknown_Name()
    {
        var factory = new PipelineFactory();
        factory.Create<ArgsA>("does-not-exist").Should().BeNull();
    }

    [Fact]
    public void Create_Null_Name_Throws()
    {
        var factory = new PipelineFactory();
        var act = () => factory.Create<ArgsA>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_Consults_Name_Resolver_In_Order()
    {
        var resolver = new QueuedResolver(new[] { "missing:specific", "pipeline-a" });
        var factory = new PipelineFactory(resolver);

        factory.Create<ArgsA>("unused").Should().NotBeNull();
        resolver.Calls.Should().Be(1);
    }

    [Fact]
    public void AllPipelinesFor_Filters_By_Argument_Type()
    {
        var factory = new PipelineFactory();

        factory.AllPipelinesFor<ArgsA>().Should().BeEquivalentTo(new[] { "pipeline-a" });
        factory.AllPipelinesFor<ArgsB>().Should().BeEquivalentTo(new[] { "pipeline-b" });
        factory.AllPipelinesFor<string>().Should().BeEmpty();
    }

    [Fact]
    public void Instance_Uses_Default_Resolver()
    {
        PipelineFactory.Instance.Should().NotBeNull();
        PipelineFactory.Instance.Create<ArgsA>("pipeline-a").Should().NotBeNull();
    }

    [Fact]
    public void PipelineFactory_With_Null_Resolver_Throws()
    {
        var act = () => new PipelineFactory(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class QueuedResolver : IPipelineNameResolver
    {
        private readonly string[] candidates;
        public int Calls { get; private set; }

        public QueuedResolver(string[] candidates) { this.candidates = candidates; }

        public IList<string> ResolveNames(string localName)
        {
            Calls++;
            return candidates;
        }
    }
}
