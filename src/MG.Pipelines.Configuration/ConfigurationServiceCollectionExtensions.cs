using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using MG.Pipelines.Attribute;
using MG.Pipelines.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;

namespace MG.Pipelines.Configuration;

/// <summary>Registers MG.Pipelines pipelines from <see cref="IConfiguration"/>.</summary>
public static class ConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Binds the supplied <paramref name="configurationSection"/> as a JSON array of pipeline
    /// definitions and registers each entry as a keyed pipeline. Task entries may be either a bare
    /// type-name string or an object of the form <c>{ "type": "...", "config": { ... } }</c>; the
    /// <c>config</c> sub-block is bound onto the resolved task instance via
    /// <see cref="ConfigurationTaskValidator"/> at pipeline-construction time, and
    /// <see cref="System.ComponentModel.DataAnnotations"/> validation is enforced.
    /// </summary>
    /// <remarks>
    /// After initial registration this method subscribes to <paramref name="configurationSection"/>'s
    /// reload token: when the underlying source notifies of a change (e.g. a JSON file backed with
    /// <c>reloadOnChange: true</c>), each existing pipeline's task list is re-parsed and atomically
    /// swapped into the <see cref="IPipelineTaskRegistry"/>. New pipeline names introduced by reload
    /// are ignored — pipelines are fixed at startup; only their task lists are mutable.
    ///
    /// The registry is also exposed as <see cref="IPipelineTaskRegistry"/> so callers can add or
    /// remove tasks programmatically at runtime via <see cref="IPipelineTaskRegistry.SetTasks"/>.
    /// </remarks>
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
        var registry = GetOrAddRegistry(services);

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entrySection in configurationSection.GetChildren())
        {
            var parsed = ParseDefinition(entrySection);

            if (!seenNames.Add(parsed.Name))
            {
                throw new PipelineConfigurationException(
                    $"Duplicate pipeline name '{parsed.Name}' in configuration.");
            }

            RegisterDefinition(services, registry, parsed);

            if (parsed.ArgsSection is not null)
            {
                argsBinder.SetArgsConfiguration(parsed.Name, parsed.ArgsSection);
            }
        }

        ChangeToken.OnChange(
            () => configurationSection.GetReloadToken(),
            () => Reload(configurationSection, registry, argsBinder));

        return services;
    }

    private static void RegisterDefinition(
        IServiceCollection services,
        PipelineTaskRegistry registry,
        ParsedDefinition parsed)
    {
        var slotsBuilder = ImmutableArray.CreateBuilder<PipelineTaskSlot>(parsed.TaskEntries.Count);
        var taskInterfaceType = typeof(IPipelineTask<>).MakeGenericType(parsed.ArgumentType);

        for (var i = 0; i < parsed.TaskEntries.Count; i++)
        {
            var entry = parsed.TaskEntries[i];
            var taskType = TypeNameResolver.Resolve(entry.TypeName, entry.ContextLabel);

            if (!Attribute.Reflection.DescendsFromAncestorType(taskType, taskInterfaceType))
            {
                throw new PipelineConfigurationException(
                    $"Task '{taskType.FullName}' in pipeline '{parsed.Name}' must implement '{taskInterfaceType.FullName}'.");
            }

            services.TryAddTransient(taskType);
            slotsBuilder.Add(new PipelineTaskSlot(taskType, entry.ConfigSection));
        }

        var slots = slotsBuilder.MoveToImmutable();
        registry.Initialize(parsed.Name, parsed.ArgumentType, slots);

        var attributeForRegistry = new PipelineAttribute(
            parsed.Name,
            parsed.ArgumentType,
            slots.Select(s => s.TaskType).ToArray());
        Registration.Pipelines[parsed.Name] = new PipelineRegistration(parsed.PipelineType, attributeForRegistry);

        var closedPipelineInterface = typeof(IPipeline<>).MakeGenericType(parsed.ArgumentType);
        var pipelineType = parsed.PipelineType;

        services.AddKeyedTransient(closedPipelineInterface, parsed.Name, (sp, key) =>
            BuildPipeline(sp, (string)key!, pipelineType, taskInterfaceType, registry));
    }

    private static object BuildPipeline(
        IServiceProvider sp,
        string pipelineName,
        Type pipelineType,
        Type taskInterfaceType,
        IPipelineTaskRegistry registry)
    {
        var slots = registry.GetTasks(pipelineName);
        var taskListType = typeof(List<>).MakeGenericType(taskInterfaceType);
        var taskList = (IList)Activator.CreateInstance(taskListType)!;

        IPipelineTaskInstanceBinder[]? userBinders = null;

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var taskInstance = ActivatorUtilities.GetServiceOrCreateInstance(sp, slot.TaskType);

            ConfigurationTaskValidator.BindAndValidate(pipelineName, i, taskInstance, slot.ConfigSection);

            if (userBinders is null)
            {
                var enumerable = sp.GetServices<IPipelineTaskInstanceBinder>();
                userBinders = enumerable as IPipelineTaskInstanceBinder[]
                              ?? enumerable.ToArray();
            }

            for (var b = 0; b < userBinders.Length; b++)
            {
                userBinders[b].Bind(pipelineName, i, taskInstance);
            }

            taskList.Add(taskInstance);
        }

        return ActivatorUtilities.CreateInstance(sp, pipelineType, taskList);
    }

    private static void Reload(
        IConfiguration configurationSection,
        PipelineTaskRegistry registry,
        ConfigurationPipelineArgsBinder argsBinder)
    {
        foreach (var entrySection in configurationSection.GetChildren())
        {
            ParsedDefinition parsed;
            try
            {
                parsed = ParseDefinition(entrySection);
            }
            catch (PipelineConfigurationException)
            {
                // Skip malformed entries on reload — leave existing registrations intact.
                continue;
            }

            if (!registry.Contains(parsed.Name))
            {
                continue;
            }

            // Argument type is fixed at startup. If the reloaded definition disagrees, skip
            // rather than half-applying.
            if (registry.GetArgumentType(parsed.Name) != parsed.ArgumentType)
            {
                continue;
            }

            var taskInterfaceType = typeof(IPipelineTask<>).MakeGenericType(parsed.ArgumentType);
            var newSlots = new List<PipelineTaskSlot>(parsed.TaskEntries.Count);

            try
            {
                foreach (var entry in parsed.TaskEntries)
                {
                    var taskType = TypeNameResolver.Resolve(entry.TypeName, entry.ContextLabel);
                    if (!Attribute.Reflection.DescendsFromAncestorType(taskType, taskInterfaceType))
                    {
                        throw new PipelineConfigurationException(
                            $"Task '{taskType.FullName}' in pipeline '{parsed.Name}' must implement '{taskInterfaceType.FullName}'.");
                    }

                    newSlots.Add(new PipelineTaskSlot(taskType, entry.ConfigSection));
                }

                registry.SetTasks(parsed.Name, newSlots);
            }
            catch (PipelineConfigurationException)
            {
                // Reload failed validation for this pipeline — leave its current tasks intact.
                continue;
            }

            if (parsed.ArgsSection is not null)
            {
                argsBinder.SetArgsConfiguration(parsed.Name, parsed.ArgsSection);
            }
        }
    }

    private static ParsedDefinition ParseDefinition(IConfigurationSection entrySection)
    {
        var definition = entrySection.Get<PipelineDefinition>()
            ?? throw new PipelineConfigurationException("Pipeline definition entry is null.");

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new PipelineConfigurationException("Pipeline definition is missing 'name'.");
        }

        var (pipelineType, argumentType) = ResolvePipelineAndArgumentTypes(definition.Name!, definition);
        var taskEntries = ParseTaskEntries(entrySection.GetSection("tasks"), definition.Name!);

        if (taskEntries.Count == 0)
        {
            throw new PipelineConfigurationException(
                $"Pipeline '{definition.Name}' must declare at least one task.");
        }

        var argsSection = entrySection.GetSection("args");
        return new ParsedDefinition(
            definition.Name!,
            pipelineType,
            argumentType,
            taskEntries,
            argsSection.Exists() ? argsSection : null);
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

    private static (Type pipelineType, Type argumentType) ResolvePipelineAndArgumentTypes(
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

            return (pipelineType, argumentType);
        }

        if (string.IsNullOrWhiteSpace(definition.ArgumentType))
        {
            throw new PipelineConfigurationException(
                $"Pipeline '{name}' must specify either 'pipelineType' or 'argumentType' (preferably both).");
        }

        var argType = TypeNameResolver.Resolve(definition.ArgumentType!,
            $"argumentType of pipeline '{name}'");
        return (typeof(ConfigurablePipeline<>).MakeGenericType(argType), argType);
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

    private static PipelineTaskRegistry GetOrAddRegistry(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d =>
            d.ServiceType == typeof(PipelineTaskRegistry)
            && d.ImplementationInstance is PipelineTaskRegistry);
        if (existing?.ImplementationInstance is PipelineTaskRegistry current)
        {
            return current;
        }

        var registry = new PipelineTaskRegistry();
        services.AddSingleton(registry);
        services.AddSingleton<IPipelineTaskRegistry>(registry);
        return registry;
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

    private sealed class ParsedDefinition
    {
        public string Name { get; }
        public Type PipelineType { get; }
        public Type ArgumentType { get; }
        public IReadOnlyList<TaskEntry> TaskEntries { get; }
        public IConfiguration? ArgsSection { get; }

        public ParsedDefinition(
            string name,
            Type pipelineType,
            Type argumentType,
            IReadOnlyList<TaskEntry> taskEntries,
            IConfiguration? argsSection)
        {
            Name = name;
            PipelineType = pipelineType;
            ArgumentType = argumentType;
            TaskEntries = taskEntries;
            ArgsSection = argsSection;
        }
    }
}
