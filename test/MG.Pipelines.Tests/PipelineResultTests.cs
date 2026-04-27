using AwesomeAssertions;

using Xunit;

namespace MG.Pipelines.Tests;

public class PipelineResultTests
{
    [Fact]
    public void Ordering_Is_Ok_Less_Than_Warn_Less_Than_Abort_Less_Than_Fail()
    {
        // Pipeline<T>.Execute depends on this ordering for short-circuiting.
        ((int)PipelineResult.Ok).Should().BeLessThan((int)PipelineResult.Warn);
        ((int)PipelineResult.Warn).Should().BeLessThan((int)PipelineResult.Abort);
        ((int)PipelineResult.Abort).Should().BeLessThan((int)PipelineResult.Fail);
    }
}
