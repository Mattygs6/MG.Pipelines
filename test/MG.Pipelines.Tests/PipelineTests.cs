using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AwesomeAssertions;

using MG.Pipelines.Tests.TestSupport;

using Xunit;

namespace MG.Pipelines.Tests;

public class PipelineTests
{
    [Fact]
    public async Task All_Ok_Runs_Every_Task_In_Order_And_Returns_Ok()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new OkTask("a"),
            new OkTask("b"),
            new OkTask("c"),
        });

        var result = await pipeline.ExecuteAsync(args);

        result.Should().Be(PipelineResult.Ok);
        args.Log.Should().Equal("a:exec", "b:exec", "c:exec");
    }

    [Fact]
    public async Task Warn_Is_Recorded_But_Does_Not_Short_Circuit()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new OkTask("a"),
            new ResultTask("b", PipelineResult.Warn),
            new OkTask("c"),
        });

        var result = await pipeline.ExecuteAsync(args);

        result.Should().Be(PipelineResult.Warn);
        args.Log.Should().Equal("a:exec", "b:exec", "c:exec");
    }

    [Fact]
    public async Task Abort_Short_Circuits_And_Undoes_Executed_Tasks_In_Reverse()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new UndoableTask("a"),
            new UndoableTask("b"),
            new ResultTask("c", PipelineResult.Abort),     // aborts; included in rollback set
            new OkTask("d"),                                // never runs
        });

        var result = await pipeline.ExecuteAsync(args);

        result.Should().Be(PipelineResult.Abort);
        // c ran (and aborted); d did not. Undo is reverse order across executed tasks;
        // c is not undoable so it is skipped.
        args.Log.Should().Equal("a:exec", "b:exec", "c:exec", "b:undo", "a:undo");
    }

    [Fact]
    public async Task Fail_Short_Circuits_And_Returns_Fail()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new OkTask("a"),
            new ResultTask("b", PipelineResult.Fail),
            new OkTask("c"),
        });

        var result = await pipeline.ExecuteAsync(args);

        result.Should().Be(PipelineResult.Fail);
        args.Log.Should().Equal("a:exec", "b:exec");
    }

    [Fact]
    public async Task Exception_Is_Wrapped_In_PipelineException_And_Undo_Is_Attempted()
    {
        var args = new Args();
        var inner = new InvalidOperationException("boom");
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new UndoableTask("a"),
            new ThrowingTask("b", inner),
        });

        var act = async () => await pipeline.ExecuteAsync(args);

        var thrown = await act.Should().ThrowAsync<PipelineException>();
        thrown.Which.InnerException.Should().BeSameAs(inner);

        // a was executed and undone; b threw before returning.
        args.Log.Should().Equal("a:exec", "b:exec", "a:undo");
        pipeline.Logged.Should().ContainSingle().Which.Exception.Should().BeSameAs(inner);
    }

    [Fact]
    public async Task Undo_Skips_NonUndoable_Tasks()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new UndoableTask("a"),
            new OkTask("b"),                                // non-undoable; should be skipped during rollback
            new ResultTask("c", PipelineResult.Fail),
        });

        (await pipeline.ExecuteAsync(args)).Should().Be(PipelineResult.Fail);

        args.Log.Should().Equal("a:exec", "b:exec", "c:exec", "a:undo");
    }

    [Fact]
    public async Task Undo_Exception_Is_Logged_But_Original_Is_Still_Thrown()
    {
        var args = new Args();
        var inner = new InvalidOperationException("exec-boom");
        var undoEx = new InvalidOperationException("undo-boom");
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new ThrowingUndoTask("a", undoEx),
            new ThrowingTask("b", inner),
        });

        var act = async () => await pipeline.ExecuteAsync(args);

        // The execute-phase exception is the primary cause; undo failure is logged.
        var thrown = await act.Should().ThrowAsync<PipelineException>();
        thrown.Which.InnerException.Should().BeSameAs(inner);

        pipeline.Logged.Should().HaveCount(2);
        pipeline.Logged.Should().Contain(entry => entry.Exception == inner);
        pipeline.Logged.Should().Contain(entry => entry.Exception == undoEx);
    }

    [Fact]
    public async Task Empty_Task_List_Returns_Ok_Without_Error()
    {
        var pipeline = new RecordingPipeline<Args>(new List<IPipelineTask<Args>>());
        (await pipeline.ExecuteAsync(new Args())).Should().Be(PipelineResult.Ok);
    }

    [Fact]
    public void Null_Tasks_Throws()
    {
        var act = () => new RecordingPipeline<Args>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Tasks_Property_Exposes_Supplied_List()
    {
        var tasks = new IPipelineTask<Args>[] { new OkTask("a") };
        var pipeline = new RecordingPipeline<Args>(tasks);
        pipeline.Tasks.Should().BeSameAs(tasks);
    }

    [Fact]
    public async Task Pre_Cancelled_Token_Throws_Before_Any_Task_Runs()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[] { new OkTask("a") });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await pipeline.ExecuteAsync(args, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // No task ran, no undo to run.
        args.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_Mid_Pipeline_Triggers_Undo_And_Rethrows_OCE_Unwrapped()
    {
        var args = new Args();
        using var cts = new CancellationTokenSource();

        // Second task cancels the token from inside ExecuteAsync, then observes it.
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new UndoableTask("a"),
            new CancellationAwareTask("b", cts),
            new OkTask("c"),
        });

        var act = async () => await pipeline.ExecuteAsync(args, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // a executed and was undone; b executed (and threw OCE); c never ran.
        args.Log.Should().Equal("a:exec", "b:exec", "b:undo", "a:undo");
        // OCE is NOT logged via Pipeline<T>.Log — control flow, not an error.
        pipeline.Logged.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_Between_Tasks_Triggers_Undo_And_Rethrows_OCE()
    {
        var args = new Args();
        using var cts = new CancellationTokenSource();

        // First task cancels the token after running. The pipeline detects the cancellation
        // at the next task boundary (before the second task runs) and rolls back.
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new CancellationOnExitTask("a", cts),
            new OkTask("b"),
        });

        var act = async () => await pipeline.ExecuteAsync(args, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        args.Log.Should().Equal("a:exec", "a:undo");
    }

    [Fact]
    public async Task Default_Cancellation_Token_Works_Like_None()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new OkTask("a"),
            new OkTask("b"),
        });

        // No token argument == CancellationToken.None — never cancelled.
        var result = await pipeline.ExecuteAsync(args);
        result.Should().Be(PipelineResult.Ok);
        args.Log.Should().Equal("a:exec", "b:exec");
    }

    private sealed class CancellationOnExitTask : IUndoablePipelineTask<Args>
    {
        private readonly string id;
        private readonly CancellationTokenSource trigger;

        public CancellationOnExitTask(string id, CancellationTokenSource trigger)
        {
            this.id = id;
            this.trigger = trigger;
        }

        public Task<PipelineResult> ExecuteAsync(Args args, CancellationToken cancellationToken = default)
        {
            args.Log.Add($"{id}:exec");
            trigger.Cancel();
            return Task.FromResult(PipelineResult.Ok);
        }

        public Task<PipelineResult> UndoAsync(Args args, CancellationToken cancellationToken = default)
        {
            args.Log.Add($"{id}:undo");
            return Task.FromResult(PipelineResult.Ok);
        }
    }
}
