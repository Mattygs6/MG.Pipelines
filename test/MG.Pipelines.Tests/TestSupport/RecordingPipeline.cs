using System;
using System.Collections.Generic;

namespace MG.Pipelines.Tests.TestSupport;

/// <summary>A minimal <see cref="Pipeline{T}"/> used by tests. Captures every logged exception and message.</summary>
public sealed class RecordingPipeline<T> : Pipeline<T>
{
    public RecordingPipeline(IList<IPipelineTask<T>> tasks) : base(tasks)
    {
    }

    public List<(Exception Exception, string Message)> Logged { get; } = new();

    protected override void Log(Exception caughtException, string message) =>
        Logged.Add((caughtException, message));
}
