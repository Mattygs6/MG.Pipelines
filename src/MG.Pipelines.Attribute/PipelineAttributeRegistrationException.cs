using System;

namespace MG.Pipelines.Attribute;

/// <summary>Thrown when a <see cref="PipelineAttribute"/> declaration is invalid.</summary>
public class PipelineAttributeRegistrationException : Exception
{
    /// <summary>Initializes a new instance with the supplied message.</summary>
    public PipelineAttributeRegistrationException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with the supplied message and wrapped exception.</summary>
    public PipelineAttributeRegistrationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
