using System;
using System.Collections.Generic;
using System.Linq;

namespace MG.Pipelines.Attribute;

/// <summary>
/// An <see cref="IPipelineFactory"/> backed by the static <see cref="Registration.Pipelines"/> map.
/// Tasks are instantiated via cached compiled-expression activators.
/// </summary>
public class PipelineFactory : IPipelineFactory
{
    /// <summary>A default process-wide instance that uses the default <see cref="PipelineNameResolver"/>.</summary>
    public static readonly IPipelineFactory Instance = new PipelineFactory();

    private readonly IPipelineNameResolver nameResolver;

    /// <summary>Creates a factory with the default <see cref="PipelineNameResolver"/>.</summary>
    public PipelineFactory() : this(new PipelineNameResolver())
    {
    }

    /// <summary>Creates a factory with a custom <see cref="IPipelineNameResolver"/>.</summary>
    public PipelineFactory(IPipelineNameResolver nameResolver)
    {
        this.nameResolver = nameResolver ?? throw new ArgumentNullException(nameof(nameResolver));
    }

    /// <inheritdoc/>
    public IEnumerable<string> AllPipelinesFor<T>() =>
        Registration.Pipelines
            .Where(kvp => kvp.Value.Attribute.ArgumentType == typeof(T))
            .Select(kvp => kvp.Key);

    /// <inheritdoc/>
    public IPipeline<T>? Create<T>(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        foreach (var candidate in nameResolver.ResolveNames(name))
        {
            if (Registration.Pipelines.TryGetValue(candidate, out var registration))
            {
                return Build<T>(registration);
            }
        }

        return null;
    }

    private static IPipeline<T> Build<T>(PipelineRegistration registration)
    {
        var tasks = new List<IPipelineTask<T>>(registration.Attribute.PipelineTasks.Length);
        foreach (var taskType in registration.Attribute.PipelineTasks)
        {
            var taskActivator = Reflection.GetActivator<IPipelineTask<T>>(taskType)
                ?? throw new PipelineAttributeRegistrationException(
                    $"Task '{taskType.FullName}' has no parameterless constructor.");
            tasks.Add(taskActivator());
        }

        var pipelineActivator = Reflection.GetActivator<IPipeline<T>>(
                registration.PipelineType,
                typeof(IList<IPipelineTask<T>>))
            ?? throw new PipelineAttributeRegistrationException(
                $"Pipeline '{registration.PipelineType.FullName}' has no constructor accepting IList<IPipelineTask<{typeof(T).Name}>>.");

        return pipelineActivator(tasks);
    }
}
