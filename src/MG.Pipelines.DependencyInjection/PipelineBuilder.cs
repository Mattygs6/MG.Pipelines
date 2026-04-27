using System;
using System.Collections;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;

namespace MG.Pipelines.DependencyInjection;

/// <summary>Constructs a pipeline instance from an <see cref="IServiceProvider"/> and an ordered task list.</summary>
internal static class PipelineBuilder
{
    /// <summary>
    /// Resolves each task type from <paramref name="serviceProvider"/>, packs them into a strongly typed
    /// <see cref="List{T}"/> of <see cref="IPipelineTask{T}"/>, and constructs <paramref name="pipelineType"/>
    /// via <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type, object[])"/>.
    /// </summary>
    /// <param name="serviceProvider">The container to resolve tasks and ancillary pipeline ctor args from.</param>
    /// <param name="pipelineType">A concrete type that derives from <see cref="Pipeline{T}"/>.</param>
    /// <param name="taskInterfaceType">The closed <see cref="IPipelineTask{T}"/> interface (e.g. <c>typeof(IPipelineTask&lt;CheckoutArgs&gt;)</c>).</param>
    /// <param name="taskTypes">Concrete task types in execution order.</param>
    public static object Build(
        IServiceProvider serviceProvider,
        Type pipelineType,
        Type taskInterfaceType,
        IReadOnlyList<Type> taskTypes)
    {
        var taskListType = typeof(List<>).MakeGenericType(taskInterfaceType);
        var taskList = (IList)Activator.CreateInstance(taskListType)!;

        for (var i = 0; i < taskTypes.Count; i++)
        {
            taskList.Add(serviceProvider.GetRequiredService(taskTypes[i]));
        }

        return ActivatorUtilities.CreateInstance(serviceProvider, pipelineType, taskList);
    }
}
