using System;
using System.Threading.Tasks;

using AwesomeAssertions;

using MG.Pipelines.Attribute;
using MG.Pipelines.Configuration.Tests.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace MG.Pipelines.Configuration.Tests;

[Collection(ConfigurationCollection.Name)]
public class LiveTaskMutationTests
{
    private static readonly string CheckoutArgsTypeName = typeof(CheckoutArgs).AssemblyQualifiedName!;
    private static readonly string ValidateTaskTypeName = typeof(ValidateTask).AssemblyQualifiedName!;
    private static readonly string ChargeTaskTypeName = typeof(ChargeTask).AssemblyQualifiedName!;
    private static readonly string FraudCheckTaskTypeName = typeof(FraudCheckTask).AssemblyQualifiedName!;
    private static readonly string SendReceiptTaskTypeName = typeof(SendReceiptTask).AssemblyQualifiedName!;
    private static readonly string WrongArgsTaskTypeName = typeof(WrongArgsTask).AssemblyQualifiedName!;

    public LiveTaskMutationTests()
    {
        Registration.Clear();
    }

    [Fact]
    public async Task SetTasks_Replaces_Task_List_For_Subsequent_Pipeline_Builds()
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
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPipelineTaskRegistry>();
        var factory = provider.GetRequiredService<IPipelineFactory>();
        var counter = provider.GetRequiredService<CounterState>();

        // Baseline build uses the configured tasks.
        await factory.Create<CheckoutArgs>("checkout")!.ExecuteAsync(new CheckoutArgs(counter));
        counter.Calls.Should().Equal("validate", "charge");

        // Live mutation: prepend FraudCheck, append SendReceipt.
        registry.SetTasks("checkout", new[]
        {
            new PipelineTaskSlot(typeof(FraudCheckTask)),
            new PipelineTaskSlot(typeof(ValidateTask)),
            new PipelineTaskSlot(typeof(ChargeTask)),
            new PipelineTaskSlot(typeof(SendReceiptTask)),
        });

        counter.Calls.Clear();
        await factory.Create<CheckoutArgs>("checkout")!.ExecuteAsync(new CheckoutArgs(counter));
        counter.Calls.Should().Equal("fraud", "validate", "charge", "receipt");
    }

    [Fact]
    public async Task SetTasks_Can_Remove_Tasks()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "checkout", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}", "{{ChargeTaskTypeName}}", "{{SendReceiptTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPipelineTaskRegistry>();
        var factory = provider.GetRequiredService<IPipelineFactory>();
        var counter = provider.GetRequiredService<CounterState>();

        // Drop ChargeTask in the middle.
        registry.SetTasks("checkout", new[]
        {
            new PipelineTaskSlot(typeof(ValidateTask)),
            new PipelineTaskSlot(typeof(SendReceiptTask)),
        });

        await factory.Create<CheckoutArgs>("checkout")!.ExecuteAsync(new CheckoutArgs(counter));
        counter.Calls.Should().Equal("validate", "receipt");
    }

    [Fact]
    public async Task SetTasks_With_Runtime_Only_Type_Activates_Without_Pre_Registration()
    {
        // Tasks registered at startup are TryAddTransient'd into DI, but a runtime-added type can be
        // brand-new. The keyed pipeline factory uses ActivatorUtilities.GetServiceOrCreateInstance,
        // so it falls back to direct activation when DI doesn't know the type.
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "checkout", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPipelineTaskRegistry>();
        var factory = provider.GetRequiredService<IPipelineFactory>();
        var counter = provider.GetRequiredService<CounterState>();

        registry.SetTasks("checkout", new[]
        {
            new PipelineTaskSlot(typeof(ValidateTask)),
            new PipelineTaskSlot(typeof(FraudCheckTask)), // not in initial config — never TryAddTransient'd
        });

        await factory.Create<CheckoutArgs>("checkout")!.ExecuteAsync(new CheckoutArgs(counter));
        counter.Calls.Should().Equal("validate", "fraud");
    }

    [Fact]
    public void SetTasks_Rejects_Task_Whose_Type_Does_Not_Match_Argument_Type()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "checkout", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPipelineTaskRegistry>();

        var act = () => registry.SetTasks("checkout", new[]
        {
            new PipelineTaskSlot(typeof(WrongArgsTask)), // IPipelineTask<string>, not <CheckoutArgs>
        });

        act.Should().Throw<PipelineConfigurationException>().WithMessage("*must implement*");

        // Existing list is still intact.
        registry.GetTasks("checkout").Should().ContainSingle()
            .Which.TaskType.Should().Be(typeof(ValidateTask));
    }

    [Fact]
    public void SetTasks_Rejects_Empty_Task_List()
    {
        var section = ConfigBuilders.BuildSection($$"""
            {
              "Pipelines": [
                { "name": "checkout", "argumentType": "{{CheckoutArgsTypeName}}",
                  "tasks": [ "{{ValidateTaskTypeName}}" ] }
              ]
            }
            """);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPipelineTaskRegistry>();

        var act = () => registry.SetTasks("checkout", Array.Empty<PipelineTaskSlot>());
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*at least one task*");
    }

    [Fact]
    public void SetTasks_On_Unknown_Pipeline_Throws_Argument_Exception()
    {
        var section = ConfigBuilders.BuildSection("""{ "Pipelines": [] }""");

        var services = new ServiceCollection();
        services.AddPipelinesFromConfiguration(section);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPipelineTaskRegistry>();

        var act = () => registry.SetTasks("never-registered", new[]
        {
            new PipelineTaskSlot(typeof(ValidateTask)),
        });
        act.Should().Throw<ArgumentException>().WithMessage("*never-registered*not registered*");
    }
}
