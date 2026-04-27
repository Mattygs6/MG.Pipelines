using System.Collections.Generic;

namespace MG.Pipelines;

/// <summary>Expands a logical pipeline name into the ordered list of candidate registration names to try.</summary>
public interface IPipelineNameResolver
{
    /// <summary>Returns candidate names, most-specific first.</summary>
    IList<string> ResolveNames(string localName);
}
