namespace MG.Pipelines.Configuration;

/// <summary>
/// A single pipeline definition bound from configuration. The <c>tasks</c> array entries are parsed
/// separately (not bound onto this POCO) because each entry may be either a string (just the type
/// name) or an object with a <c>config</c> sub-block bound onto the task instance:
/// <code>
/// {
///   "name": "checkout:vip",
///   "argumentType": "MyApp.CheckoutArgs, MyApp",
///   "pipelineType": "MyApp.Pipelines.VipCheckout, MyApp",
///   "tasks": [
///     "MyApp.Tasks.Validate, MyApp",
///     { "type": "MyApp.Tasks.Charge, MyApp", "config": { "MaxRetries": 3 } }
///   ],
///   "args": { "Currency": "USD" }
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
}
