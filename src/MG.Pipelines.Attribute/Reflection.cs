using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MG.Pipelines.Attribute;

/// <summary>
/// Compiled-expression activators and type-relationship helpers.
/// </summary>
internal static class Reflection
{
    public delegate T CompiledActivator<out T>(params object[] args);

    private static readonly ConcurrentDictionary<ActivatorKey, CompiledActivator<object>?> Activators = new();

    /// <summary>
    /// Returns a compiled activator that constructs <paramref name="type"/> using the constructor whose
    /// parameters match <paramref name="parameterTypes"/>, or <see langword="null"/> if no such constructor exists.
    /// </summary>
    public static CompiledActivator<T>? GetActivator<T>(Type type, params Type[] parameterTypes)
    {
        var key = new ActivatorKey(type, parameterTypes);
        var boxed = Activators.GetOrAdd(key, static k =>
        {
            var ctor = FindConstructor(k.Type, k.ParameterTypes);
            return ctor is null ? null : BuildBoxedActivator(ctor);
        });

        return boxed is null ? null : (args => (T)boxed(args));
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="type"/> derives from or implements <paramref name="ancestor"/> (supports open generics).</summary>
    public static bool DescendsFromAncestorType(Type? type, Type ancestor)
    {
        while (type is not null && type != typeof(object))
        {
            if (ancestor.IsAssignableFrom(type))
            {
                return true;
            }

            if (ancestor.IsGenericType && DescendsFromGeneric(type, ancestor))
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    private static bool DescendsFromGeneric(Type? type, Type ancestor)
    {
        while (type is not null && type != typeof(object))
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == ancestor)
            {
                return true;
            }

            foreach (var @interface in type.GetInterfaces())
            {
                if (@interface.IsGenericType && @interface.GetGenericTypeDefinition() == ancestor)
                {
                    return true;
                }
            }

            type = type.BaseType;
        }

        return false;
    }

    private static ConstructorInfo? FindConstructor(Type type, Type[] parameterTypes)
    {
        var ctors = type.GetConstructors();
        if (parameterTypes.Length == 0)
        {
            return ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
        }

        return ctors.FirstOrDefault(c =>
        {
            var ps = c.GetParameters();
            if (ps.Length != parameterTypes.Length)
            {
                return false;
            }

            for (var i = 0; i < ps.Length; i++)
            {
                if (!ps[i].ParameterType.IsAssignableFrom(parameterTypes[i]))
                {
                    return false;
                }
            }

            return true;
        });
    }

    private static CompiledActivator<object> BuildBoxedActivator(ConstructorInfo constructor)
    {
        var parameters = constructor.GetParameters();
        var argsParameter = Expression.Parameter(typeof(object[]), "args");
        var argExpressions = new Expression[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var indexed = Expression.ArrayIndex(argsParameter, Expression.Constant(i));
            argExpressions[i] = Expression.Convert(indexed, parameters[i].ParameterType);
        }

        var newExpression = Expression.New(constructor, argExpressions);
        var body = Expression.Convert(newExpression, typeof(object));

        return Expression.Lambda<CompiledActivator<object>>(body, argsParameter).Compile();
    }

    private readonly struct ActivatorKey : IEquatable<ActivatorKey>
    {
        public readonly Type Type;
        public readonly Type[] ParameterTypes;

        public ActivatorKey(Type type, Type[] parameterTypes)
        {
            Type = type;
            ParameterTypes = parameterTypes;
        }

        public bool Equals(ActivatorKey other) =>
            Type == other.Type && ParameterTypes.SequenceEqual(other.ParameterTypes);

        public override bool Equals(object? obj) => obj is ActivatorKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Type.GetHashCode();
                foreach (var p in ParameterTypes)
                {
                    hash = (hash * 397) ^ p.GetHashCode();
                }

                return hash;
            }
        }
    }
}
