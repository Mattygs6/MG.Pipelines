using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MG.Pipelines.Attribute;

using Xunit;

namespace MG.Pipelines.DependencyInjection.Tests.TestSupport;

[CollectionDefinition(Name, DisableParallelization = true)]
public class DiCollection : ICollectionFixture<DiCollection>
{
    public const string Name = "DI";
}

public sealed class Counter
{
    public int Value { get; set; }
}

public sealed class Args
{
    public List<string> Log { get; } = new();
}

public sealed class Increment : IPipelineTask<Args>
{
    private readonly Counter counter;

    public Increment(Counter counter) { this.counter = counter; }

    public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
    {
        counter.Value++;
        args.Log.Add($"inc={counter.Value}");
        return Task.FromResult(PipelineResult.Ok);
    }
}

public sealed class Double : IPipelineTask<Args>
{
    private readonly Counter counter;

    public Double(Counter counter) { this.counter = counter; }

    public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
    {
        counter.Value *= 2;
        args.Log.Add($"dbl={counter.Value}");
        return Task.FromResult(PipelineResult.Ok);
    }
}

[Pipeline("arithmetic", typeof(Args), typeof(Increment), typeof(Double))]
public sealed class ArithmeticPipeline : Pipeline<Args>
{
    public ArithmeticPipeline(IList<IPipelineTask<Args>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}

[Pipeline("arithmetic:specific", typeof(Args), typeof(Double), typeof(Increment))]
public sealed class SpecificArithmeticPipeline : Pipeline<Args>
{
    public SpecificArithmeticPipeline(IList<IPipelineTask<Args>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}

public sealed class PrefixResolver : IPipelineNameResolver
{
    public IList<string> ResolveNames(string localName) =>
        new[] { localName + ":specific", localName };
}
