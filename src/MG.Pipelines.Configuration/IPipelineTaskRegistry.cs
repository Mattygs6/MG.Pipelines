using System.Collections.Generic;

namespace MG.Pipelines.Configuration;

/// <summary>
/// Mutable registry of task lists for config-registered pipelines. Pipelines themselves (their names
/// and argument types) are fixed at <see cref="ConfigurationServiceCollectionExtensions.AddPipelinesFromConfiguration"/>
/// time; only the per-pipeline task list is mutable.
/// </summary>
/// <remarks>
/// All operations are thread-safe. <see cref="SetTasks"/> swaps a pipeline's task list atomically — an
/// in-flight pipeline build always sees a consistent snapshot. The next call to
/// <c>IPipelineFactory.Create&lt;T&gt;(name)</c> picks up the new list.
/// </remarks>
public interface IPipelineTaskRegistry
{
    /// <summary>Returns the names of all pipelines tracked by the registry.</summary>
    IReadOnlyCollection<string> PipelineNames { get; }

    /// <summary>Returns <see langword="true"/> if <paramref name="pipelineName"/> was registered at startup.</summary>
    bool Contains(string pipelineName);

    /// <summary>
    /// The argument type of the named pipeline (the <c>T</c> in <see cref="Pipeline{T}"/>). Fixed at
    /// startup; used to validate that any new task implements <see cref="IPipelineTask{T}"/> for the
    /// same <c>T</c>.
    /// </summary>
    /// <exception cref="System.ArgumentException"><paramref name="pipelineName"/> was not registered.</exception>
    System.Type GetArgumentType(string pipelineName);

    /// <summary>Returns a snapshot of the named pipeline's current task list.</summary>
    /// <exception cref="System.ArgumentException"><paramref name="pipelineName"/> was not registered.</exception>
    IReadOnlyList<PipelineTaskSlot> GetTasks(string pipelineName);

    /// <summary>
    /// Atomically replaces the named pipeline's task list with <paramref name="tasks"/>. Each task type
    /// is validated against the pipeline's argument type before the swap — if any task fails, the swap
    /// is aborted and the existing list is left intact.
    /// </summary>
    /// <exception cref="System.ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="pipelineName"/> was not registered.</exception>
    /// <exception cref="PipelineConfigurationException"><paramref name="tasks"/> is empty or a task fails type validation.</exception>
    void SetTasks(string pipelineName, IEnumerable<PipelineTaskSlot> tasks);
}
