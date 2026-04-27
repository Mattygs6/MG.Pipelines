namespace MG.Pipelines.DependencyInjection;

/// <summary>
/// Applies registered defaults onto an args instance produced by
/// <see cref="IPipelineFactory.CreateArgs{T}(string)"/>. Implementations are typically supplied by the
/// configuration package (binding from <c>IConfiguration</c>) but the contract is open for any source.
/// </summary>
public interface IPipelineArgsBinder
{
    /// <summary>
    /// Mutates <paramref name="instance"/> in place with any defaults registered for
    /// <paramref name="pipelineName"/>. Implementations must be a no-op when nothing is registered for the name.
    /// </summary>
    void Bind<T>(string pipelineName, T instance);
}
