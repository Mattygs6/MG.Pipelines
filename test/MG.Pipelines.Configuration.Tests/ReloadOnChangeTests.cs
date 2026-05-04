using System.Collections.Generic;
using System.Threading.Tasks;

using AwesomeAssertions;

using MG.Pipelines.Attribute;
using MG.Pipelines.Configuration.Tests.TestSupport;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

using Xunit;

namespace MG.Pipelines.Configuration.Tests;

[Collection(ConfigurationCollection.Name)]
public class ReloadOnChangeTests
{
    private static readonly string CheckoutArgsTypeName = typeof(CheckoutArgs).AssemblyQualifiedName!;
    private static readonly string ValidateTaskTypeName = typeof(ValidateTask).AssemblyQualifiedName!;
    private static readonly string ChargeTaskTypeName = typeof(ChargeTask).AssemblyQualifiedName!;
    private static readonly string FraudCheckTaskTypeName = typeof(FraudCheckTask).AssemblyQualifiedName!;
    private static readonly string SendReceiptTaskTypeName = typeof(SendReceiptTask).AssemblyQualifiedName!;
    private static readonly string WrongArgsTaskTypeName = typeof(WrongArgsTask).AssemblyQualifiedName!;

    public ReloadOnChangeTests()
    {
        Registration.Clear();
    }

    [Fact]
    public async Task Configuration_Reload_Updates_Existing_Pipeline_Task_List()
    {
        var source = new ReloadableSource(new Dictionary<string, string?>
        {
            ["Pipelines:0:name"] = "checkout",
            ["Pipelines:0:argumentType"] = CheckoutArgsTypeName,
            ["Pipelines:0:tasks:0"] = ValidateTaskTypeName,
            ["Pipelines:0:tasks:1"] = ChargeTaskTypeName,
        });

        var config = new ConfigurationBuilder().Add(source).Build();

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(config.GetSection("Pipelines"));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();
        var counter = provider.GetRequiredService<CounterState>();

        // Reload with a new task list (FraudCheck then SendReceipt).
        source.SetData(new Dictionary<string, string?>
        {
            ["Pipelines:0:name"] = "checkout",
            ["Pipelines:0:argumentType"] = CheckoutArgsTypeName,
            ["Pipelines:0:tasks:0"] = FraudCheckTaskTypeName,
            ["Pipelines:0:tasks:1"] = SendReceiptTaskTypeName,
        });

        await factory.Create<CheckoutArgs>("checkout")!.ExecuteAsync(new CheckoutArgs(counter));
        counter.Calls.Should().Equal("fraud", "receipt");
    }

    [Fact]
    public async Task Reload_With_Invalid_Task_Type_Leaves_Existing_Tasks_Intact()
    {
        var source = new ReloadableSource(new Dictionary<string, string?>
        {
            ["Pipelines:0:name"] = "checkout",
            ["Pipelines:0:argumentType"] = CheckoutArgsTypeName,
            ["Pipelines:0:tasks:0"] = ValidateTaskTypeName,
            ["Pipelines:0:tasks:1"] = ChargeTaskTypeName,
        });

        var config = new ConfigurationBuilder().Add(source).Build();

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(config.GetSection("Pipelines"));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IPipelineFactory>();
        var counter = provider.GetRequiredService<CounterState>();

        // Reload with a task whose argument type doesn't match — should be skipped silently.
        source.SetData(new Dictionary<string, string?>
        {
            ["Pipelines:0:name"] = "checkout",
            ["Pipelines:0:argumentType"] = CheckoutArgsTypeName,
            ["Pipelines:0:tasks:0"] = WrongArgsTaskTypeName,
        });

        await factory.Create<CheckoutArgs>("checkout")!.ExecuteAsync(new CheckoutArgs(counter));
        counter.Calls.Should().Equal("validate", "charge");
    }

    [Fact]
    public async Task Reload_Ignores_Newly_Introduced_Pipeline_Names()
    {
        // Pipelines are fixed at startup. A name appearing only in the reloaded config is ignored —
        // the registry still tracks the original "checkout" only.
        var source = new ReloadableSource(new Dictionary<string, string?>
        {
            ["Pipelines:0:name"] = "checkout",
            ["Pipelines:0:argumentType"] = CheckoutArgsTypeName,
            ["Pipelines:0:tasks:0"] = ValidateTaskTypeName,
        });

        var config = new ConfigurationBuilder().Add(source).Build();

        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddLogging();
        services.AddPipelinesFromConfiguration(config.GetSection("Pipelines"));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPipelineTaskRegistry>();

        source.SetData(new Dictionary<string, string?>
        {
            ["Pipelines:0:name"] = "checkout",
            ["Pipelines:0:argumentType"] = CheckoutArgsTypeName,
            ["Pipelines:0:tasks:0"] = ValidateTaskTypeName,
            ["Pipelines:1:name"] = "brand-new",
            ["Pipelines:1:argumentType"] = CheckoutArgsTypeName,
            ["Pipelines:1:tasks:0"] = ChargeTaskTypeName,
        });

        registry.PipelineNames.Should().BeEquivalentTo(new[] { "checkout" });
        registry.Contains("brand-new").Should().BeFalse();

        var factory = provider.GetRequiredService<IPipelineFactory>();
        factory.Create<CheckoutArgs>("brand-new").Should().BeNull();

        await Task.CompletedTask;
    }

    /// <summary>
    /// An <see cref="IConfigurationSource"/> backed by an in-memory dictionary that can be replaced at
    /// runtime; replacement raises a reload notification (the same mechanism used by reload-on-change
    /// JSON file sources).
    /// </summary>
    private sealed class ReloadableSource : IConfigurationSource
    {
        private readonly ReloadableProvider provider;
        public ReloadableSource(Dictionary<string, string?> initial) { provider = new ReloadableProvider(initial); }
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
        public void SetData(Dictionary<string, string?> next) => provider.SetData(next);
    }

    private sealed class ReloadableProvider : ConfigurationProvider
    {
        public ReloadableProvider(Dictionary<string, string?> initial)
        {
            Data = new Dictionary<string, string?>(initial, System.StringComparer.OrdinalIgnoreCase);
        }

        public void SetData(Dictionary<string, string?> next)
        {
            Data = new Dictionary<string, string?>(next, System.StringComparer.OrdinalIgnoreCase);
            OnReload(); // raises GetReloadToken's signal
        }
    }
}
