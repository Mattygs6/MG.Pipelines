using System;
using System.Collections.Generic;

using AwesomeAssertions;

using MG.Pipelines.Tests.TestSupport;

using Xunit;

namespace MG.Pipelines.Tests;

public class PipelineTests
{
    [Fact]
    public void All_Ok_Runs_Every_Task_In_Order_And_Returns_Ok()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new OkTask("a"),
            new OkTask("b"),
            new OkTask("c"),
        });

        var result = pipeline.Execute(args);

        result.Should().Be(PipelineResult.Ok);
        args.Log.Should().Equal("a:exec", "b:exec", "c:exec");
    }

    [Fact]
    public void Warn_Is_Recorded_But_Does_Not_Short_Circuit()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new OkTask("a"),
            new ResultTask("b", PipelineResult.Warn),
            new OkTask("c"),
        });

        var result = pipeline.Execute(args);

        result.Should().Be(PipelineResult.Warn);
        args.Log.Should().Equal("a:exec", "b:exec", "c:exec");
    }

    [Fact]
    public void Abort_Short_Circuits_And_Undoes_Executed_Tasks_In_Reverse()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new UndoableTask("a"),
            new UndoableTask("b"),
            new ResultTask("c", PipelineResult.Abort),     // aborts; included in rollback set
            new OkTask("d"),                                // never runs
        });

        var result = pipeline.Execute(args);

        result.Should().Be(PipelineResult.Abort);
        // c ran (and aborted); d did not. Undo is reverse order across executed tasks;
        // c is not undoable so it is skipped.
        args.Log.Should().Equal("a:exec", "b:exec", "c:exec", "b:undo", "a:undo");
    }

    [Fact]
    public void Fail_Short_Circuits_And_Returns_Fail()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new OkTask("a"),
            new ResultTask("b", PipelineResult.Fail),
            new OkTask("c"),
        });

        var result = pipeline.Execute(args);

        result.Should().Be(PipelineResult.Fail);
        args.Log.Should().Equal("a:exec", "b:exec");
    }

    [Fact]
    public void Exception_Is_Wrapped_In_PipelineException_And_Undo_Is_Attempted()
    {
        var args = new Args();
        var inner = new InvalidOperationException("boom");
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new UndoableTask("a"),
            new ThrowingTask("b", inner),
        });

        var act = () => pipeline.Execute(args);

        var ex = act.Should().Throw<PipelineException>()
            .WithInnerException<InvalidOperationException>()
            .Which;
        ex.Message.Should().Be(inner.Message);

        // a was executed and undone; b threw before returning.
        args.Log.Should().Equal("a:exec", "b:exec", "a:undo");
        pipeline.Logged.Should().ContainSingle().Which.Exception.Should().BeSameAs(inner);
    }

    [Fact]
    public void Undo_Skips_NonUndoable_Tasks()
    {
        var args = new Args();
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new UndoableTask("a"),
            new OkTask("b"),                                // non-undoable; should be skipped during rollback
            new ResultTask("c", PipelineResult.Fail),
        });

        pipeline.Execute(args).Should().Be(PipelineResult.Fail);

        args.Log.Should().Equal("a:exec", "b:exec", "c:exec", "a:undo");
    }

    [Fact]
    public void Undo_Exception_Is_Logged_But_Original_Is_Still_Thrown()
    {
        var args = new Args();
        var inner = new InvalidOperationException("exec-boom");
        var undoEx = new InvalidOperationException("undo-boom");
        var pipeline = new RecordingPipeline<Args>(new IPipelineTask<Args>[]
        {
            new ThrowingUndoTask("a", undoEx),
            new ThrowingTask("b", inner),
        });

        var act = () => pipeline.Execute(args);

        // The execute-phase exception is the primary cause; undo failure is logged.
        act.Should().Throw<PipelineException>()
           .WithInnerException<InvalidOperationException>()
           .Which.Message.Should().Be(inner.Message);

        pipeline.Logged.Should().HaveCount(2);
        pipeline.Logged.Should().Contain(entry => entry.Exception == inner);
        pipeline.Logged.Should().Contain(entry => entry.Exception == undoEx);
    }

    [Fact]
    public void Empty_Task_List_Returns_Ok_Without_Error()
    {
        var pipeline = new RecordingPipeline<Args>(new List<IPipelineTask<Args>>());
        pipeline.Execute(new Args()).Should().Be(PipelineResult.Ok);
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
}
