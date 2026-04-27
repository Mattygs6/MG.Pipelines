using System;
using System.Collections.Concurrent;

using MG.Pipelines.DependencyInjection;

using Microsoft.Extensions.Configuration;

namespace MG.Pipelines.Configuration;

/// <summary>
/// An <see cref="IPipelineArgsBinder"/> backed by a per-pipeline-name lookup of
/// <see cref="IConfiguration"/> sub-sections. Sections are bound onto the args instance with
/// <see cref="ConfigurationBinder.Bind(IConfiguration, object?)"/>.
/// </summary>
public sealed class ConfigurationPipelineArgsBinder : IPipelineArgsBinder
{
    private readonly ConcurrentDictionary<string, IConfiguration> argsByName =
        new(StringComparer.Ordinal);

    /// <summary>Registers (or replaces) the args configuration for <paramref name="pipelineName"/>.</summary>
    public void SetArgsConfiguration(string pipelineName, IConfiguration argsSection)
    {
        if (string.IsNullOrWhiteSpace(pipelineName))
        {
            throw new ArgumentException("Pipeline name must not be empty.", nameof(pipelineName));
        }

        argsByName[pipelineName] = argsSection ?? throw new ArgumentNullException(nameof(argsSection));
    }

    /// <summary>Returns <see langword="true"/> if a section is registered for <paramref name="pipelineName"/>.</summary>
    public bool HasArgsConfiguration(string pipelineName) => argsByName.ContainsKey(pipelineName);

    /// <inheritdoc/>
    public void Bind<T>(string pipelineName, T instance)
    {
        if (instance is null)
        {
            return;
        }

        if (argsByName.TryGetValue(pipelineName, out var section))
        {
            section.Bind(instance);
        }
    }
}
