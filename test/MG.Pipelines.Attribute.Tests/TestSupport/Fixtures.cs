using System;
using System.Collections.Generic;

using Xunit;

namespace MG.Pipelines.Attribute.Tests.TestSupport;

/// <summary>
/// Forces serial execution of every test that mutates the static <see cref="Registration.Pipelines"/> map.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class RegistrationCollection : ICollectionFixture<RegistrationCollection>
{
    public const string Name = "Registration";
}

public sealed class ArgsA { public List<string> Log { get; } = new(); }
public sealed class ArgsB { public List<string> Log { get; } = new(); }

public sealed class TaskA1 : IPipelineTask<ArgsA>
{
    public PipelineResult Execute(ArgsA args) { args.Log.Add("A1"); return PipelineResult.Ok; }
}

public sealed class TaskA2 : IPipelineTask<ArgsA>
{
    public PipelineResult Execute(ArgsA args) { args.Log.Add("A2"); return PipelineResult.Ok; }
}

public sealed class TaskB1 : IPipelineTask<ArgsB>
{
    public PipelineResult Execute(ArgsB args) { args.Log.Add("B1"); return PipelineResult.Ok; }
}

public sealed class MismatchTask : IPipelineTask<ArgsB>
{
    public PipelineResult Execute(ArgsB args) => PipelineResult.Ok;
}

[Pipeline("pipeline-a", typeof(ArgsA), typeof(TaskA1), typeof(TaskA2))]
public sealed class PipelineA : Pipeline<ArgsA>
{
    public PipelineA(IList<IPipelineTask<ArgsA>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}

[Pipeline("pipeline-b", typeof(ArgsB), typeof(TaskB1))]
public sealed class PipelineB : Pipeline<ArgsB>
{
    public PipelineB(IList<IPipelineTask<ArgsB>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}

[Pipeline("empty-tasks", typeof(ArgsA))]
public sealed class PipelineWithNoTasks : Pipeline<ArgsA>
{
    public PipelineWithNoTasks(IList<IPipelineTask<ArgsA>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}

[Pipeline("mismatched-task", typeof(ArgsA), typeof(MismatchTask))]
public sealed class PipelineWithMismatchedTask : Pipeline<ArgsA>
{
    public PipelineWithMismatchedTask(IList<IPipelineTask<ArgsA>> tasks) : base(tasks) { }
    protected override void Log(Exception caughtException, string message) { }
}
