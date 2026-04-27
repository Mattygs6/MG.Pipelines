using System;
using System.Collections.Generic;

namespace MG.Pipelines;

/// <summary>Default pass-through resolver that returns the supplied name as the only candidate.</summary>
public class PipelineNameResolver : IPipelineNameResolver
{
    /// <inheritdoc/>
    public IList<string> ResolveNames(string localName)
    {
        if (localName is null)
        {
            throw new ArgumentNullException(nameof(localName));
        }

        return new[] { localName };
    }
}
