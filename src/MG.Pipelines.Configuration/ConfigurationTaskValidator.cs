using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Configuration;

namespace MG.Pipelines.Configuration;

/// <summary>
/// Binds a config section onto a freshly resolved task instance and verifies declarative validation:
/// <list type="bullet">
///   <item><c>System.ComponentModel.DataAnnotations</c> attributes (e.g. <c>[Required]</c>) checked via <c>Validator.TryValidateObject</c>.</item>
///   <item>Properties carrying <see cref="RequiredMemberAttribute"/> (the C# <c>required</c> keyword) — required to
///     have a corresponding key in the supplied config section, since the keyword has no runtime enforcement
///     when the instance is produced via reflection-based construction.</item>
/// </list>
/// Failures throw <see cref="PipelineConfigurationException"/> at pipeline construction time
/// (i.e. when <c>factory.Create&lt;T&gt;(name)</c> is called).
/// </summary>
internal static class ConfigurationTaskValidator
{
    /// <summary>Binds <paramref name="section"/> (if non-null) onto <paramref name="taskInstance"/>, then validates.</summary>
    public static void BindAndValidate(
        string pipelineName,
        int taskIndex,
        object taskInstance,
        IConfiguration? section)
    {
        if (taskInstance is null)
        {
            return;
        }

        section?.Bind(taskInstance);

        var validationContext = new ValidationContext(taskInstance);
        var dataAnnotationResults = new List<ValidationResult>();
        var dataAnnotationsOk = Validator.TryValidateObject(
            taskInstance, validationContext, dataAnnotationResults, validateAllProperties: true);

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
    /// Verifies that every property carrying <see cref="RequiredMemberAttribute"/> has a corresponding key in
    /// the supplied config section. Mirrors the <c>required</c> keyword's compile-time enforcement at runtime.
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
}
