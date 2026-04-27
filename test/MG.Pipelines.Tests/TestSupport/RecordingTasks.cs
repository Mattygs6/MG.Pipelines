using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MG.Pipelines.Tests.TestSupport;

public sealed class Args
{
    public List<string> Log { get; } = new();
}

public sealed class OkTask : IPipelineTask<Args>
{
    public string Id { get; }

    public OkTask(string id) { Id = id; }

    public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
    {
        args.Log.Add($"{Id}:exec");
        return Task.FromResult(PipelineResult.Ok);
    }
}

public sealed class ResultTask : IPipelineTask<Args>
{
    public string Id { get; }
    public PipelineResult Result { get; }

    public ResultTask(string id, PipelineResult result) { Id = id; Result = result; }

    public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
    {
        args.Log.Add($"{Id}:exec");
        return Task.FromResult(Result);
    }
}

public sealed class ThrowingTask : IPipelineTask<Args>
{
    public string Id { get; }
    public Exception Exception { get; }

    public ThrowingTask(string id, Exception exception) { Id = id; Exception = exception; }

    public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
    {
        args.Log.Add($"{Id}:exec");
        throw Exception;
    }
}

public sealed class UndoableTask : IUndoablePipelineTask<Args>
{
    public string Id { get; }
    public PipelineResult Result { get; }

    public UndoableTask(string id, PipelineResult result = PipelineResult.Ok) { Id = id; Result = result; }

    public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
    {
        args.Log.Add($"{Id}:exec");
        return Task.FromResult(Result);
    }

    public Task<PipelineResult> UndoAsync(Args args, CancellationToken cancellationToken = default)
    {
        args.Log.Add($"{Id}:undo");
        return Task.FromResult(PipelineResult.Ok);
    }
}

public sealed class ThrowingUndoTask : IUndoablePipelineTask<Args>
{
    public string Id { get; }
    public Exception UndoException { get; }

    public ThrowingUndoTask(string id, Exception undoException) { Id = id; UndoException = undoException; }

    public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
    {
        args.Log.Add($"{Id}:exec");
        return Task.FromResult(PipelineResult.Ok);
    }

    public Task<PipelineResult> UndoAsync(Args args, CancellationToken cancellationToken = default) =>
        throw UndoException;
}

/// <summary>A task that observes the cancellation token and throws <see cref="OperationCanceledException"/> when cancelled.</summary>
public sealed class CancellationAwareTask : IUndoablePipelineTask<Args>
{
    public string Id { get; }
    public CancellationTokenSource? Trigger { get; }

    public CancellationAwareTask(string id, CancellationTokenSource? trigger = null)
    {
        Id = id;
        Trigger = trigger;
    }

    public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
    {
        args.Log.Add($"{Id}:exec");
        Trigger?.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PipelineResult.Ok);
    }

    public Task<PipelineResult> UndoAsync(Args args, CancellationToken cancellationToken = default)
    {
        args.Log.Add($"{Id}:undo");
        return Task.FromResult(PipelineResult.Ok);
    }
}
