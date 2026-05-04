using System;

using Microsoft.Extensions.Configuration;

namespace MG.Pipelines.Configuration;

/// <summary>
/// One ordered task within a config-registered pipeline. <see cref="ConfigSection"/> (when non-null)
/// is bound onto the freshly resolved task instance during pipeline construction; <c>[Required]</c>
/// data-annotation and <c>required</c>-keyword properties are validated at the same time.
/// </summary>
public sealed class PipelineTaskSlot
{
    /// <summary>The concrete task type. Must implement <see cref="IPipelineTask{T}"/> for the owning pipeline's argument type.</summary>
    public Type TaskType { get; }

    /// <summary>The configuration section bound onto this task instance during construction, or <see langword="null"/>.</summary>
    public IConfiguration? ConfigSection { get; }

    /// <summary>Creates a new task slot.</summary>
    public PipelineTaskSlot(Type taskType, IConfiguration? configSection = null)
    {
        TaskType = taskType ?? throw new ArgumentNullException(nameof(taskType));
        ConfigSection = configSection;
    }
}
