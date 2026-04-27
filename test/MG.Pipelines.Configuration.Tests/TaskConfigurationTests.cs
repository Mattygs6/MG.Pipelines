using System.Threading.Tasks;

using AwesomeAssertions;

using MG.Pipelines.Attribute;
using MG.Pipelines.Configuration.Tests.TestSupport;
using MG.Pipelines.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace MG.Pipelines.Configuration.Tests;

[Collection(ConfigurationCollection.Name)]
public class TaskConfigurationTests
{
    private static readonly string ConfigurableArgsTypeName = typeof(ConfigurableArgs).AssemblyQualifiedName!;
    private static readonly string HttpCallTaskTypeName = typeof(HttpCallTask).AssemblyQualifiedName!;
    private static readonly string CacheLookupTaskTypeName = typeof(CacheLookupTask).AssemblyQualifiedName!;
    private static readonly string ConfigArgsTaskTypeName = typeof(ConfigArgsTask).AssemblyQualifiedName!;
    private static readonly string RequiredKeywordTaskTypeName = typeof(RequiredKeywordTask).AssemblyQualifiedName!;

    public TaskConfigurationTests()
    {
        Registration.Clear();
    }

    [Fact]
    public async Task Object_Form_Task_Entry_Binds_Config_Onto_Instance()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    {
                      "type": "{{HttpCallTaskTypeName}}",
                      "config": { "ApiKey": "secret-123", "TimeoutSeconds": 30 }
                    }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var pipeline = factory.Create<ConfigurableArgs>("checkout");
        pipeline.Should().NotBeNull();

        var args = factory.CreateArgs<ConfigurableArgs>("checkout");
        (await pipeline!.ExecuteAsync(args)).Should().Be(PipelineResult.Ok);

