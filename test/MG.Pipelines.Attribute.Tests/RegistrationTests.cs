using System.Linq;

using AwesomeAssertions;

using MG.Pipelines.Attribute.Tests.TestSupport;

using Xunit;

namespace MG.Pipelines.Attribute.Tests;

[Collection(RegistrationCollection.Name)]
public class RegistrationTests
{
    [Fact]
    public void RegisterPipelines_Records_Valid_Attributes()
    {
        Registration.Clear();
        Registration.RegisterPipelines(new[] { typeof(PipelineA), typeof(PipelineB) });

        Registration.Pipelines.Keys.Should().BeEquivalentTo(new[] { "pipeline-a", "pipeline-b" });
        Registration.Pipelines["pipeline-a"].PipelineType.Should().Be<PipelineA>();
        Registration.Pipelines["pipeline-a"].Attribute.PipelineTasks
            .Should().BeEquivalentTo(new[] { typeof(TaskA1), typeof(TaskA2) });
    }

    [Fact]
    public void RegisterPipelines_Throws_When_Attribute_Has_Zero_Tasks()
    {
        Registration.Clear();
        var act = () => Registration.RegisterPipelines(new[] { typeof(PipelineWithNoTasks) });

        act.Should().Throw<PipelineAttributeRegistrationException>()
           .WithMessage("*must declare at least one task*");
    }

    [Fact]
    public void RegisterPipelines_Throws_When_Task_Generic_Argument_Mismatches()
    {
        Registration.Clear();
        var act = () => Registration.RegisterPipelines(new[] { typeof(PipelineWithMismatchedTask) });

        act.Should().Throw<PipelineAttributeRegistrationException>()
           .WithMessage("*must implement*");
    }

    [Fact]
    public void Clear_Empties_The_Registration_Map()
    {
        Registration.Clear();
        Registration.RegisterPipelines(new[] { typeof(PipelineA) });
        Registration.Pipelines.Should().NotBeEmpty();

        Registration.Clear();
        Registration.Pipelines.Should().BeEmpty();
    }

    [Fact]
    public void RegisterPipelines_Is_Idempotent_For_Same_Name()
    {
        Registration.Clear();
        Registration.RegisterPipelines(new[] { typeof(PipelineA) });
        Registration.RegisterPipelines(new[] { typeof(PipelineA) });

        Registration.Pipelines.Should().ContainSingle().Which.Key.Should().Be("pipeline-a");
    }
}
