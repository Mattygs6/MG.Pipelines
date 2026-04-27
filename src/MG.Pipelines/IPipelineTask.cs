namespace MG.Pipelines;

/// <summary>A single step in an <see cref="IPipeline{T}"/>.</summary>
/// <typeparam name="T">The pipeline argument type.</typeparam>
public interface IPipelineTask<in T>
{
    /// <summary>Executes the task against the supplied argument.</summary>
    PipelineResult Execute(T args);
}
