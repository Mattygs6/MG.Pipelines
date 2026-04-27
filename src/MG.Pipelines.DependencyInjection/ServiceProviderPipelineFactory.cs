using System;
using System.Collections.Generic;

using MG.Pipelines.Attribute;

using Microsoft.Extensions.DependencyInjection;

namespace MG.Pipelines.DependencyInjection;

/// <summary>
/// An <see cref="IPipelineFactory"/> that resolves pipelines from an <see cref="IServiceProvider"/> using
/// keyed services. Candidate names are produced by an <see cref="IPipelineNameResolver"/> in order.
/// </summary>
public sealed class ServiceProviderPipelineFactory : IPipelineFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly IPipelineNameResolver nameResolver;

    /// <summary>Creates a new factory.</summary>
    public ServiceProviderPipelineFactory(IServiceProvider serviceProvider, IPipelineNameResolver nameResolver)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.nameResolver = nameResolver ?? throw new ArgumentNullException(nameof(nameResolver));
    }

    /// <inheritdoc/>
    public IEnumerable<string> AllPipelinesFor<T>()
    {
        foreach (var entry in Registration.Pipelines)
        {
            if (entry.Value.Attribute.ArgumentType == typeof(T))
            {
                yield return entry.Key;
            }
        }
    }

    /// <inheritdoc/>
    public IPipeline<T>? Create<T>(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        foreach (var candidate in nameResolver.ResolveNames(name))
        {
            var pipeline = serviceProvider.GetKeyedService<IPipeline<T>>(candidate);
            if (pipeline is not null)
            {
                return pipeline;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Constructs <typeparamref name="T"/> via <see cref="ActivatorUtilities.CreateInstance{T}(IServiceProvider, object[])"/>
    /// (so DI dependencies on the args ctor are resolved), then applies any registered
    /// <see cref="IPipelineArgsBinder"/> in turn. Multiple binders are applied in registration order.
    /// </remarks>
    public T CreateArgs<T>(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        var instance = ActivatorUtilities.CreateInstance<T>(serviceProvider);

        foreach (var binder in serviceProvider.GetServices<IPipelineArgsBinder>())
        {
            binder.Bind(name, instance);
        }

        return instance;
    }
}
