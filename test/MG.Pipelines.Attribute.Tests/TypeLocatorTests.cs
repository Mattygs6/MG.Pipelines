using System.Linq;

using AwesomeAssertions;

using MG.Pipelines.Attribute.Tests.TestSupport;

using Xunit;

namespace MG.Pipelines.Attribute.Tests;

[Collection(RegistrationCollection.Name)]
public class TypeLocatorTests
{
    [Fact]
    public void LocateTypes_Finds_Concrete_Pipeline_Implementations()
    {
        var types = TypeLocator.LocateTypes(typeof(IPipeline<>)).ToArray();

        types.Should().Contain(typeof(PipelineA));
        types.Should().Contain(typeof(PipelineB));
    }

    [Fact]
    public void LocateTypes_Excludes_Abstract_And_Interface_Types_By_Default()
    {
        var types = TypeLocator.LocateTypes(typeof(IPipeline<>)).ToArray();

        types.Should().NotContain(typeof(Pipeline<>));
        types.Should().NotContain(typeof(IPipeline<>));
    }

    [Fact]
    public void Generic_Overload_Works()
    {
        var types = TypeLocator.LocateTypes<Pipeline<ArgsA>>().ToArray();
        types.Should().Contain(typeof(PipelineA));
    }
}
