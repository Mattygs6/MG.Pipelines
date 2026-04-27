using System.Collections.Generic;

namespace MG.Pipelines;

/// <summary>Resolves named pipelines by argument type.</summary>
public interface IPipelineFactory
{
    /// <summary>All registered pipeline names whose argument type is <typeparamref name="T"/>.</summary>
    IEnumerable<string> AllPipelinesFor<T>();

    /// <summary>Creates the named pipeline, or returns <see langword="null"/> if none is registered under that name.</summary>
    IPipeline<T>? Create<T>(string name);

    /// <summary>
    /// Creates a fresh argument instance for the named pipeline, with any registered defaults
    /// (e.g. configuration-bound properties) applied. The returned instance is intended to be
    /// further mutated by the caller before being passed to <see cref="IPipeline{T}.Execute"/>.
    /// </summary>
    /// <typeparam name="T">The pipeline argument type.</typeparam>
    /// <param name="name">The pipeline name. Implementations may ignore this when no per-name defaults exist.</param>
    /// <returns>A new <typeparamref name="T"/> instance.</returns>
    T CreateArgs<T>(string name);
}
