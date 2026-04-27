using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using MG.Pipelines.DependencyInjection;

using Microsoft.Extensions.Configuration;

namespace MG.Pipelines.Configuration;

/// <summary>
/// An <see cref="IPipelineTaskInstanceBinder"/> backed by a per-(pipeline-name, task-index) lookup
/// of <see cref="IConfiguration"/> sub-sections. After binding, the task instance is validated:
/// <list type="bullet">
///   <item><see cref="System.ComponentModel.DataAnnotations"/> attributes (e.g. <c>[Required]</c>) are checked via <c>Validator.TryValidateObject</c>.</item>
///   <item>Properties carrying <see cref="RequiredMemberAttribute"/> (the C# <c>required</c> keyword) are required to have a corresponding key in the supplied config section, since the keyword has no runtime enforcement.</item>
/// </list>
/// Failures throw <see cref="PipelineConfigurationException"/> at pipeline construction time
/// (i.e. when <c>factory.Create&lt;T&gt;(name)</c> is called).
/// </summary>
public sealed class ConfigurationPipelineTaskBinder : IPipelineTaskInstanceBinder
{
    private readonly ConcurrentDictionary<TaskKey, IConfiguration?> taskConfigs = new();

    /// <summary>
    /// Records that the task at (<paramref name="pipelineName"/>, <paramref name="taskIndex"/>)
    /// participates in configuration-driven binding. Pass <see langword="null"/> for
    /// <paramref name="configSection"/> to enable validation without binding any properties
    /// (the task may still satisfy <c>[Required]</c> through ctor injection or property defaults).
    /// </summary>
    public void RegisterTask(string pipelineName, int taskIndex, IConfiguration? configSection)
    {
        if (string.IsNullOrWhiteSpace(pipelineName))
        {
            throw new ArgumentException("Pipeline name must not be empty.", nameof(pipelineName));
        }

        taskConfigs[new TaskKey(pipelineName, taskIndex)] = configSection;
    }

    /// <inheritdoc/>
    public void Bind(string pipelineName, int taskIndex, object taskInstance)
    {
        if (taskInstance is null)
        {
            return;
        }

        var key = new TaskKey(pipelineName, taskIndex);
        if (!taskConfigs.TryGetValue(key, out var section))
        {
            return; // not a config-registered task — leave it alone
        }

        section?.Bind(taskInstance);

        ValidateInstance(pipelineName, taskIndex, taskInstance, section);
    }

    private static void ValidateInstance(string pipelineName, int taskIndex, object taskInstance, IConfiguration? section)
    {
        var ctx = new ValidationContext(taskInstance);
        var dataAnnotationResults = new List<ValidationResult>();
        var dataAnnotationsOk = Validator.TryValidateObject(
            taskInstance, ctx, dataAnnotationResults, validateAllProperties: true);

        var requiredMemberFailures = ValidateRequiredMembers(taskInstance.GetType(), section);

        if (dataAnnotationsOk && requiredMemberFailures.Count == 0)
        {
            return;
        }

        var allMessages = dataAnnotationResults
            .Select(r => r.ErrorMessage)
            .Concat(requiredMemberFailures)
            .Where(m => !string.IsNullOrEmpty(m));

        var problems = string.Join("; ", allMessages);
        throw new PipelineConfigurationException(
            $"Task '{taskInstance.GetType().FullName}' at index {taskIndex} of pipeline '{pipelineName}' " +
            $"failed configuration validation: {problems}");
    }

    /// <summary>
    /// Verifies that every property carrying <see cref="RequiredMemberAttribute"/> (the C# <c>required</c>
    /// keyword) has a corresponding key in the supplied config section. The keyword's own enforcement is
    /// compile-time only and is bypassed by reflection-based instantiation, so we mirror it here.
    /// </summary>
    private static List<string> ValidateRequiredMembers(Type taskType, IConfiguration? section)
    {
        var failures = new List<string>();

        var requiredProps = taskType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(RequiredMemberAttribute), inherit: true))
            .ToArray();

        if (requiredProps.Length == 0)
        {
            return failures;
        }

        var suppliedKeys = section is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                section.GetChildren().Select(c => c.Key),
                StringComparer.OrdinalIgnoreCase);

        foreach (var prop in requiredProps)
        {
            if (!suppliedKeys.Contains(prop.Name))
            {
                failures.Add($"The {prop.Name} field is required (marked with the C# 'required' keyword).");
            }
        }

        return failures;
    }

    private readonly struct TaskKey : IEquatable<TaskKey>
    {
        public readonly string PipelineName;
        public readonly int TaskIndex;

        public TaskKey(string pipelineName, int taskIndex)
        {
            PipelineName = pipelineName;
            TaskIndex = taskIndex;
        }

        public bool Equals(TaskKey other) =>
            string.Equals(PipelineName, other.PipelineName, StringComparison.Ordinal)
            && TaskIndex == other.TaskIndex;

        public override bool Equals(object? obj) => obj is TaskKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                return (PipelineName.GetHashCode() * 397) ^ TaskIndex;
            }
        }
    }
}
