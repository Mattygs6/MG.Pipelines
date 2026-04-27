namespace MG.Pipelines;

/// <summary>A pipeline task that can roll back its side effects when a later task fails.</summary>
/// <typeparam name="T">The pipeline argument type.</typeparam>
public interface IUndoablePipelineTask<in T> : IPipelineTask<T>
{
    /// <summary>Attempts to undo the work performed by <see cref="IPipelineTask{T}.Execute"/>.</summary>
    PipelineResult Undo(T args);
}
