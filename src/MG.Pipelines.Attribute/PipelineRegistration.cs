using System;

namespace MG.Pipelines.Attribute;

/// <summary>A resolved pipeline registration: the pipeline <see cref="Type"/> and its source <see cref="PipelineAttribute"/>.</summary>
public class PipelineRegistration
{
    /// <summary>The concrete pipeline type (derives from <see cref="Pipeline{T}"/>).</summary>
    public Type PipelineType { get; }

    /// <summary>The attribute that declared this registration.</summary>
    public PipelineAttribute Attribute { get; }

    /// <summary>Creates a new <see cref="PipelineRegistration"/>.</summary>
    public PipelineRegistration(Type pipelineType, PipelineAttribute attribute)
    {
        PipelineType = pipelineType ?? throw new ArgumentNullException(nameof(pipelineType));
        Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
    }
}
