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
    /// Binds the supplied <paramref name="configurationSection"/> as a JSON array of pipeline
    /// definitions and registers each entry as a keyed pipeline. Task entries may be either a bare
    /// type-name string or an object of the form <c>{ "type": "...", "config": { ... } }</c>; the
    /// <c>config</c> sub-block is bound onto the resolved task instance via
    /// <see cref="ConfigurationPipelineTaskBinder"/> at pipeline-construction time, and
    /// <see cref="System.ComponentModel.DataAnnotations"/> validation is enforced.
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
        var taskBinder = GetOrAddTaskBinder(services);

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

            var taskEntries = ParseTaskEntries(entrySection.GetSection("tasks"), definition.Name!);
            RegisterDefinition(services, definition.Name!, definition, taskEntries, taskBinder);

            var argsSection = entrySection.GetSection("args");
            if (argsSection.Exists())
            {
                argsBinder.SetArgsConfiguration(definition.Name!, argsSection);
            }
        }

        return services;
    }

    private static List<TaskEntry> ParseTaskEntries(IConfigurationSection tasksSection, string pipelineName)
    {
        var entries = new List<TaskEntry>();

        foreach (var taskChild in tasksSection.GetChildren())
        {
            var contextLabel = $"task[{entries.Count}] of pipeline '{pipelineName}'";

            // Leaf string form: "tasks": [ "MyApp.Foo, MyApp", ... ]
            if (taskChild.Value is not null)
            {
                entries.Add(new TaskEntry(taskChild.Value, configSection: null, contextLabel));
                continue;
            }

            // Object form: "tasks": [ { "type": "MyApp.Foo, MyApp", "config": { ... } } ]
            var typeName = taskChild["type"];
            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new PipelineConfigurationException(
                    $"{contextLabel} is an object but is missing the 'type' property.");
            }

            var configSection = taskChild.GetSection("config");
            entries.Add(new TaskEntry(
                typeName!,
                configSection.Exists() ? configSection : null,
                contextLabel));
        }

        return entries;
    }

    private static void RegisterDefinition(
        IServiceCollection services,
        string name,
        PipelineDefinition definition,
        List<TaskEntry> taskEntries,
        ConfigurationPipelineTaskBinder taskBinder)
    {
        if (taskEntries.Count == 0)
        {
            throw new PipelineConfigurationException($"Pipeline '{name}' must declare at least one task.");
        }

        var (pipelineType, argumentType, _) = ResolvePipelineAndArgumentTypes(name, definition);
        var taskInterfaceType = typeof(IPipelineTask<>).MakeGenericType(argumentType);

        var taskTypes = new Type[taskEntries.Count];
        for (var i = 0; i < taskEntries.Count; i++)
        {
            var entry = taskEntries[i];
            var taskType = TypeNameResolver.Resolve(entry.TypeName, entry.ContextLabel);

            if (!Attribute.Reflection.DescendsFromAncestorType(taskType, taskInterfaceType))
            {
                throw new PipelineConfigurationException(
                    $"Task '{taskType.FullName}' in pipeline '{name}' must implement '{taskInterfaceType.FullName}'.");
            }

            taskTypes[i] = taskType;
            services.TryAddTransient(taskType);

            // Every task in a config-registered pipeline participates in the binder so that
            // [Required] validation runs even when no `config` block is supplied.
            taskBinder.RegisterTask(name, i, entry.ConfigSection);
        }

        var attributeForRegistry = new PipelineAttribute(name, argumentType, taskTypes);
        Registration.Pipelines[name] = new PipelineRegistration(pipelineType, attributeForRegistry);

        var closedPipelineInterface = typeof(IPipeline<>).MakeGenericType(argumentType);

        services.AddKeyedTransient(closedPipelineInterface, name, (sp, key) =>
            PipelineBuilder.Build(sp, (string)key!, pipelineType, taskInterfaceType, taskTypes));
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

    private static ConfigurationPipelineArgsBinder GetOrAddArgsBinder(IServiceCollection services)
    {
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

    private static ConfigurationPipelineTaskBinder GetOrAddTaskBinder(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ConfigurationPipelineTaskBinder)
            && d.ImplementationInstance is ConfigurationPipelineTaskBinder);
        if (existing?.ImplementationInstance is ConfigurationPipelineTaskBinder current)
        {
            return current;
        }

        var binder = new ConfigurationPipelineTaskBinder();
        services.AddSingleton(binder);
        services.AddSingleton<IPipelineTaskInstanceBinder>(binder);
        return binder;
    }

    private readonly struct TaskEntry
    {
        public readonly string TypeName;
        public readonly IConfiguration? ConfigSection;
        public readonly string ContextLabel;

        public TaskEntry(string typeName, IConfiguration? configSection, string contextLabel)
        {
            TypeName = typeName;
            ConfigSection = configSection;
            ContextLabel = contextLabel;
        }
    }
}
