using System;

namespace MG.Pipelines.Configuration;

/// <summary>Thrown when a pipeline configuration entry is malformed or refers to a type that cannot be resolved.</summary>
public class PipelineConfigurationException : Exception
{
    /// <summary>Initializes a new instance with the supplied message.</summary>
    public PipelineConfigurationException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with the supplied message and wrapped exception.</summary>
    public PipelineConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
