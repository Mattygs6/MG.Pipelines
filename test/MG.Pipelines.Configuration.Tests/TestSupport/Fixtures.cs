using System;
using System.Collections.Generic;

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
    public PipelineResult Execute(CheckoutArgs args)
    {
        args.Counter.Calls.Add("validate");
        return PipelineResult.Ok;
    }
}

public sealed class ChargeTask : IPipelineTask<CheckoutArgs>
{
    public PipelineResult Execute(CheckoutArgs args)
    {
        args.Counter.Calls.Add("charge");
        return PipelineResult.Ok;
    }
}

public sealed class SendReceiptTask : IPipelineTask<CheckoutArgs>
{
    public PipelineResult Execute(CheckoutArgs args)
    {
        args.Counter.Calls.Add("receipt");
        return PipelineResult.Ok;
    }
}

public sealed class FraudCheckTask : IPipelineTask<CheckoutArgs>
{
    public PipelineResult Execute(CheckoutArgs args)
    {
        args.Counter.Calls.Add("fraud");
        return PipelineResult.Ok;
    }
}

/// <summary>A task whose argument type does not match — used to verify validation.</summary>
public sealed class WrongArgsTask : IPipelineTask<string>
{
    public PipelineResult Execute(string args) => PipelineResult.Ok;
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
