using System.Threading;
using System.Threading.Tasks;

namespace MG.Pipelines;

/// <summary>A pipeline task that can roll back its side effects when a later task fails.</summary>
/// <typeparam name="T">The pipeline argument type.</typeparam>
public interface IUndoablePipelineTask<in T> : IPipelineTask<T>
{
    /// <summary>Attempts to undo the work performed by <see cref="IPipelineTask{T}.ExecuteAsync"/>.</summary>
    /// <param name="args">The pipeline argument.</param>
    /// <param name="cancellationToken">
    /// The same token that was supplied to <see cref="IPipelineTask{T}.ExecuteAsync"/>. If undo must
    /// run to completion regardless of upstream cancellation, the caller (typically a
    /// <see cref="Pipeline{T}"/> override) should pass <see cref="CancellationToken.None"/>.
    /// </param>
    Task<PipelineResult> UndoAsync(T args, CancellationToken cancellationToken = default);
}
