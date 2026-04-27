using System.Collections.Generic;
using System.IO;
using System.Text;

using Microsoft.Extensions.Configuration;

namespace MG.Pipelines.Configuration.Tests.TestSupport;

internal static class ConfigBuilders
{
    /// <summary>Builds an <see cref="IConfigurationSection"/> from inline JSON, returning the named section.</summary>
    public static IConfigurationSection BuildSection(string json, string sectionName = "Pipelines")
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(bytes))
            .Build();
        return config.GetSection(sectionName);
    }

    /// <summary>Builds an <see cref="IConfigurationSection"/> from a flat dictionary of "Section:..." keys.</summary>
    public static IConfigurationSection BuildSection(Dictionary<string, string?> values, string sectionName = "Pipelines")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return config.GetSection(sectionName);
    }
}
