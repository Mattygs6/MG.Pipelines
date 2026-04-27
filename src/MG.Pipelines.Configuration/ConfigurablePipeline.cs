using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace MG.Pipelines.Configuration;

/// <summary>
/// A default <see cref="Pipeline{T}"/> implementation used when configuration declares a pipeline name
/// without specifying a concrete <see cref="Pipeline{T}"/> subclass. Logs unhandled exceptions through
/// <see cref="ILogger{TCategoryName}"/>.
/// </summary>
/// <typeparam name="T">The pipeline argument type.</typeparam>
public class ConfigurablePipeline<T> : Pipeline<T>
{
    private readonly ILogger<ConfigurablePipeline<T>> logger;

    /// <summary>Creates a new <see cref="ConfigurablePipeline{T}"/>.</summary>
    public ConfigurablePipeline(IList<IPipelineTask<T>> tasks, ILogger<ConfigurablePipeline<T>> logger)
        : base(tasks)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override void Log(Exception caughtException, string message) =>
        logger.LogError(caughtException, "{Message}", message);
}
