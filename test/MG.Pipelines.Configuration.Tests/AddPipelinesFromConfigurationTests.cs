using System;
using System.Threading.Tasks;

using AwesomeAssertions;

using MG.Pipelines.Attribute;
using MG.Pipelines.Configuration.Tests.TestSupport;
using MG.Pipelines.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace MG.Pipelines.Configuration.Tests;

[Collection(ConfigurationCollection.Name)]
public class AddPipelinesFromConfigurationTests
{
    private static readonly string CheckoutArgsTypeName = typeof(CheckoutArgs).AssemblyQualifiedName!;
    private static readonly string ValidateTaskTypeName = typeof(ValidateTask).AssemblyQualifiedName!;
    private static readonly string ChargeTaskTypeName = typeof(ChargeTask).AssemblyQualifiedName!;
    private static readonly string SendReceiptTaskTypeName = typeof(SendReceiptTask).AssemblyQualifiedName!;
    private static readonly string FraudCheckTaskTypeName = typeof(FraudCheckTask).AssemblyQualifiedName!;
    private static readonly string WrongArgsTaskTypeName = typeof(WrongArgsTask).AssemblyQualifiedName!;
    private static readonly string ExplicitConfigPipelineTypeName = typeof(ExplicitConfigPipeline).AssemblyQualifiedName!;

    public AddPipelinesFromConfigurationTests()
    {
        Registration.Clear();
    }

    [Fact]
    public async Task Registers_ConfigurablePipeline_When_PipelineType_Is_Omitted()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "checkout", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}", "{{ChargeTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddTransient<CheckoutArgs>();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IPipelineFactory>().Create<CheckoutArgs>("checkout");

        pipeline.Should().BeOfType<ConfigurablePipeline<CheckoutArgs>>();
        var counter = provider.GetRequiredService<CounterState>();
        var args = new CheckoutArgs(counter);
        (await pipeline!.ExecuteAsync(args)).Should().Be(PipelineResult.Ok);
        counter.Calls.Should().Equal("validate", "charge");
    }

    [Fact]
    public void Registers_Explicit_PipelineType_When_Specified()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "checkout", "pipelineType": "{{ExplicitConfigPipelineTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}", "{{ChargeTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IPipelineFactory>().Create<CheckoutArgs>("checkout");

        pipeline.Should().BeOfType<ExplicitConfigPipeline>();
    }

    [Fact]
    public async Task Configuration_Overrides_Existing_Attribute_Pipeline_Task_Order()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "attribute-checkout", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{FraudCheckTaskTypeName}}", "{{ValidateTaskTypeName}}",
                             "{{ChargeTaskTypeName}}", "{{SendReceiptTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        services.AddPipelines(typeof(AttributeCheckoutPipeline).Assembly);
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var counter = provider.GetRequiredService<CounterState>();
        var args = new CheckoutArgs(counter);

        var result = await provider.GetRequiredService<IPipelineFactory>()
            .Create<CheckoutArgs>("attribute-checkout")!
            .ExecuteAsync(args);

        result.Should().Be(PipelineResult.Ok);
        counter.Calls.Should().Equal("fraud", "validate", "charge", "receipt");
    }

    [Fact]
    public void Empty_Configuration_Section_Is_Safe_And_Still_Registers_Factory()
    {
        var section = ConfigBuilders.BuildSection("""{ "Pipelines": [] }""");

        var services = new ServiceCollection();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPipelineFactory>().Should().BeOfType<ServiceProviderPipelineFactory>();
    }

    [Fact]
    public void Throws_When_Tasks_List_Is_Empty()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "broken", "argumentType": "{{CheckoutArgsTypeName}}", "tasks": [] }
              ]
            }
            """);

        var services = new ServiceCollection();
        var act = () => services.AddPipelinesFromConfiguration(section);
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*at least one task*");
    }

    [Fact]
    public void Throws_When_ArgumentType_And_PipelineType_Are_Both_Missing()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "broken", "tasks": [ "{{ValidateTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        var act = () => services.AddPipelinesFromConfiguration(section);
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*pipelineType*argumentType*");
    }

    [Fact]
    public void Throws_When_Task_Does_Not_Implement_IPipelineTask_Of_Argument_Type()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "broken", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{WrongArgsTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        var act = () => services.AddPipelinesFromConfiguration(section);
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*must implement*");
    }

    [Fact]
    public void Throws_When_Task_Type_Cannot_Be_Resolved()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "broken", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "MG.Nope.MissingTask" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        var act = () => services.AddPipelinesFromConfiguration(section);
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*could not be loaded*");
    }

    [Fact]
    public void Throws_When_Definition_Has_Missing_Name()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        var act = () => services.AddPipelinesFromConfiguration(section);
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*missing 'name'*");
    }

    [Fact]
    public void Throws_When_Duplicate_Names_Are_Present()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "dupe", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}" ] },
                { "name": "dupe", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ChargeTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        var act = () => services.AddPipelinesFromConfiguration(section);
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*Duplicate pipeline name 'dupe'*");
    }

    [Fact]
    public void Null_Services_Throws()
    {
        var section = ConfigBuilders.BuildSection("""{ "Pipelines": [] }""");
        var act = () => ConfigurationServiceCollectionExtensions.AddPipelinesFromConfiguration(null!, section);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Null_Configuration_Throws()
    {
        var act = () => new ServiceCollection().AddPipelinesFromConfiguration(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Names_With_Colons_Are_Supported()
    {
        // The list-of-definitions schema (vs dictionary-keyed) avoids the IConfiguration colon-as-separator collision.
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "checkout", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}", "{{ChargeTaskTypeName}}" ] },
                { "name": "checkout:vip", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}", "{{FraudCheckTaskTypeName}}", "{{ChargeTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        factory.AllPipelinesFor<CheckoutArgs>().Should().BeEquivalentTo(new[] { "checkout", "checkout:vip" });
        factory.Create<CheckoutArgs>("checkout").Should().NotBeNull();
        factory.Create<CheckoutArgs>("checkout:vip").Should().NotBeNull();
    }

    [Fact]
    public void Tasks_Are_Resolved_From_DI_So_Pre_Registered_Singletons_Are_Honored()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "checkout", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}" ] }
              ]
            }
            """);

        var preregisteredTask = new ValidateTask();
        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        // Pre-register the task as a singleton — TryAddTransient should not overwrite it.
        services.AddSingleton(preregisteredTask);
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IPipelineFactory>().Create<CheckoutArgs>("checkout")!;

        pipeline.Tasks.Should().ContainSingle().Which.Should().BeSameAs(preregisteredTask);
    }
}
