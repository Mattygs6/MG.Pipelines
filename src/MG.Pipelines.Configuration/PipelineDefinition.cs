using System.Collections.Generic;

namespace MG.Pipelines.Configuration;

/// <summary>
/// A single pipeline definition bound from configuration. Maps to a JSON object like:
/// <code>
/// {
///   "name": "checkout:vip",
///   "argumentType": "MyApp.CheckoutArgs, MyApp",
///   "pipelineType": "MyApp.Pipelines.VipCheckout, MyApp",
///   "tasks": [ "MyApp.Tasks.Validate, MyApp", "MyApp.Tasks.Charge, MyApp" ]
/// }
/// </code>
/// </summary>
/// <remarks>
/// The schema is a JSON array (rather than a dictionary keyed by name) because pipeline names commonly
/// contain ':' (the IConfiguration path separator), which collides with dictionary-key flattening.
/// </remarks>
public sealed class PipelineDefinition
{
    /// <summary>The unique pipeline name. Required.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The pipeline argument type (the <c>T</c> in <see cref="Pipeline{T}"/>) as an assembly-qualified or
    /// scannable type name. Required when <see cref="PipelineType"/> is omitted; otherwise inferred.
    /// </summary>
    public string? ArgumentType { get; set; }

    /// <summary>
    /// The concrete pipeline implementation, as an assembly-qualified or scannable type name. Optional;
    /// when omitted, a built-in <see cref="ConfigurablePipeline{T}"/> is used.
    /// </summary>
    public string? PipelineType { get; set; }

    /// <summary>The ordered list of task type names to instantiate for the pipeline. Required and non-empty.</summary>
    public List<string> Tasks { get; set; } = new();
}
