namespace MG.Pipelines.DependencyInjection;

/// <summary>
/// Applies per-instance customisation to a task immediately after it is resolved from the
/// <see cref="System.IServiceProvider"/> during pipeline construction. Implementations are typically
/// supplied by the configuration package (binding from <c>IConfiguration</c> and validating
/// <c>System.ComponentModel.DataAnnotations</c> attributes) but the contract is open for any source.
/// </summary>
/// <remarks>
/// Tasks targeted by an <see cref="IPipelineTaskInstanceBinder"/> should be transient (the default).
/// Pre-registering as singleton would let successive pipeline builds overwrite each other's per-instance
/// state.
/// </remarks>
public interface IPipelineTaskInstanceBinder
{
    /// <summary>
    /// Mutates <paramref name="taskInstance"/> in place. Implementations must be a no-op when
    /// nothing is registered for the supplied (<paramref name="pipelineName"/>, <paramref name="taskIndex"/>) pair.
    /// </summary>
    /// <param name="pipelineName">The pipeline registration name.</param>
    /// <param name="taskIndex">Zero-based position of the task within the pipeline's task list.</param>
    /// <param name="taskInstance">The freshly resolved task.</param>
    void Bind(string pipelineName, int taskIndex, object taskInstance);
}
