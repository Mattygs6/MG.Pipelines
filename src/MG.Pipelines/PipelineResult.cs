namespace MG.Pipelines;

/// <summary>
/// Outcome of a <see cref="IPipelineTask{T}"/> or <see cref="IPipeline{T}"/> execution.
/// Ordering matters: <see cref="Pipeline{T}"/> short-circuits when a task returns a value greater than <see cref="Warn"/>.
/// </summary>
public enum PipelineResult
{
    /// <summary>Task completed successfully.</summary>
    Ok = 0,

    /// <summary>Task completed but raised a non-fatal warning; execution continues.</summary>
    Warn = 1,

    /// <summary>Task requested that the pipeline stop. Executed tasks are rolled back.</summary>
    Abort = 2,

    /// <summary>Task failed. Executed tasks are rolled back.</summary>
    Fail = 3,
}
