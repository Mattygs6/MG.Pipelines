using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MG.Pipelines.Configuration;

/// <inheritdoc />
public sealed class PipelineTaskRegistry : IPipelineTaskRegistry
{
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyCollection<string> PipelineNames => entries.Keys.ToArray();

    /// <inheritdoc />
    public bool Contains(string pipelineName) =>
        !string.IsNullOrWhiteSpace(pipelineName) && entries.ContainsKey(pipelineName);

    /// <inheritdoc />
    public Type GetArgumentType(string pipelineName) => GetEntry(pipelineName).ArgumentType;

    /// <inheritdoc />
    public IReadOnlyList<PipelineTaskSlot> GetTasks(string pipelineName) => GetEntry(pipelineName).Tasks;

    /// <inheritdoc />
    public void SetTasks(string pipelineName, IEnumerable<PipelineTaskSlot> tasks)
    {
        if (tasks is null)
        {
            throw new ArgumentNullException(nameof(tasks));
        }

        var existing = GetEntry(pipelineName);
        var newTasks = tasks.ToImmutableArray();

        if (newTasks.IsEmpty)
        {
            throw new PipelineConfigurationException(
                $"Pipeline '{pipelineName}' must declare at least one task.");
        }

        var taskInterface = typeof(IPipelineTask<>).MakeGenericType(existing.ArgumentType);
        for (var i = 0; i < newTasks.Length; i++)
        {
            var slot = newTasks[i];
            if (slot is null)
            {
                throw new PipelineConfigurationException(
                    $"Task[{i}] of pipeline '{pipelineName}' is null.");
            }

            if (!Attribute.Reflection.DescendsFromAncestorType(slot.TaskType, taskInterface))
            {
                throw new PipelineConfigurationException(
                    $"Task '{slot.TaskType.FullName}' in pipeline '{pipelineName}' must implement '{taskInterface.FullName}'.");
            }
        }

        entries[pipelineName] = new Entry(existing.ArgumentType, newTasks);
    }

    /// <summary>
    /// Records the pipeline's argument type and initial task list. Called once per pipeline by
    /// <see cref="ConfigurationServiceCollectionExtensions.AddPipelinesFromConfiguration"/>.
    /// </summary>
    internal void Initialize(string pipelineName, Type argumentType, ImmutableArray<PipelineTaskSlot> tasks)
    {
        if (string.IsNullOrWhiteSpace(pipelineName))
        {
            throw new ArgumentException("Pipeline name must not be empty.", nameof(pipelineName));
        }

        entries[pipelineName] = new Entry(
            argumentType ?? throw new ArgumentNullException(nameof(argumentType)),
            tasks);
    }

    private Entry GetEntry(string pipelineName)
    {
        if (string.IsNullOrWhiteSpace(pipelineName))
        {
            throw new ArgumentException("Pipeline name must not be empty.", nameof(pipelineName));
        }

        if (!entries.TryGetValue(pipelineName, out var entry))
        {
            throw new ArgumentException(
                $"Pipeline '{pipelineName}' is not registered.", nameof(pipelineName));
        }

        return entry;
    }

    private sealed class Entry
    {
        public Type ArgumentType { get; }
        public ImmutableArray<PipelineTaskSlot> Tasks { get; }

        public Entry(Type argumentType, ImmutableArray<PipelineTaskSlot> tasks)
        {
            ArgumentType = argumentType;
            Tasks = tasks;
        }
    }
}
