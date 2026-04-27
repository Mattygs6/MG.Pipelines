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
    public PipelineResult Execute(MyArgs args) { args.Value += 1; return PipelineResult.Ok; }
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
pipeline!.Execute(new MyArgs { Value = 41 }); // Value == 42
```

**DI-based:**

```csharp
services.AddPipelines(typeof(IncrementPipeline).Assembly);
// ...
var factory = provider.GetRequiredService<IPipelineFactory>();
var pipeline = factory.Create<MyArgs>("increment");
```

**Configuration-based** — define new pipelines or override attribute pipelines from `appsettings.json` (or any `IConfiguration` source):

```jsonc
{
  "Pipelines": [
    {
      "name": "increment",
      "argumentType": "MyApp.MyArgs, MyApp",
      "tasks": [ "MyApp.AddOne, MyApp" ]
    },
    {
      "name": "increment:vip",
      "argumentType": "MyApp.MyArgs, MyApp",
      "pipelineType": "MyApp.VipIncrementPipeline, MyApp",
      "tasks": [ "MyApp.AddOne, MyApp", "MyApp.AddTen, MyApp" ]
    }
  ]
}
```

The schema is a JSON array (rather than a name-keyed object) so that pipeline names containing `:` work cleanly — `:` is the `IConfiguration` path separator and would otherwise collide with key flattening.

```csharp
services.AddLogging();
services.AddPipelines(typeof(IncrementPipeline).Assembly);                                // attribute scan
services.AddPipelinesFromConfiguration(builder.Configuration.GetSection("Pipelines"));    // config layer (overrides + adds)
```

If `pipelineType` is omitted, a built-in `ConfigurablePipeline<T>` is used (logs unhandled task exceptions through `ILogger<>`).

## Pipeline semantics

Tasks run in registration order. A task returns a `PipelineResult`:

- `Ok` — continue.
- `Warn` — continue, but the pipeline's overall result is `Warn` (unless something later escalates it).
- `Abort` / `Fail` — stop. Executed tasks (including the failing one) that implement `IUndoablePipelineTask<T>` have `Undo` called in reverse order.

An unhandled exception is logged via `Pipeline<T>.Log`, undo is attempted, and
the original exception is re-thrown inside a `PipelineException`.

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
- Pushes to `dev` publish preview packages (`2.0.0-preview.<run-number>`) to NuGet.org.
- Tagging `v2.0.0` on `master` publishes stable `2.0.0` packages.

## License

MIT. See [LICENSE](LICENSE).
