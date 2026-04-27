using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MG.Pipelines.Configuration;

/// <summary>Resolves a configuration string into a <see cref="Type"/>.</summary>
internal static class TypeNameResolver
{
    /// <summary>
    /// Attempts assembly-qualified resolution first; on miss, scans loaded assemblies for a unique
    /// <see cref="Type.FullName"/> match. Throws <see cref="PipelineConfigurationException"/> when the
    /// name cannot be resolved or is ambiguous.
    /// </summary>
    public static Type Resolve(string typeName, string contextDescription)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new PipelineConfigurationException($"Type name is empty ({contextDescription}).");
        }

        var qualified = Type.GetType(typeName, throwOnError: false);
        if (qualified is not null)
        {
            return qualified;
        }

        var matches = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(SafeGetTypes)
            .Where(t => t is not null && (t.FullName == typeName || t.AssemblyQualifiedName == typeName))
            .Distinct()
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0]!,
            0 => throw new PipelineConfigurationException(
                $"Type '{typeName}' could not be loaded ({contextDescription}). " +
                "Use the assembly-qualified form 'Namespace.Type, AssemblyName' or ensure the assembly is loaded."),
            _ => throw new PipelineConfigurationException(
                $"Type name '{typeName}' is ambiguous; matched {matches.Length} types ({contextDescription}). " +
                "Use the assembly-qualified form."),
        };
    }

    private static IEnumerable<Type?> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
    }
}
