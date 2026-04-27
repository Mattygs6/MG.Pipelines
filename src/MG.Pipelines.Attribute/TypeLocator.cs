using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MG.Pipelines.Attribute;

/// <summary>
/// Scans the current runtime for types that derive from a supplied ancestor. On first use it eagerly loads any
/// DLLs found next to the executing assembly so that pipeline types declared in plug-in assemblies that have
/// not yet been loaded by the runtime are discovered.
/// </summary>
public static class TypeLocator
{
    private static int assembliesForced;

    /// <summary>Resets the one-shot "force assemblies into the load context" flag. Primarily for tests.</summary>
    public static void ResetAssemblyForcing() => System.Threading.Interlocked.Exchange(ref assembliesForced, 0);

    /// <summary>Returns every loaded concrete type assignable to <paramref name="ancestor"/>.</summary>
    public static IEnumerable<Type> LocateTypes(Type ancestor, bool includeAbstract = false)
    {
        if (ancestor is null)
        {
            throw new ArgumentNullException(nameof(ancestor));
        }

        if (System.Threading.Interlocked.CompareExchange(ref assembliesForced, 1, 0) == 0)
        {
            ForceAssembliesIntoLoadContext();
        }

        var types = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            try
            {
                types.AddRange(assembly.GetTypes());
            }
            catch (ReflectionTypeLoadException ex)
            {
                types.AddRange(ex.Types.Where(t => t is not null)!);
            }
        }

        return types.Where(t =>
            t is not null
            && (includeAbstract || !t.IsAbstract)
            && !t.IsInterface
            && Reflection.DescendsFromAncestorType(t, ancestor));
    }

    /// <summary>Generic convenience overload of <see cref="LocateTypes(Type, bool)"/>.</summary>
    public static IEnumerable<Type> LocateTypes<T>(bool includeAbstract = false) where T : class =>
        LocateTypes(typeof(T), includeAbstract);

    private static void ForceAssembliesIntoLoadContext()
    {
        var loadedPaths = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(TryGetLocation)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToArray();

        var baseDirectory = AppContext.BaseDirectory;
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(baseDirectory, "*.dll", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TypeLocator: unable to enumerate '{baseDirectory}': {ex.Message}");
            return;
        }

        var toLoad = candidates
            .Where(p => !loadedPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var path in toLoad)
        {
            try
            {
                Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"TypeLocator: unable to load '{path}': {ex.Message}");
            }
        }
    }

    private static string? TryGetLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch
        {
            return null;
        }
    }
}
