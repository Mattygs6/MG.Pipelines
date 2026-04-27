using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using MG.Pipelines.Attribute;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MG.Pipelines.DependencyInjection;

/// <summary>Registers MG.Pipelines services with an <see cref="IServiceCollection"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Scans <paramref name="assemblies"/> (or the calling assembly if none supplied) for types declaring
    /// <see cref="PipelineAttribute"/>. Each declared task is registered as transient, and each pipeline
    /// is registered as a keyed transient under its attribute name. Also registers the default
    /// <see cref="IPipelineNameResolver"/> and an <see cref="IPipelineFactory"/>.
    /// </summary>
    /// <remarks>Existing registrations are respected — the caller may pre-register
    /// <see cref="IPipelineNameResolver"/> with a custom implementation and <see cref="AddPipelines"/>
    /// will not overwrite it.</remarks>
    public static IServiceCollection AddPipelines(this IServiceCollection services, params Assembly[] assemblies)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (assemblies is null || assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }

        services.TryAddSingleton<IPipelineNameResolver, PipelineNameResolver>();
        services.TryAddTransient<IPipelineFactory, ServiceProviderPipelineFactory>();

        foreach (var type in assemblies.SelectMany(SafeGetTypes).Distinct())
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            var attributes = type.GetCustomAttributes<PipelineAttribute>(inherit: false).ToArray();
            if (attributes.Length == 0)
            {
                continue;
            }

            if (!Reflection.DescendsFromAncestorType(type, typeof(IPipeline<>)))
            {
                throw new PipelineAttributeRegistrationException(
                    $"Type '{type.FullName}' declares [Pipeline] but does not implement IPipeline<T>.");
            }

            foreach (var attribute in attributes)
            {
                Validate(type, attribute);
                Registration.Pipelines.TryAdd(attribute.Name, new PipelineRegistration(type, attribute));

                var closedPipelineInterface = typeof(IPipeline<>).MakeGenericType(attribute.ArgumentType);

                foreach (var taskType in attribute.PipelineTasks)
                {
                    services.TryAddTransient(taskType);
                }

                services.AddKeyedTransient(closedPipelineInterface, attribute.Name, (sp, _) =>
                    PipelineBuilder.Build(sp, type, attribute.TaskType, attribute.PipelineTasks));
            }
        }

        return services;
    }

    private static void Validate(Type type, PipelineAttribute attribute)
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
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
