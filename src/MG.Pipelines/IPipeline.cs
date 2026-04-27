using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MG.Pipelines;

/// <summary>An ordered collection of <see cref="IPipelineTask{T}"/> that share a single argument.</summary>
/// <typeparam name="T">The pipeline argument type.</typeparam>
public interface IPipeline<T>
{
    /// <summary>The tasks, in execution order.</summary>
    IList<IPipelineTask<T>> Tasks { get; }

    /// <summary>Executes the pipeline against the supplied argument.</summary>
    /// <param name="args">The pipeline argument.</param>
    /// <param name="cancellationToken">Propagated cancellation. Cancelling triggers rollback and rethrows <see cref="System.OperationCanceledException"/>.</param>
    Task<PipelineResult> ExecuteAsync(T args, CancellationToken cancellationToken = default);
}
