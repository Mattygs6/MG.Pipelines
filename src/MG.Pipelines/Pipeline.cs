using System;
using System.Collections.Generic;

namespace MG.Pipelines;

/// <summary>
/// Runs an ordered list of <see cref="IPipelineTask{T}"/>, escalating the overall result and
/// short-circuiting once a task returns a value greater than <see cref="PipelineResult.Warn"/>.
/// When a task fails or throws, previously executed <see cref="IUndoablePipelineTask{T}"/> instances
/// are rolled back in reverse order.
/// </summary>
/// <typeparam name="T">The pipeline argument type.</typeparam>
public abstract class Pipeline<T> : IPipeline<T>
{
    /// <summary>Initializes the pipeline with the supplied tasks (in execution order).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is <see langword="null"/>.</exception>
    protected Pipeline(IList<IPipelineTask<T>> tasks)
    {
        Tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
    }

    /// <inheritdoc/>
    public IList<IPipelineTask<T>> Tasks { get; }

    /// <inheritdoc/>
    /// <exception cref="PipelineException">An unhandled exception occurred while running a task or rolling back.</exception>
    public PipelineResult Execute(T args)
    {
        var pipelineResult = PipelineResult.Ok;
        var executedTasks = new List<IPipelineTask<T>>();
        var isAborted = false;
        Exception? exception = null;

        foreach (var task in Tasks)
        {
            executedTasks.Add(task);

            PipelineResult taskResult;
            try
            {
                taskResult = task.Execute(args);
            }
            catch (Exception ex)
            {
                exception = ex;
                Log(ex, FormatMessage("processing"));
                isAborted = true;
                break;
            }

            if (taskResult == PipelineResult.Warn)
            {
                if (pipelineResult < PipelineResult.Warn)
                {
                    pipelineResult = PipelineResult.Warn;
                }
            }
            else if (taskResult > PipelineResult.Warn)
            {
                pipelineResult = taskResult;
                isAborted = true;
                break;
            }
        }

        if (isAborted)
        {
            try
            {
                Undo(executedTasks, args);
            }
            catch (Exception ex)
            {
                // Preserve the originating exception (if any) as the cause; log the undo failure.
                Log(ex, FormatMessage("undoing"));
                exception ??= ex;
            }
        }

        if (exception != null)
        {
            throw new PipelineException(FormatMessage("processing"), exception);
        }

        return pipelineResult;
    }

    /// <summary>Logs an unhandled exception. Implementations must not throw.</summary>
    protected abstract void Log(Exception caughtException, string message);

    /// <summary>
    /// Rolls back executed tasks in reverse order. Only tasks implementing
    /// <see cref="IUndoablePipelineTask{T}"/> are rolled back; others are skipped.
    /// The task that aborted or threw is included in the rollback set.
    /// </summary>
    protected virtual void Undo(IList<IPipelineTask<T>> executedTasks, T args)
    {
        for (var i = executedTasks.Count - 1; i >= 0; i--)
        {
            if (executedTasks[i] is IUndoablePipelineTask<T> undoable)
            {
                undoable.Undo(args);
            }
        }
    }

    private string FormatMessage(string verb) =>
        $"Exception occurred while {verb} pipeline '{GetType().FullName}'. See inner exception for details.";
}
