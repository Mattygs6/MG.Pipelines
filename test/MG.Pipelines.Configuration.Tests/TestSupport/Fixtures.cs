using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MG.Pipelines.Attribute;

using Xunit;

namespace MG.Pipelines.Configuration.Tests.TestSupport;

[CollectionDefinition(Name, DisableParallelization = true)]
public class ConfigurationCollection : ICollectionFixture<ConfigurationCollection>
{
    public const string Name = "Configuration";
}

public sealed class CounterState
{
    public List<string> Calls { get; } = new();
}

public sealed class CheckoutArgs
{
    public CounterState Counter { get; }
    public CheckoutArgs(CounterState counter) { Counter = counter; }
}

public sealed class ValidateTask : IPipelineTask<CheckoutArgs>
{
    public Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken cancellationToken = default)
    {
        args.Counter.Calls.Add("validate");
        return Task.FromResult(PipelineResult.Ok);
    }
}

public sealed class ChargeTask : IPipelineTask<CheckoutArgs>
{
    public Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken cancellationToken = default)
    {
        args.Counter.Calls.Add("charge");
        return Task.FromResult(PipelineResult.Ok);
    }
}

public sealed class SendReceiptTask : IPipelineTask<CheckoutArgs>
{
    public Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken cancellationToken = default)
    {
        args.Counter.Calls.Add("receipt");
        return Task.FromResult(PipelineResult.Ok);
    }
}

public sealed class FraudCheckTask : IPipelineTask<CheckoutArgs>
{
    public Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken cancellationToken = default)
    {
        args.Counter.Calls.Add("fraud");
        return Task.FromResult(PipelineResult.Ok);
    }
}

/// <summary>A task whose argument type does not match — used to verify validation.</summary>
public sealed class WrongArgsTask : IPipelineTask<string>
{
    public Task<PipelineResult> ExecuteAsync(string args, CancellationToken cancellationToken = default) =>
        Task.FromResult(PipelineResult.Ok);
}

[Pipeline("attribute-checkout", typeof(CheckoutArgs), typeof(ValidateTask), typeof(ChargeTask))]
public sealed class AttributeCheckoutPipeline : Pipeline<CheckoutArgs>
{
    public AttributeCheckoutPipeline(IList<IPipelineTask<CheckoutArgs>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}

/// <summary>A pipeline declared as a concrete class but no [Pipeline] — referenced only by configuration.</summary>
public sealed class ExplicitConfigPipeline : Pipeline<CheckoutArgs>
{
    public ExplicitConfigPipeline(IList<IPipelineTask<CheckoutArgs>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}

/// <summary>An args type whose properties are bound from the <c>args</c> configuration block.</summary>
public sealed class ConfigurableArgs
{
    public string Currency { get; set; } = "default-USD";
    public int MaxRetries { get; set; } = 1;
    public List<string> Tags { get; set; } = new();
    public LimitsConfig? Limits { get; set; }

    /// <summary>Captured by <see cref="ConfigArgsTask"/> at execute time.</summary>
    public string? ObservedCurrency { get; set; }

    /// <summary>Captured by <see cref="ConfigArgsTask"/> at execute time.</summary>
    public int ObservedMaxRetries { get; set; }
}

public sealed class LimitsConfig
{
    public int DailyCap { get; set; }
}

public sealed class ConfigArgsTask : IPipelineTask<ConfigurableArgs>
{
    public Task<PipelineResult> ExecuteAsync(ConfigurableArgs args, CancellationToken cancellationToken = default)
    {
        args.ObservedCurrency = args.Currency;
        args.ObservedMaxRetries = args.MaxRetries;
        return Task.FromResult(PipelineResult.Ok);
    }
}

/// <summary>An attribute-registered pipeline whose args are populated by configuration's args block.</summary>
[Pipeline("configurable-attribute", typeof(ConfigurableArgs), typeof(ConfigArgsTask))]
public sealed class ConfigurableAttributePipeline : Pipeline<ConfigurableArgs>
{
    public ConfigurableAttributePipeline(IList<IPipelineTask<ConfigurableArgs>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}

/// <summary>A task with per-instance configuration. <see cref="ApiKey"/> is required.</summary>
public sealed class HttpCallTask : IPipelineTask<ConfigurableArgs>
{
    [System.ComponentModel.DataAnnotations.Required]
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 5;

    public Task<PipelineResult> ExecuteAsync(ConfigurableArgs args, CancellationToken cancellationToken = default)
    {
        // Record the values that the binder produced so tests can assert them.
        args.ObservedCurrency = ApiKey;
        args.ObservedMaxRetries = TimeoutSeconds;
        return Task.FromResult(PipelineResult.Ok);
    }
}

/// <summary>A second task with no required properties; used to exercise multiple per-task configs in one pipeline.</summary>
public sealed class CacheLookupTask : IPipelineTask<ConfigurableArgs>
{
    public string Region { get; set; } = "us-east";
    public int TtlMinutes { get; set; } = 60;

    public Task<PipelineResult> ExecuteAsync(ConfigurableArgs args, CancellationToken cancellationToken = default)
    {
        args.Tags.Add($"cache:{Region}:{TtlMinutes}");
        return Task.FromResult(PipelineResult.Ok);
    }
}

/// <summary>
/// A task that uses the C# 11 <c>required</c> keyword. The binder enforces these by checking that
/// the supplied config section actually carries each key (the keyword itself is compile-time only
/// and is bypassed by reflection-based instantiation).
/// </summary>
public sealed class RequiredKeywordTask : IPipelineTask<ConfigurableArgs>
{
    public required string ApiKey { get; set; }

    public required int MaxRetries { get; set; }

    public string OptionalNote { get; set; } = "default-note";

    public Task<PipelineResult> ExecuteAsync(ConfigurableArgs args, CancellationToken cancellationToken = default)
    {
        args.ObservedCurrency = ApiKey;
        args.ObservedMaxRetries = MaxRetries;
        args.Tags.Add(OptionalNote);
        return Task.FromResult(PipelineResult.Ok);
    }
}
