using System;
using System.Collections.Generic;

namespace MG.Pipelines.Tests.TestSupport;

public sealed class Args
{
    public List<string> Log { get; } = new();
}

public sealed class OkTask : IPipelineTask<Args>
{
    public string Id { get; }

    public OkTask(string id) { Id = id; }

    public PipelineResult Execute(Args args)
    {
        args.Log.Add($"{Id}:exec");
        return PipelineResult.Ok;
    }
}

public sealed class ResultTask : IPipelineTask<Args>
{
    public string Id { get; }
    public PipelineResult Result { get; }

    public ResultTask(string id, PipelineResult result) { Id = id; Result = result; }

    public PipelineResult Execute(Args args)
    {
        args.Log.Add($"{Id}:exec");
        return Result;
    }
}

public sealed class ThrowingTask : IPipelineTask<Args>
{
    public string Id { get; }
    public Exception Exception { get; }

    public ThrowingTask(string id, Exception exception) { Id = id; Exception = exception; }

    public PipelineResult Execute(Args args)
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

    public PipelineResult Execute(Args args)
    {
        args.Log.Add($"{Id}:exec");
        return Result;
    }

    public PipelineResult Undo(Args args)
    {
        args.Log.Add($"{Id}:undo");
        return PipelineResult.Ok;
    }
}

public sealed class ThrowingUndoTask : IUndoablePipelineTask<Args>
{
    public string Id { get; }
    public Exception UndoException { get; }

    public ThrowingUndoTask(string id, Exception undoException) { Id = id; UndoException = undoException; }

    public PipelineResult Execute(Args args)
    {
        args.Log.Add($"{Id}:exec");
        return PipelineResult.Ok;
    }

    public PipelineResult Undo(Args args) => throw UndoException;
}
