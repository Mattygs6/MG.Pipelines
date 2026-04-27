using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AwesomeAssertions;

using MG.Pipelines.Configuration.Tests.TestSupport;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace MG.Pipelines.Configuration.Tests;

public class ConfigurablePipelineTests
{
    [Fact]
    public async Task Executes_Tasks_In_Order_And_Returns_Ok()
    {
        var counter = new CounterState();
        var pipeline = new ConfigurablePipeline<CheckoutArgs>(
            new IPipelineTask<CheckoutArgs>[] { new ValidateTask(), new ChargeTask() },
            NullLogger<ConfigurablePipeline<CheckoutArgs>>.Instance);

        (await pipeline.ExecuteAsync(new CheckoutArgs(counter))).Should().Be(PipelineResult.Ok);
        counter.Calls.Should().Equal("validate", "charge");
    }

    [Fact]
    public async Task Logs_Unhandled_Task_Exceptions_To_ILogger()
    {
        var logger = new RecordingLogger<ConfigurablePipeline<CheckoutArgs>>();
        var boom = new InvalidOperationException("kapow");
        var pipeline = new ConfigurablePipeline<CheckoutArgs>(
            new IPipelineTask<CheckoutArgs>[] { new ThrowingTask(boom) },
            logger);

        var act = async () => await pipeline.ExecuteAsync(new CheckoutArgs(new CounterState()));

        var thrown = await act.Should().ThrowAsync<PipelineException>();
        thrown.Which.InnerException.Should().BeSameAs(boom);
        logger.Entries.Should().ContainSingle()
            .Which.Should().Match<LogEntry>(e =>
                e.Level == LogLevel.Error && e.Exception == boom);
    }

    [Fact]
    public void Null_Logger_Throws()
    {
        var act = () => new ConfigurablePipeline<CheckoutArgs>(
            new IPipelineTask<CheckoutArgs>[] { new ValidateTask() },
            null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class ThrowingTask : IPipelineTask<CheckoutArgs>
    {
        private readonly Exception exception;
        public ThrowingTask(Exception exception) { this.exception = exception; }
        public Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken cancellationToken = default) => throw exception;
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
}
