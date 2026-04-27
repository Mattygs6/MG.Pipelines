# MG.Pipelines

A small, unopinionated pipeline library for .NET. Compose an ordered list of
tasks that operate on a shared argument, short-circuit on the first failure,
and (optionally) roll back what has already run.

| Package | Purpose |
| --- | --- |
| `MG.Pipelines` | Core abstractions: `IPipeline<T>`, `IPipelineTask<T>`, `IUndoablePipelineTask<T>`, `Pipeline<T>`, `PipelineResult`. |
| `MG.Pipelines.Attribute` | Attribute-based registration — decorate a `Pipeline<T>` with `[Pipeline(...)]` and resolve it via `PipelineFactory.Instance`. |
| `MG.Pipelines.DependencyInjection` | `IServiceCollection` integration — `services.AddPipelines()` to scan assemblies and wire pipelines into MS DI. |
| `MG.Pipelines.Configuration` | Bind pipelines from `IConfiguration` (appsettings.json, env vars, etc.). Override attribute pipelines or define new ones entirely from config. |

`MG.Pipelines` and `MG.Pipelines.Attribute` target `netstandard2.0` and `net8.0`.
`MG.Pipelines.DependencyInjection` and `MG.Pipelines.Configuration` are `net8.0`
only (they use MS DI keyed services).

## Quickstart

```csharp
public class MyArgs { public int Value; }

public class AddOne : IPipelineTask<MyArgs>
{
    public Task<PipelineResult> ExecuteAsync(MyArgs args, CancellationToken ct = default)
    {
        args.Value += 1;
        return Task.FromResult(PipelineResult.Ok);
    }
}

[Pipeline("increment", typeof(MyArgs), typeof(AddOne))]
public class IncrementPipeline : Pipeline<MyArgs>
{
    public IncrementPipeline(IList<IPipelineTask<MyArgs>> tasks) : base(tasks) { }
    protected override void Log(Exception ex, string message) { /* ... */ }
}
```

**Attribute-based:**

```csharp
Registration.RegisterPipelines();
var pipeline = PipelineFactory.Instance.Create<MyArgs>("increment");
await pipeline!.ExecuteAsync(new MyArgs { Value = 41 }); // Value == 42
```

**DI-based:**

```csharp
services.AddPipelines(typeof(IncrementPipeline).Assembly);
// ...
var factory = provider.GetRequiredService<IPipelineFactory>();
var pipeline = factory.Create<MyArgs>("increment");
await pipeline!.ExecuteAsync(new MyArgs { Value = 41 }, cancellationToken);
```

**Configuration-based** — define new pipelines or override attribute pipelines from `appsettings.json` (or any `IConfiguration` source). Each definition can also include an `args` block whose properties are bound onto the args instance returned by `factory.CreateArgs<T>(name)`, and individual task entries can be objects with their own `config` block (bound onto the resolved task instance, with `[Required]` validation enforced):

```jsonc
{
  "Pipelines": [
    {
      "name": "increment",
      "argumentType": "MyApp.MyArgs, MyApp",
      "tasks": [
        { "type": "MyApp.AddOne, MyApp",
          "config": { "ApiKey": "secret-...", "TimeoutSeconds": 5 } }
      ],
      "args": { "Step": 1, "MaxValue": 100 }
    },
    {
      "name": "increment:vip",
      "argumentType": "MyApp.MyArgs, MyApp",
      "pipelineType": "MyApp.VipIncrementPipeline, MyApp",
      "tasks": [ "MyApp.AddOne, MyApp", "MyApp.AddTen, MyApp" ],
      "args": { "Step": 10, "MaxValue": 1000 }
    }
  ]
}
```

```csharp
var args = factory.CreateArgs<MyArgs>("increment");   // Step=1, MaxValue=100 from config
args.CustomerId = "abc";                                // request-specific overrides
await factory.Create<MyArgs>("increment")!.ExecuteAsync(args, cancellationToken);
```

The schema is a JSON array (rather than a name-keyed object) so that pipeline names containing `:` work cleanly — `:` is the `IConfiguration` path separator and would otherwise collide with key flattening.

```csharp
services.AddLogging();
services.AddPipelines(typeof(IncrementPipeline).Assembly);                                // attribute scan
services.AddPipelinesFromConfiguration(builder.Configuration.GetSection("Pipelines"));    // config layer (overrides + adds)
```

If `pipelineType` is omitted, a built-in `ConfigurablePipeline<T>` is used (logs unhandled task exceptions through `ILogger<>`).

## Pipeline semantics

Tasks run in registration order. A task returns a `Task<PipelineResult>`:

- `Ok` — continue.
- `Warn` — continue, but the pipeline's overall result is `Warn` (unless something later escalates it).
- `Abort` / `Fail` — stop. Executed tasks (including the failing one) that implement `IUndoablePipelineTask<T>` have `UndoAsync` called in reverse order.

An unhandled exception is logged via `Pipeline<T>.Log`, undo is attempted, and
the original exception is re-thrown inside a `PipelineException`.

**Cancellation** — `ExecuteAsync` accepts a `CancellationToken`. When cancelled,
the pipeline stops at the next task boundary, runs rollback over the executed
tasks, and rethrows the `OperationCanceledException` **unwrapped** (so callers
can catch it as control flow rather than as a pipeline error). The same token
is passed to `UndoAsync`; override `Pipeline<T>.UndoAsync` if you need cleanup
to run on a fresh token.

## More examples

See [docs/examples.md](docs/examples.md) for a full walkthrough — manual wiring,
attribute-based registration, MS DI, configuration overrides, custom name
resolvers (per-tenant / per-site), and rollback patterns.

## Building

```
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

## Releases

- CI runs on every push and PR (build + test on Ubuntu and Windows).
- Pushes to `dev` publish preview packages (`3.0.0-preview.<run-number>`) to NuGet.org.
- Tagging `v3.0.0` on `master` publishes stable `3.0.0` packages.

## License

MIT. See [LICENSE](LICENSE).
