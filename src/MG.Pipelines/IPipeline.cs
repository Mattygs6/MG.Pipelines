using System.Collections.Generic;

namespace MG.Pipelines;

/// <summary>An ordered collection of <see cref="IPipelineTask{T}"/> that share a single argument.</summary>
/// <typeparam name="T">The pipeline argument type.</typeparam>
public interface IPipeline<T>
{
    /// <summary>The tasks, in execution order.</summary>
    IList<IPipelineTask<T>> Tasks { get; }

    /// <summary>Executes the pipeline against the supplied argument.</summary>
    PipelineResult Execute(T args);
}