        args.ObservedCurrency.Should().Be("secret-123");
        args.ObservedMaxRetries.Should().Be(30);
    }

    [Fact]
    public void Missing_Required_Property_Throws_PipelineConfigurationException_At_Create_Time()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    {
                      "type": "{{HttpCallTaskTypeName}}",
                      "config": { "TimeoutSeconds": 10 }
                    }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var act = () => factory.Create<ConfigurableArgs>("checkout");
        act.Should().Throw<PipelineConfigurationException>()
           .WithMessage("*HttpCallTask*failed configuration validation*ApiKey*");
    }

    [Fact]
    public void String_Form_Task_With_Required_Property_Still_Validates_And_Throws()
    {
        // Even when a task is referenced via the bare-string form, it participates in the
        // config-driven validation flow because it's part of a config-registered pipeline.
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{HttpCallTaskTypeName}}" ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var act = () => factory.Create<ConfigurableArgs>("checkout");
        act.Should().Throw<PipelineConfigurationException>()
           .WithMessage("*HttpCallTask*ApiKey*");
    }

    [Fact]
    public async Task Mixed_String_And_Object_Forms_Coexist_In_One_Pipeline()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    "{{ConfigArgsTaskTypeName}}",
                    {
                      "type": "{{HttpCallTaskTypeName}}",
                      "config": { "ApiKey": "k", "TimeoutSeconds": 99 }
                    },
                    {
                      "type": "{{CacheLookupTaskTypeName}}",
                      "config": { "Region": "eu-west", "TtlMinutes": 15 }
                    }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var args = factory.CreateArgs<ConfigurableArgs>("checkout");
        (await factory.Create<ConfigurableArgs>("checkout")!.ExecuteAsync(args))
            .Should().Be(PipelineResult.Ok);

        // Tasks ran in order: ConfigArgsTask wrote defaults to Observed*, then HttpCallTask
        // (configured) overwrote them with its per-instance values.
        args.ObservedCurrency.Should().Be("k");
        args.ObservedMaxRetries.Should().Be(99);
        args.Tags.Should().ContainSingle().Which.Should().Be("cache:eu-west:15");
    }

    [Fact]
    public async Task Per_Task_Config_Differs_Across_Pipelines_Using_The_Same_Task_Type()
    {
        // Same HttpCallTask type, two pipelines, two different configs — proves config is keyed by
        // (pipeline-name, task-index), not by task type.
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "first",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    { "type": "{{HttpCallTaskTypeName}}",
                      "config": { "ApiKey": "first-key", "TimeoutSeconds": 5 } }
                  ]
                },
                {
                  "name": "second",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    { "type": "{{HttpCallTaskTypeName}}",
                      "config": { "ApiKey": "second-key", "TimeoutSeconds": 50 } }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var firstArgs = factory.CreateArgs<ConfigurableArgs>("first");
        await factory.Create<ConfigurableArgs>("first")!.ExecuteAsync(firstArgs);
        firstArgs.ObservedCurrency.Should().Be("first-key");
        firstArgs.ObservedMaxRetries.Should().Be(5);

        var secondArgs = factory.CreateArgs<ConfigurableArgs>("second");
        await factory.Create<ConfigurableArgs>("second")!.ExecuteAsync(secondArgs);
        secondArgs.ObservedCurrency.Should().Be("second-key");
        secondArgs.ObservedMaxRetries.Should().Be(50);
    }

    [Fact]
    public async Task Same_Task_Type_Multiple_Times_In_One_Pipeline_Each_Gets_Its_Own_Config()
    {
        // Position-keyed config supports the same type appearing twice with different values.
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "twice",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    { "type": "{{CacheLookupTaskTypeName}}",
                      "config": { "Region": "us-east", "TtlMinutes": 10 } },
                    { "type": "{{CacheLookupTaskTypeName}}",
                      "config": { "Region": "eu-west", "TtlMinutes": 20 } }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var args = factory.CreateArgs<ConfigurableArgs>("twice");
        await factory.Create<ConfigurableArgs>("twice")!.ExecuteAsync(args);

        args.Tags.Should().Equal("cache:us-east:10", "cache:eu-west:20");
    }

    [Fact]
    public void Object_Form_Without_Type_Throws()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "broken",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    { "config": { "ApiKey": "x" } }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        var act = () => services.AddPipelinesFromConfiguration(section);
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*missing the 'type' property*");
    }

    [Fact]
    public async Task Required_Keyword_Properties_Are_Bound_When_Config_Supplies_Them()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    {
                      "type": "{{RequiredKeywordTaskTypeName}}",
                      "config": { "ApiKey": "live-key", "MaxRetries": 7 }
                    }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var args = factory.CreateArgs<ConfigurableArgs>("checkout");
        (await factory.Create<ConfigurableArgs>("checkout")!.ExecuteAsync(args))
            .Should().Be(PipelineResult.Ok);

        args.ObservedCurrency.Should().Be("live-key");
        args.ObservedMaxRetries.Should().Be(7);
    }

    [Fact]
    public void Required_Keyword_Property_Missing_From_Config_Throws_At_Create_Time()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    {
                      "type": "{{RequiredKeywordTaskTypeName}}",
                      "config": { "ApiKey": "x" }
                    }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var act = () => factory.Create<ConfigurableArgs>("checkout");
        act.Should().Throw<PipelineConfigurationException>()
           .WithMessage("*RequiredKeywordTask*MaxRetries field is required*'required' keyword*");
    }

    [Fact]
    public void Required_Value_Type_Property_Cannot_Be_Satisfied_By_Defaulting_To_Zero()
    {
        // The crucial reason we need this support: a missing `int` cannot be detected by checking
        // the bound value (it's just 0, indistinguishable from "explicitly zero"). The binder must
        // inspect the supplied config keys to know whether MaxRetries was actually provided.
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    {
                      "type": "{{RequiredKeywordTaskTypeName}}",
                      "config": { "ApiKey": "x", "OptionalNote": "hello" }
                    }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var act = () => factory.Create<ConfigurableArgs>("checkout");
        act.Should().Throw<PipelineConfigurationException>()
           .WithMessage("*MaxRetries*required*");
    }

    [Fact]
    public void Required_Keyword_Bare_String_Form_Throws_Because_No_Config_Was_Supplied()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{RequiredKeywordTaskTypeName}}" ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var act = () => factory.Create<ConfigurableArgs>("checkout");
        act.Should().Throw<PipelineConfigurationException>()
           .WithMessage("*RequiredKeywordTask*ApiKey*required*MaxRetries*required*");
    }

    [Fact]
    public async Task Optional_Properties_Retain_Their_Defaults_When_Config_Omits_Them()
    {
        // OptionalNote has a default value and is not 'required'. When config doesn't mention it,
        // the bound instance keeps "default-note".
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    {
                      "type": "{{RequiredKeywordTaskTypeName}}",
                      "config": { "ApiKey": "x", "MaxRetries": 1 }
                    }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var args = factory.CreateArgs<ConfigurableArgs>("checkout");
        await factory.Create<ConfigurableArgs>("checkout")!.ExecuteAsync(args);

        args.Tags.Should().ContainSingle().Which.Should().Be("default-note");
    }

    [Fact]
    public void Task_Binder_Is_Reachable_As_IPipelineTaskInstanceBinder()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [
                    { "type": "{{HttpCallTaskTypeName}}",
                      "config": { "ApiKey": "x" } }
                  ]
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var binders = provider.GetServices<IPipelineTaskInstanceBinder>();
        binders.Should().ContainSingle()
            .Which.Should().BeOfType<ConfigurationPipelineTaskBinder>();
    }
}
