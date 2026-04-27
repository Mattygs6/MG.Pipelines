using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MG.Pipelines;

/// <summary>
/// Runs an ordered list of <see cref="IPipelineTask{T}"/>, escalating the overall result and
/// short-circuiting once a task returns a value greater than <see cref="PipelineResult.Warn"/>.
/// When a task fails or throws, previously executed <see cref="IUndoablePipelineTask{T}"/> instances
/// are rolled back in reverse order.
/// </summary>
/// <remarks>
/// Cancellation: when the supplied <see cref="CancellationToken"/> is cancelled, the pipeline stops
/// at the next task boundary and triggers rollback. The <see cref="OperationCanceledException"/>
/// is rethrown to the caller <em>unwrapped</em> (it does not become a <see cref="PipelineException"/>).
/// Undo runs with the same (cancelled) token; override <see cref="UndoAsync"/> to use a different
/// token if cleanup work must be allowed to complete.
/// </remarks>
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
    /// <exception cref="OperationCanceledException">The supplied <paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PipelineResult> ExecuteAsync(T args, CancellationToken cancellationToken = default)
    {
        var pipelineResult = PipelineResult.Ok;
        var executedTasks = new List<IPipelineTask<T>>();
        var isAborted = false;
        Exception? exception = null;
        OperationCanceledException? cancellation = null;

        foreach (var task in Tasks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellation = new OperationCanceledException(cancellationToken);
                isAborted = true;
                break;
            }

            executedTasks.Add(task);

            PipelineResult taskResult;
            try
            {
                taskResult = await task.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException oce)
            {
                cancellation = oce;
                isAborted = true;
                break;
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
                await UndoAsync(executedTasks, args, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation during undo is expected if the same token is in use; surface the
                // original cancellation to the caller rather than masking it.
            }
            catch (Exception ex)
            {
                Log(ex, FormatMessage("undoing"));
                // The original cause (task exception or cancellation) takes precedence.
                exception ??= cancellation is null ? ex : null;
            }
        }

        if (cancellation is not null)
        {
            throw cancellation;
        }

        if (exception is not null)
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
    protected virtual async Task UndoAsync(IList<IPipelineTask<T>> executedTasks, T args, CancellationToken cancellationToken)
    {
        for (var i = executedTasks.Count - 1; i >= 0; i--)
        {
            if (executedTasks[i] is IUndoablePipelineTask<T> undoable)
            {
                await undoable.UndoAsync(args, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string FormatMessage(string verb) =>
        $"Exception occurred while {verb} pipeline '{GetType().FullName}'. See inner exception for details.";
}
