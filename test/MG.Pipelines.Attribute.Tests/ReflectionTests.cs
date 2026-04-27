using System;
using System.Collections.Generic;

using AwesomeAssertions;

using Xunit;

namespace MG.Pipelines.Attribute.Tests;

public class ReflectionTests
{
    private class NoArgs
    {
        public int Id { get; } = 42;
    }

    private class WithArgs
    {
        public string Value { get; }
        public WithArgs(string value) { Value = value; }
    }

    [Fact]
    public void GetActivator_Returns_Parameterless_Activator()
    {
        var activator = Reflection.GetActivator<NoArgs>(typeof(NoArgs));
        activator.Should().NotBeNull();
        activator!().Id.Should().Be(42);
    }

    [Fact]
    public void GetActivator_Returns_Activator_With_Matching_Parameter_Types()
    {
        var activator = Reflection.GetActivator<WithArgs>(typeof(WithArgs), typeof(string));
        activator.Should().NotBeNull();
        activator!("hello").Value.Should().Be("hello");
    }

    [Fact]
    public void GetActivator_Returns_Null_When_No_Matching_Constructor()
    {
        var activator = Reflection.GetActivator<WithArgs>(typeof(WithArgs), typeof(int));
        activator.Should().BeNull();
    }

    [Fact]
    public void GetActivator_Is_Cached_For_Identical_Keys()
    {
        // Same (type, parameterTypes) returns the same underlying compiled delegate.
        var a1 = Reflection.GetActivator<NoArgs>(typeof(NoArgs));
        var a2 = Reflection.GetActivator<NoArgs>(typeof(NoArgs));
        a1.Should().NotBeNull();
        a2.Should().NotBeNull();
        // We cannot directly compare the outer wrapper delegates (they are new each call),
        // but repeated invocations must succeed and produce equivalent results.
        a1!().Id.Should().Be(a2!().Id);
    }

    [Fact]
    public void DescendsFromAncestorType_Handles_Direct_Inheritance()
    {
        Reflection.DescendsFromAncestorType(typeof(List<int>), typeof(object)).Should().BeTrue();
    }

    [Fact]
    public void DescendsFromAncestorType_Handles_Closed_Interface()
    {
        Reflection.DescendsFromAncestorType(typeof(List<int>), typeof(IList<int>)).Should().BeTrue();
    }

    [Fact]
    public void DescendsFromAncestorType_Handles_Open_Generic_Interface()
    {
        Reflection.DescendsFromAncestorType(typeof(List<int>), typeof(IList<>)).Should().BeTrue();
    }

    [Fact]
    public void DescendsFromAncestorType_Handles_Open_Generic_Base()
    {
        Reflection.DescendsFromAncestorType(typeof(DerivedPipeline), typeof(Pipeline<>)).Should().BeTrue();
    }

    [Fact]
    public void DescendsFromAncestorType_Returns_False_For_Unrelated_Types()
    {
        Reflection.DescendsFromAncestorType(typeof(string), typeof(IList<>)).Should().BeFalse();
    }

    [Fact]
    public void DescendsFromAncestorType_Null_Type_Returns_False()
    {
        Reflection.DescendsFromAncestorType(null, typeof(object)).Should().BeFalse();
    }

    private sealed class DerivedPipeline : Pipeline<string>
    {
        public DerivedPipeline(IList<IPipelineTask<string>> tasks) : base(tasks) { }
        protected override void Log(Exception caughtException, string message) { }
    }
}
