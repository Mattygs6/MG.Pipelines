using System;
using System.Collections.Generic;
using System.Linq;

using MG.Pipelines.Attribute;
using MG.Pipelines.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MG.Pipelines.Configuration;

/// <summary>Registers MG.Pipelines pipelines from <see cref="IConfiguration"/>.</summary>
public static class ConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Binds the supplied <paramref name="configurationSection"/> as <c>List&lt;<see cref="PipelineDefinition"/>&gt;</c>
    /// and registers each entry as a keyed pipeline. If <see cref="PipelineDefinition.Name"/> matches an existing
    /// attribute-based registration, the configuration entry overrides it (the most recent keyed registration wins
    /// inside MS DI, and <see cref="Registration.Pipelines"/> is updated via add-or-replace).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationSection">A configuration section bound as a JSON array of pipeline definitions.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PipelineConfigurationException">A definition is malformed or a referenced type cannot be resolved.</exception>
    public static IServiceCollection AddPipelinesFromConfiguration(
        this IServiceCollection services,
        IConfiguration configurationSection)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configurationSection is null)
        {
            throw new ArgumentNullException(nameof(configurationSection));
        }

        services.TryAddSingleton<IPipelineNameResolver, PipelineNameResolver>();
        services.TryAddTransient<IPipelineFactory, ServiceProviderPipelineFactory>();

        var argsBinder = GetOrAddArgsBinder(services);

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entrySection in configurationSection.GetChildren())
        {
            var definition = entrySection.Get<PipelineDefinition>();
            if (definition is null)
            {
                throw new PipelineConfigurationException("Pipeline definition entry is null.");
            }

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                throw new PipelineConfigurationException("Pipeline definition is missing 'name'.");
            }

            if (!seenNames.Add(definition.Name!))
            {
                throw new PipelineConfigurationException(
                    $"Duplicate pipeline name '{definition.Name}' in configuration.");
            }

            RegisterDefinition(services, definition.Name!, definition);

            var argsSection = entrySection.GetSection("args");
            if (argsSection.Exists())
            {
                argsBinder.SetArgsConfiguration(definition.Name!, argsSection);
            }
        }

        return services;
    }

    private static ConfigurationPipelineArgsBinder GetOrAddArgsBinder(IServiceCollection services)
    {
        // Reuse a single mutable binder across multiple AddPipelinesFromConfiguration calls so that
        // entries from layered config sources accumulate rather than overwrite.
        var existing = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ConfigurationPipelineArgsBinder)
            && d.ImplementationInstance is ConfigurationPipelineArgsBinder);
        if (existing?.ImplementationInstance is ConfigurationPipelineArgsBinder current)
        {
            return current;
        }

        var binder = new ConfigurationPipelineArgsBinder();
        services.AddSingleton(binder);
        services.AddSingleton<IPipelineArgsBinder>(binder);
        return binder;
    }

    private static void RegisterDefinition(IServiceCollection services, string name, PipelineDefinition definition)
    {
        if (definition.Tasks is null || definition.Tasks.Count == 0)
        {
            throw new PipelineConfigurationException($"Pipeline '{name}' must declare at least one task.");
        }

        var (pipelineType, argumentType, isConfigurable) = ResolvePipelineAndArgumentTypes(name, definition);
        var taskInterfaceType = typeof(IPipelineTask<>).MakeGenericType(argumentType);

        var taskTypes = new Type[definition.Tasks.Count];
        for (var i = 0; i < definition.Tasks.Count; i++)
        {
            var taskType = TypeNameResolver.Resolve(definition.Tasks[i],
                $"task[{i}] of pipeline '{name}'");

            if (!Attribute.Reflection.DescendsFromAncestorType(taskType, taskInterfaceType))
            {
                throw new PipelineConfigurationException(
                    $"Task '{taskType.FullName}' in pipeline '{name}' must implement '{taskInterfaceType.FullName}'.");
            }

            taskTypes[i] = taskType;
            services.TryAddTransient(taskType);
        }

        if (isConfigurable)
        {
            services.TryAddTransient(pipelineType);
        }

        var attributeForRegistry = new PipelineAttribute(name, argumentType, taskTypes);
        Registration.Pipelines[name] = new PipelineRegistration(pipelineType, attributeForRegistry);

        var closedPipelineInterface = typeof(IPipeline<>).MakeGenericType(argumentType);

        services.AddKeyedTransient(closedPipelineInterface, name, (sp, _) =>
            PipelineBuilder.Build(sp, pipelineType, taskInterfaceType, taskTypes));
    }

    private static (Type pipelineType, Type argumentType, bool isConfigurable) ResolvePipelineAndArgumentTypes(
        string name,
        PipelineDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.PipelineType))
        {
            var pipelineType = TypeNameResolver.Resolve(definition.PipelineType!,
                $"pipelineType of pipeline '{name}'");

            var argumentType = ExtractArgumentTypeFromPipelineBase(pipelineType)
                ?? (string.IsNullOrWhiteSpace(definition.ArgumentType)
                    ? throw new PipelineConfigurationException(
                        $"Could not infer the argument type for pipeline '{name}' from '{pipelineType.FullName}'. " +
                        "Specify 'argumentType' explicitly.")
                    : TypeNameResolver.Resolve(definition.ArgumentType!,
                        $"argumentType of pipeline '{name}'"));

            return (pipelineType, argumentType, isConfigurable: false);
        }

        if (string.IsNullOrWhiteSpace(definition.ArgumentType))
        {
            throw new PipelineConfigurationException(
                $"Pipeline '{name}' must specify either 'pipelineType' or 'argumentType' (preferably both).");
        }

        var argType = TypeNameResolver.Resolve(definition.ArgumentType!,
            $"argumentType of pipeline '{name}'");
        var configurablePipelineType = typeof(ConfigurablePipeline<>).MakeGenericType(argType);

        return (configurablePipelineType, argType, isConfigurable: true);
    }

    private static Type? ExtractArgumentTypeFromPipelineBase(Type pipelineType)
    {
        var current = pipelineType;
        while (current is not null && current != typeof(object))
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Pipeline<>))
            {
                return current.GetGenericArguments()[0];
            }

            current = current.BaseType;
        }

        return null;
    }
}
