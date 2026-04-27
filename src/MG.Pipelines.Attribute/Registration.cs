using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MG.Pipelines.Attribute;

/// <summary>Scans assemblies for <see cref="PipelineAttribute"/> declarations and caches them by name.</summary>
public static class Registration
{
    /// <summary>All registered pipelines, keyed by <see cref="PipelineAttribute.Name"/>.</summary>
    public static readonly ConcurrentDictionary<string, PipelineRegistration> Pipelines =
        new(StringComparer.Ordinal);

    /// <summary>Scans loaded assemblies (via <see cref="TypeLocator"/>) and records every valid <see cref="PipelineAttribute"/>.</summary>
    /// <exception cref="PipelineAttributeRegistrationException">The attribute declares zero tasks, or a declared task type does not implement <see cref="IPipelineTask{T}"/> for the attribute's argument type.</exception>
    public static void RegisterPipelines() => RegisterPipelines(TypeLocator.LocateTypes(typeof(IPipeline<>)));

    /// <summary>Records every valid <see cref="PipelineAttribute"/> on the supplied types. Useful when the caller has an explicit assembly list.</summary>
    /// <exception cref="PipelineAttributeRegistrationException">The attribute declares zero tasks, or a declared task type does not implement <see cref="IPipelineTask{T}"/> for the attribute's argument type.</exception>
    public static void RegisterPipelines(IEnumerable<Type> types)
    {
        if (types is null)
        {
            throw new ArgumentNullException(nameof(types));
        }

        foreach (var type in types)
        {
            foreach (var attribute in type.GetCustomAttributes<PipelineAttribute>(inherit: false))
            {
                if (attribute.PipelineTasks.Length == 0)
                {
                    throw new PipelineAttributeRegistrationException(
                        $"Pipeline '{attribute.Name}' on '{type.FullName}' must declare at least one task.");
                }

                foreach (var taskType in attribute.PipelineTasks)
                {
                    if (!Reflection.DescendsFromAncestorType(taskType, attribute.TaskType))
                    {
                        throw new PipelineAttributeRegistrationException(
                            $"Task '{taskType.FullName}' in pipeline '{attribute.Name}' must implement '{attribute.TaskType.FullName}'.");
                    }
                }

                Pipelines.TryAdd(attribute.Name, new PipelineRegistration(type, attribute));
            }
        }
    }

    /// <summary>Clears all registrations. Primarily for tests.</summary>
    public static void Clear() => Pipelines.Clear();
}
