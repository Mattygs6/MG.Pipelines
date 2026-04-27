using System.Collections.Generic;

using AwesomeAssertions;

using MG.Pipelines.Attribute;
using MG.Pipelines.Configuration.Tests.TestSupport;
using MG.Pipelines.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace MG.Pipelines.Configuration.Tests;

[Collection(ConfigurationCollection.Name)]
public class ArgsConfigurationTests
{
    private static readonly string ConfigurableArgsTypeName = typeof(ConfigurableArgs).AssemblyQualifiedName!;
    private static readonly string ConfigArgsTaskTypeName = typeof(ConfigArgsTask).AssemblyQualifiedName!;

    public ArgsConfigurationTests()
    {
        Registration.Clear();
    }

    [Fact]
    public void Args_Section_Is_Bound_Onto_Fresh_Instance_From_CreateArgs()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": {
                    "Currency": "USD",
                    "MaxRetries": 3,
                    "Tags": [ "alpha", "beta" ],
                    "Limits": { "DailyCap": 5000 }
                  }
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

        args.Currency.Should().Be("USD");
        args.MaxRetries.Should().Be(3);
        args.Tags.Should().Equal("alpha", "beta");
        args.Limits.Should().NotBeNull();
        args.Limits!.DailyCap.Should().Be(5000);
    }

    [Fact]
    public void Pipeline_Without_Args_Section_Yields_Default_Constructed_Instance()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ]
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

        args.Currency.Should().Be("default-USD");                // ConfigurableArgs default
        args.MaxRetries.Should().Be(1);
        args.Tags.Should().BeEmpty();
        args.Limits.Should().BeNull();
    }

    [Fact]
    public void Caller_Can_Override_Configured_Defaults_After_CreateArgs()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": { "Currency": "USD", "MaxRetries": 3 }
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

        // Request-specific override after defaults are applied.
        args.Currency = "EUR";

        args.Currency.Should().Be("EUR");
        args.MaxRetries.Should().Be(3); // not overridden
    }

    [Fact]
    public void End_To_End_CreateArgs_Then_Execute_Pipeline_Uses_Configured_Values()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": { "Currency": "GBP", "MaxRetries": 7 }
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
        var pipeline = factory.Create<ConfigurableArgs>("checkout");

        pipeline!.Execute(args).Should().Be(PipelineResult.Ok);

        // ConfigArgsTask records the values it observed at execution time.
        args.ObservedCurrency.Should().Be("GBP");
        args.ObservedMaxRetries.Should().Be(7);
    }

    [Fact]
    public void Multiple_AddPipelinesFromConfiguration_Calls_Accumulate_Args_Bindings()
    {
        var first = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": { "Currency": "USD" }
                }
              ]
            }
            """);

        var second = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "renewal",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": { "Currency": "EUR", "MaxRetries": 9 }
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(first);
        services.AddPipelinesFromConfiguration(second);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        factory.CreateArgs<ConfigurableArgs>("checkout").Currency.Should().Be("USD");

        var renewal = factory.CreateArgs<ConfigurableArgs>("renewal");
        renewal.Currency.Should().Be("EUR");
        renewal.MaxRetries.Should().Be(9);
    }

    [Fact]
    public void Same_Name_In_Second_Configuration_Replaces_The_Args_Binding()
    {
        var first = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": { "Currency": "USD", "MaxRetries": 3 }
                }
              ]
            }
            """);

        var second = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": { "Currency": "EUR" }
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(first);
        services.AddPipelinesFromConfiguration(second);

        using var provider = services.BuildServiceProvider();
        var args = provider.GetRequiredService<IPipelineFactory>().CreateArgs<ConfigurableArgs>("checkout");

        // Second source replaces the first wholesale — MaxRetries reverts to its type default.
        args.Currency.Should().Be("EUR");
        args.MaxRetries.Should().Be(1);
    }

    [Fact]
    public void Binder_Is_Reachable_As_IPipelineArgsBinder_Service()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "checkout",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": { "Currency": "USD" }
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var binder = provider.GetRequiredService<IPipelineArgsBinder>();
        binder.Should().BeOfType<ConfigurationPipelineArgsBinder>();

        var args = new ConfigurableArgs();
        binder.Bind("checkout", args);
        args.Currency.Should().Be("USD");
    }

    [Fact]
    public void Args_Binding_Coexists_With_Attribute_Pipelines()
    {
        // An attribute pipeline registered first; configuration adds args defaults for it.
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                {
                  "name": "configurable-attribute",
                  "argumentType": "{{ConfigurableArgsTypeName}}",
                  "tasks": [ "{{ConfigArgsTaskTypeName}}" ],
                  "args": { "Currency": "JPY", "MaxRetries": 4 }
                }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelines(typeof(ConfigurableAttributePipeline).Assembly);
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();

        var args = factory.CreateArgs<ConfigurableArgs>("configurable-attribute");
        args.Currency.Should().Be("JPY");
        args.MaxRetries.Should().Be(4);
    }

    [Fact]
    public void Empty_Configuration_Produces_Default_Args_When_CreateArgs_Is_Called()
    {
        // No pipelines configured — CreateArgs should still return a default instance (no binder match).
        var section = ConfigBuilders.BuildSection("""{ "Pipelines": [] }""");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var args = provider.GetRequiredService<IPipelineFactory>()
            .CreateArgs<ConfigurableArgs>("never-registered");

        args.Currency.Should().Be("default-USD");
        args.MaxRetries.Should().Be(1);
    }
}
