using System.Collections.Generic;

namespace MG.Pipelines;

/// <summary>Resolves named pipelines by argument type.</summary>
public interface IPipelineFactory
{
    /// <summary>All registered pipeline names whose argument type is <typeparamref name="T"/>.</summary>
    IEnumerable<string> AllPipelinesFor<T>();

    /// <summary>Creates the named pipeline, or returns <see langword="null"/> if none is registered under that name.</summary>
    IPipeline<T>? Create<T>(string name);
}
