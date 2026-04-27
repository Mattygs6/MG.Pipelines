using System.Collections.Generic;

using AwesomeAssertions;

using MG.Pipelines.Configuration.Tests.TestSupport;

using Xunit;

namespace MG.Pipelines.Configuration.Tests;

public class TypeNameResolverTests
{
    [Fact]
    public void Resolves_Assembly_Qualified_Name()
    {
        var resolved = TypeNameResolver.Resolve(typeof(CheckoutArgs).AssemblyQualifiedName!, "test");
        resolved.Should().Be<CheckoutArgs>();
    }

    [Fact]
    public void Resolves_Full_Name_Via_Assembly_Scan()
    {
        var resolved = TypeNameResolver.Resolve(typeof(ValidateTask).FullName!, "test");
        resolved.Should().Be<ValidateTask>();
    }

    [Fact]
    public void Throws_For_Empty_Name()
    {
        var act = () => TypeNameResolver.Resolve("", "task[0] of pipeline 'x'");
        act.Should().Throw<PipelineConfigurationException>().WithMessage("*empty*");
    }

    [Fact]
    public void Throws_With_Helpful_Message_For_Unknown_Type()
    {
        var act = () => TypeNameResolver.Resolve("MG.NotARealNamespace.NotAType", "argumentType of pipeline 'x'");
        act.Should().Throw<PipelineConfigurationException>()
           .WithMessage("*could not be loaded*")
           .Which.Message.Should().Contain("argumentType of pipeline 'x'");
    }

    [Fact]
    public void Resolves_Generic_Closed_Type_Via_Assembly_Qualified_Name()
    {
        // Generic closed types must use assembly-qualified form.
        var name = typeof(List<int>).AssemblyQualifiedName!;
        TypeNameResolver.Resolve(name, "test").Should().Be<List<int>>();
    }
}
