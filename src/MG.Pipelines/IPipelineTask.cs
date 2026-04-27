using System.Threading;
using System.Threading.Tasks;

namespace MG.Pipelines;

/// <summary>A single step in an <see cref="IPipeline{T}"/>.</summary>
/// <typeparam name="T">The pipeline argument type.</typeparam>
public interface IPipelineTask<in T>
{
    /// <summary>Executes the task against the supplied argument.</summary>
    /// <param name="args">The pipeline argument.</param>
    /// <param name="cancellationToken">Propagated cancellation. Implementations should observe it.</param>
    Task<PipelineResult> ExecuteAsync(T args, CancellationToken cancellationToken = default);
}
