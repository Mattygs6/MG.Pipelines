using System;

namespace MG.Pipelines.Attribute;

/// <summary>Declares a named pipeline by associating an argument type and an ordered list of task types with a <see cref="Pipeline{T}"/>.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PipelineAttribute : System.Attribute
{
    /// <summary>The registration name (unique across the application).</summary>
    public string Name { get; }

    /// <summary>The tasks to instantiate and inject into the pipeline, in order.</summary>
    public Type[] PipelineTasks { get; }

    /// <summary>The pipeline argument type (the <c>T</c> in <see cref="Pipeline{T}"/>).</summary>
    public Type ArgumentType { get; }

    /// <summary>The resulting <see cref="IPipelineTask{T}"/> closed type, derived from <see cref="ArgumentType"/>.</summary>
    public Type TaskType { get; }

    /// <summary>Initializes a new <see cref="PipelineAttribute"/>.</summary>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public PipelineAttribute(string name, Type argumentType, params Type[] pipelineTasks)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ArgumentType = argumentType ?? throw new ArgumentNullException(nameof(argumentType));
        PipelineTasks = pipelineTasks ?? throw new ArgumentNullException(nameof(pipelineTasks));
        TaskType = typeof(IPipelineTask<>).MakeGenericType(argumentType);
    }
}
