using System;

namespace MG.Pipelines;

/// <summary>Thrown when a pipeline wraps an unhandled task or undo exception.</summary>
public class PipelineException : Exception
{
    /// <summary>Initializes a new instance with the supplied message.</summary>
    public PipelineException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with the supplied message and wrapped exception.</summary>
    public PipelineException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
