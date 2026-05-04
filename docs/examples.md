# MG.Pipelines — Registration Examples

Every example below uses this small domain — a checkout flow with three tasks:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MG.Pipelines;

public class CheckoutArgs
{
    public string CustomerId { get; init; } = "";
    public decimal Total { get; set; }
    public List<string> Trace { get; } = new();
}

public class ValidateCart : IPipelineTask<CheckoutArgs>
{
    public Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken ct = default)
    {
        args.Trace.Add("validate");
        return Task.FromResult(args.Total > 0 ? PipelineResult.Ok : PipelineResult.Fail);
    }
}

public class ChargeCard : IUndoablePipelineTask<CheckoutArgs>
{
    private readonly IPaymentGateway gateway;
    public ChargeCard(IPaymentGateway gateway) { this.gateway = gateway; }

    public async Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken ct = default)
    {
        args.Trace.Add("charge");
        await gateway.ChargeAsync(args.CustomerId, args.Total, ct);
        return PipelineResult.Ok;
    }

    public async Task<PipelineResult> UndoAsync(CheckoutArgs args, CancellationToken ct = default)
    {
        args.Trace.Add("refund");      // rolled back when a later task fails
        await gateway.RefundAsync(args.CustomerId, args.Total, ct);
        return PipelineResult.Ok;
    }
}

public class SendReceipt : IPipelineTask<CheckoutArgs>
{
    public Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken ct = default)
    {
        args.Trace.Add("receipt");
        return Task.FromResult(PipelineResult.Ok);
    }
}
```

The `Async` suffix and `CancellationToken` are required by the interface; pure
CPU-only tasks can use `Task.FromResult(...)` to satisfy the signature.

---

## 1. Pure manual wiring (no extras)

Just the `MG.Pipelines` package. No DI, no scanning, no config. Useful in tests
or when the call site already knows every task.

```csharp
public class CheckoutPipeline : Pipeline<CheckoutArgs>
{
    public CheckoutPipeline(IList<IPipelineTask<CheckoutArgs>> tasks) : base(tasks) { }
    protected override void Log(Exception ex, string message) =>
        Console.Error.WriteLine($"{message}\n{ex}");
}

var pipeline = new CheckoutPipeline(new IPipelineTask<CheckoutArgs>[]
{
    new ValidateCart(),
    new ChargeCard(gateway),
    new SendReceipt(),
});

var result = await pipeline.ExecuteAsync(
    new CheckoutArgs { CustomerId = "abc", Total = 42m },
    cancellationToken);
// result == PipelineResult.Ok
```

---

## 2. Attribute-based registration

Add the `MG.Pipelines.Attribute` package. Decorate the pipeline class with
`[Pipeline(...)]` and resolve through the static `PipelineFactory.Instance`.

```csharp
using MG.Pipelines.Attribute;

[Pipeline("checkout", typeof(CheckoutArgs),
    typeof(ValidateCart), typeof(ChargeCard), typeof(SendReceipt))]
public class CheckoutPipeline : Pipeline<CheckoutArgs>
{
    public CheckoutPipeline(IList<IPipelineTask<CheckoutArgs>> tasks) : base(tasks) { }
    protected override void Log(Exception ex, string message) { /* ... */ }
}

// Once at app startup — scans loaded assemblies for [Pipeline]
Registration.RegisterPipelines();

var pipeline = PipelineFactory.Instance.Create<CheckoutArgs>("checkout");
await pipeline!.ExecuteAsync(new CheckoutArgs { CustomerId = "abc", Total = 42m });
```

A single class can declare **multiple** `[Pipeline]` attributes — useful when
the same task list runs under several names:

```csharp
[Pipeline("checkout", typeof(CheckoutArgs), typeof(ValidateCart), typeof(ChargeCard))]
[Pipeline("checkout:guest", typeof(CheckoutArgs), typeof(ValidateCart), typeof(ChargeCard))]
public class CheckoutPipeline : Pipeline<CheckoutArgs> { /* ... */ }
```

---

## 3. Microsoft DI registration

Add the `MG.Pipelines.DependencyInjection` package. `AddPipelines(...)` scans
the supplied assemblies and registers tasks + pipelines as keyed services.

```csharp
using MG.Pipelines;
using MG.Pipelines.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IPaymentGateway, StripeGateway>();   // dependency of ChargeCard
services.AddPipelines(typeof(CheckoutPipeline).Assembly);

using var provider = services.BuildServiceProvider();

var factory = provider.GetRequiredService<IPipelineFactory>();
var pipeline = factory.Create<CheckoutArgs>("checkout");
await pipeline!.ExecuteAsync(
    new CheckoutArgs { CustomerId = "abc", Total = 42m },
    cancellationToken);
```

Tasks can take any DI-resolvable constructor arguments:

```csharp
public class ChargeCard : IUndoablePipelineTask<CheckoutArgs>
{
    private readonly IPaymentGateway gateway;
    private readonly ILogger<ChargeCard> logger;

    public ChargeCard(IPaymentGateway gateway, ILogger<ChargeCard> logger)
    {
        this.gateway = gateway;
        this.logger = logger;
    }
    // ... ExecuteAsync / UndoAsync as above
}
```

The pipeline class itself can take additional DI args alongside the task list:

```csharp
public class CheckoutPipeline : Pipeline<CheckoutArgs>
{
    private readonly ILogger<CheckoutPipeline> logger;

    public CheckoutPipeline(IList<IPipelineTask<CheckoutArgs>> tasks, ILogger<CheckoutPipeline> logger)
        : base(tasks)
    {
        this.logger = logger;
    }

    protected override void Log(Exception ex, string message) =>
        logger.LogError(ex, "{Message}", message);
}
```

---

## 4. Configuration-driven registration

Add the `MG.Pipelines.Configuration` package. Define pipelines in
`appsettings.json` (or any `IConfiguration` source) — useful when ops need to
swap task ordering without redeploying.

`appsettings.json`:
```jsonc
{
  "Pipelines": [
    {
      "name": "checkout",
      "argumentType": "MyApp.CheckoutArgs, MyApp",
      "tasks": [
        "MyApp.ValidateCart, MyApp",
        "MyApp.ChargeCard, MyApp",
        "MyApp.SendReceipt, MyApp"
      ]
    }
  ]
}
```

Wire-up:
```csharp
using MG.Pipelines.Configuration;

services.AddLogging();
services.AddSingleton<IPaymentGateway, StripeGateway>();
services.AddPipelinesFromConfiguration(builder.Configuration.GetSection("Pipelines"));
```

When `pipelineType` is omitted (as above), a built-in
`ConfigurablePipeline<CheckoutArgs>` is used — it logs unhandled task
exceptions through `ILogger<ConfigurablePipeline<CheckoutArgs>>`.

To use your own `Pipeline<T>` subclass:
```jsonc
{
  "name": "checkout",
  "pipelineType": "MyApp.CheckoutPipeline, MyApp",
  "tasks": [ "MyApp.ValidateCart, MyApp", "MyApp.ChargeCard, MyApp" ]
}
```
With `pipelineType` set, `argumentType` is inferred from `Pipeline<T>` — you only
need it when inference can't find a closed `Pipeline<T>` base (e.g. the class
derives via several layers of inheritance).

> **Names with `:`** — the schema is a JSON array (not a name-keyed object) so
> entries like `"name": "checkout:vip"` work cleanly. `:` is the
> `IConfiguration` path separator and would otherwise collide with key
> flattening.

---

## 5. Setting args properties from configuration

Each definition may include an `args` block. Its keys are bound onto a fresh
args instance every time you call `factory.CreateArgs<T>(name)`, using the
standard `Microsoft.Extensions.Configuration.Binder` (so nested objects, lists,
enums, and DI-friendly args ctors all work).

```jsonc
{
  "Pipelines": [
    {
      "name": "checkout",
      "argumentType": "MyApp.CheckoutArgs, MyApp",
      "tasks": [ "MyApp.Tasks.Validate, MyApp", "MyApp.Tasks.Charge, MyApp" ],
      "args": {
        "Currency": "USD",
        "MaxRetries": 3,
        "Tags": [ "alpha", "beta" ],
        "Limits": { "DailyCap": 5000 }
      }
    }
  ]
}
```

```csharp
public class CheckoutArgs
{
    public string Currency { get; set; } = "USD";
    public int MaxRetries { get; set; } = 1;
    public List<string> Tags { get; set; } = new();
    public LimitsConfig? Limits { get; set; }

    public string? CustomerId { get; set; }   // request-specific, set after CreateArgs
    public decimal Total { get; set; }
}

public class LimitsConfig
{
    public int DailyCap { get; set; }
}
```

```csharp
services.AddLogging();
services.AddPipelinesFromConfiguration(builder.Configuration.GetSection("Pipelines"));

// ...
var factory = provider.GetRequiredService<IPipelineFactory>();

var args = factory.CreateArgs<CheckoutArgs>("checkout");
// args.Currency == "USD", args.MaxRetries == 3, args.Tags == ["alpha","beta"],
// args.Limits.DailyCap == 5000

args.CustomerId = httpContext.User.Identity!.Name;     // request-specific overrides
args.Total = cart.Total;

await factory.Create<CheckoutArgs>("checkout")!.ExecuteAsync(args, cancellationToken);
```

**Caller overrides win** — properties you set after `CreateArgs` are not
overwritten. The binder is only invoked at instance creation.

**Layering** — calling `AddPipelinesFromConfiguration` more than once
accumulates entries from every source (e.g. `appsettings.json` + an environment
overlay). When two sources define the same pipeline name, the later call's
`args` block replaces the earlier one wholesale.

**Without configuration** — `factory.CreateArgs<T>(name)` is always safe to
call; if no `args` section is registered for that name, you simply get a
default-constructed instance. Same call site works for attribute-only and
DI-only registrations:

```csharp
// Works whether "checkout" was registered via [Pipeline], AddPipelines, or AddPipelinesFromConfiguration.
var args = factory.CreateArgs<CheckoutArgs>("checkout");
```

**Args with DI dependencies** — the DI factory uses
`ActivatorUtilities.CreateInstance<T>`, so args ctors can request services:

```csharp
public class CheckoutArgs
{
    public IClock Clock { get; }
    public CheckoutArgs(IClock clock) { Clock = clock; }
    public string? CustomerId { get; set; }
}
```

Configuration values are bound on top of the DI-constructed instance.

**Custom binders** — the contract is `IPipelineArgsBinder` (in
`MG.Pipelines.DependencyInjection`). The Configuration package supplies an
implementation, but you can register additional binders (registered binders
run in registration order, mutating the same instance):

```csharp
services.AddSingleton<IPipelineArgsBinder, MyEnvironmentOverlayBinder>();
```

## 6. Per-task configuration

Each entry in a `tasks` array can be either a bare type-name string (the
default) or an object of the form `{ "type": "...", "config": { ... } }`. The
`config` block is bound onto the freshly resolved task instance via the
standard `Microsoft.Extensions.Configuration.Binder`, before the task is added
to the pipeline.

```jsonc
{
  "Pipelines": [
    {
      "name": "checkout",
      "argumentType": "MyApp.CheckoutArgs, MyApp",
      "tasks": [
        "MyApp.ValidateCart, MyApp",
        {
          "type": "MyApp.ChargeCard, MyApp",
          "config": {
            "ApiKey": "live-sk-...",
            "TimeoutSeconds": 30,
            "MaxRetries": 3
          }
        },
        {
          "type": "MyApp.SendReceipt, MyApp",
          "config": { "TemplateId": "tpl_default" }
        }
      ]
    }
  ]
}
```

```csharp
public class ChargeCard : IUndoablePipelineTask<CheckoutArgs>
{
    [Required]
    public string ApiKey { get; set; } = null!;     // missing config -> throws

    public int TimeoutSeconds { get; set; } = 5;     // optional, has a default
    public int MaxRetries { get; set; } = 1;
    // ... ExecuteAsync / UndoAsync
}
```

**Validation (required properties)** — after binding, the task instance is
validated. Two mechanisms are recognised:

1. **`[Required]` (and other `System.ComponentModel.DataAnnotations`)** —
   classic attribute-based validation via `Validator.TryValidateObject`. Use this
   for reference types (`string`, custom classes, `int?`, etc.). Note that
   `[Required]` on a non-nullable value type (`[Required] int Foo`) is a no-op
   in DataAnnotations — `int` can never be null.
2. **`required` keyword (C# 11)** — `public required int MaxRetries { get; set; }`.
   The keyword is compile-time only and is bypassed by reflection-based
   instantiation, so the binder enforces it at runtime by checking that the
   supplied config section actually carried a key matching the property name.
   This works correctly for value types: `required int MaxRetries` is satisfied
   by `"MaxRetries": 0` but rejected if the key is absent.

Either mechanism throws `PipelineConfigurationException` listing the offending
properties — at `factory.Create<T>(name)` time, so misconfiguration is caught
before the pipeline runs:

```text
Task 'MyApp.ChargeCard' at index 1 of pipeline 'checkout' failed configuration
validation: The ApiKey field is required.; The MaxRetries field is required
(marked with the C# 'required' keyword).
```

Validation runs for every task in a config-registered pipeline, even tasks
declared in the bare-string form. Tasks registered exclusively through
`[Pipeline]` / `AddPipelines` (no Configuration overlay) are not validated.

> **Caveat for `required`** — the binder treats "required" as "the config key
> must be present." If you combine `required` with a property initializer
> (`public required int MaxRetries { get; set; } = 5;`) and a
> `[SetsRequiredMembers]` constructor, the value is set at construction but the
> binder will still demand the config key. If you want a default-with-fallback
> semantic, drop the `required` keyword and validate manually, or use
> `[Required]` on a nullable type.

**Per-instance, not per-type** — config is attached to each ordered task slot,
so the same task type can appear in multiple pipelines with different config,
and even multiple times within one pipeline:

```jsonc
{
  "name": "fan-out",
  "argumentType": "MyApp.RequestArgs, MyApp",
  "tasks": [
    { "type": "MyApp.HttpCall, MyApp",
      "config": { "Endpoint": "https://primary.api/" } },
    { "type": "MyApp.HttpCall, MyApp",
      "config": { "Endpoint": "https://fallback.api/" } }
  ]
}
```

**Lifetime** — tasks targeted by per-instance config must be transient (the
default registration lifetime). Pre-registering a task as `AddSingleton` would
let successive `factory.Create<T>(name)` calls overwrite each other's config on
the shared instance.

**Custom binders** — config-block binding and `[Required]`/`required`
validation run inline in the pipeline build path; the framework no longer
registers an internal `IPipelineTaskInstanceBinder`. If you need cross-cutting
per-instance customisation from another source (e.g. secrets, feature flags),
register your own `IPipelineTaskInstanceBinder` (the contract lives in
`MG.Pipelines.DependencyInjection`). Multiple registrations all run in
registration order against each task instance, after the config block has been
applied.

---

## 7. Live task mutation and reload-on-change

Pipeline names and argument types are fixed at
`AddPipelinesFromConfiguration` time, but the per-pipeline task list is
mutable at runtime. Two ways in:

**Programmatic mutation** — resolve `IPipelineTaskRegistry` and replace the
task list wholesale. The swap is atomic, so an in-flight `Create<T>(name)`
either sees the old list or the new one — never a half-applied state.

```csharp
public class FraudToggleService
{
    private readonly IPipelineTaskRegistry registry;
    public FraudToggleService(IPipelineTaskRegistry registry) { this.registry = registry; }

    public void EnableFraudCheck() =>
        registry.SetTasks("checkout", new[]
        {
            new PipelineTaskSlot(typeof(ValidateCart)),
            new PipelineTaskSlot(typeof(FraudCheck)),
            new PipelineTaskSlot(typeof(ChargeCard)),
            new PipelineTaskSlot(typeof(SendReceipt)),
        });

    public void DisableFraudCheck() =>
        registry.SetTasks("checkout", new[]
        {
            new PipelineTaskSlot(typeof(ValidateCart)),
            new PipelineTaskSlot(typeof(ChargeCard)),
            new PipelineTaskSlot(typeof(SendReceipt)),
        });
}
```

`PipelineTaskSlot` optionally carries an `IConfiguration` for that task — the
same config block you'd nest under `tasks[].config` in JSON. Tasks added at
runtime do not need to be pre-registered in DI; they're activated via
`ActivatorUtilities.GetServiceOrCreateInstance`, so DI-resolved dependencies
still flow into the task constructor when available.

`SetTasks` validates each task type implements `IPipelineTask<T>` for the
pipeline's argument type before swapping. A bad list throws
`PipelineConfigurationException` and the existing list is left intact.

**Reload from `IConfiguration`** —
`AddPipelinesFromConfiguration` automatically subscribes to the supplied
section's reload token via `ChangeToken.OnChange(...)`. When the underlying
source signals a change (e.g. a JSON file registered with
`reloadOnChange: true`), each pipeline's task list is re-parsed and atomically
swapped. New names introduced by reload are ignored, and any pipeline whose
reloaded definition fails validation keeps its existing list.

```csharp
var pipelineConfig = new ConfigurationBuilder()
    .AddJsonFile("pipelines.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"pipelines.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .Build();

services.AddPipelinesFromConfiguration(pipelineConfig.GetSection("Pipelines"));
```

Edits to either JSON file flow into the registry without restarting the host —
useful for ops-driven task ordering changes (toggling a fraud check, swapping a
gateway adapter, adding a metric-emitting wrapper) without redeploys.

> **Scalar values inside an existing slot** — these already hot-reload without
> any registry mutation: `IConfigurationSection` references are live, so a
> change to e.g. `tasks[1].config.TimeoutSeconds` flows into the next task
> instance the binder produces. The reload subscription kicks in only when the
> *shape* of the task list changes (added, removed, reordered, or retyped).

---

## 8. Attribute + Configuration (override pattern)

Register pipelines from attributes for the default behaviour, then layer
configuration on top to override task lists at deploy time.

```csharp
[Pipeline("checkout", typeof(CheckoutArgs),
    typeof(ValidateCart), typeof(ChargeCard), typeof(SendReceipt))]
public class CheckoutPipeline : Pipeline<CheckoutArgs> { /* ... */ }
```

```csharp
services.AddPipelines(typeof(CheckoutPipeline).Assembly);                          // baseline
services.AddPipelinesFromConfiguration(config.GetSection("Pipelines"));            // overlay
```

If `Pipelines` contains an entry named `"checkout"`, its task list replaces the
attribute-declared one (the most recent keyed registration wins inside MS DI).
If it doesn't, the attribute version stands. New names in config are added
alongside the attribute pipelines.

Production scenario: ship sensible defaults via `[Pipeline]`, expose a fraud
check toggle via config:

```jsonc
// appsettings.Production.json
{
  "Pipelines": [
    {
      "name": "checkout",
      "argumentType": "MyApp.CheckoutArgs, MyApp",
      "pipelineType": "MyApp.CheckoutPipeline, MyApp",
      "tasks": [
        "MyApp.ValidateCart, MyApp",
        "MyApp.FraudCheck, MyApp",
        "MyApp.ChargeCard, MyApp",
        "MyApp.SendReceipt, MyApp"
      ]
    }
  ]
}
```

---

## 9. Custom name resolution

Implement `IPipelineNameResolver` to expand a logical name into multiple
candidates (most-specific first). The factory tries each in order and returns
the first match.

```csharp
public class TenantPipelineNameResolver : IPipelineNameResolver
{
    private readonly ITenantContext context;

    public TenantPipelineNameResolver(ITenantContext context)
    {
        this.context = context;
    }

    public IList<string> ResolveNames(string localName) => new[]
    {
        $"{localName}:{context.CurrentTenant}",   // e.g. "checkout:acme"
        localName,                                 // fall back to the global definition
    };
}

services.AddSingleton<ITenantContext, HttpHeaderTenantContext>();
services.AddSingleton<IPipelineNameResolver, TenantPipelineNameResolver>();
services.AddPipelines(typeof(CheckoutPipeline).Assembly);
```

Now define both variants:
```csharp
[Pipeline("checkout", typeof(CheckoutArgs), typeof(ValidateCart), typeof(ChargeCard))]
public class CheckoutPipeline : Pipeline<CheckoutArgs> { /* ... */ }

[Pipeline("checkout:acme", typeof(CheckoutArgs),
    typeof(ValidateCart), typeof(AcmeDiscount), typeof(ChargeCard))]
public class AcmeCheckoutPipeline : Pipeline<CheckoutArgs> { /* ... */ }
```

A `factory.Create<CheckoutArgs>("checkout")` call from Acme's tenant context
resolves `AcmeCheckoutPipeline`; everyone else falls through to
`CheckoutPipeline`.

---

## 10. Rollback (undoable tasks)

Tasks that implement `IUndoablePipelineTask<T>` are rolled back in reverse
order when a later task aborts, fails, or throws. Non-undoable tasks in the
executed set are simply skipped during rollback.

```csharp
[Pipeline("checkout", typeof(CheckoutArgs),
    typeof(ValidateCart),       // not undoable
    typeof(ChargeCard),          // undoable — refund issued on rollback
    typeof(ReserveInventory),    // undoable — release reservation on rollback
    typeof(SendReceipt))]        // not undoable
public class CheckoutPipeline : Pipeline<CheckoutArgs> { /* ... */ }
```

If `SendReceipt` throws:
1. `Pipeline<T>.Log(ex, message)` is invoked with the exception.
2. `SendReceipt.UndoAsync` is **not** called (it isn't undoable).
3. `ReserveInventory.UndoAsync`, then `ChargeCard.UndoAsync` run in reverse.
4. The original exception is rethrown wrapped in a `PipelineException`.

Override `Pipeline<T>.UndoAsync` if you need a custom rollback strategy (e.g.
abort on the first undo failure rather than continuing).

---

## 11. Cancellation

`ExecuteAsync` accepts a `CancellationToken`. When cancelled, the pipeline
stops at the next task boundary, runs rollback over the executed tasks, and
rethrows the `OperationCanceledException` **unwrapped**:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

try
{
    await factory.Create<CheckoutArgs>("checkout")!.ExecuteAsync(args, cts.Token);
}
catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
{
    // Cancellation is control flow, not a pipeline error — undoable tasks have already run.
    return Results.StatusCode(499);
}
```

The same token is passed to `UndoAsync`, so by default cleanup work also
respects cancellation. If you need cleanup to run unconditionally, override
`UndoAsync` on the pipeline subclass and pass `CancellationToken.None`:

```csharp
public class CheckoutPipeline : Pipeline<CheckoutArgs>
{
    // ...
    protected override Task UndoAsync(
        IList<IPipelineTask<CheckoutArgs>> executedTasks,
        CheckoutArgs args,
        CancellationToken cancellationToken) =>
        base.UndoAsync(executedTasks, args, CancellationToken.None);
}
```

Inside a task, observe the token at any await boundary or by calling
`cancellationToken.ThrowIfCancellationRequested()`. The pipeline catches
`OperationCanceledException` from `ExecuteAsync`, treats it as a cancel signal,
and rolls back.

---

## 12. Result semantics

```csharp
public Task<PipelineResult> ExecuteAsync(CheckoutArgs args, CancellationToken ct = default)
{
    if (args.Total <= 0)               return Task.FromResult(PipelineResult.Fail);
    if (args.Total > 10_000)            return Task.FromResult(PipelineResult.Abort);
    if (CartIsExpiringSoon(args))       return Task.FromResult(PipelineResult.Warn);
    return Task.FromResult(PipelineResult.Ok);
}
```

The pipeline's overall result is the **highest** task result observed (`Ok < Warn < Abort < Fail`).
A returned `Warn` does not stop the pipeline; `Abort` and `Fail` do, and trigger
rollback.

---

## 13. Putting it together (ASP.NET Core)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPaymentGateway, StripeGateway>();
builder.Services.AddSingleton<IPipelineNameResolver, TenantPipelineNameResolver>();
builder.Services.AddPipelines(typeof(CheckoutPipeline).Assembly);
builder.Services.AddPipelinesFromConfiguration(builder.Configuration.GetSection("Pipelines"));

var app = builder.Build();

app.MapPost("/checkout", async (
    CheckoutRequest request,
    IPipelineFactory factory,
    CancellationToken cancellationToken) =>
{
    // Configured defaults applied; request data layered on top.
    var args = factory.CreateArgs<CheckoutArgs>("checkout");
    args.CustomerId = request.CustomerId;
    args.Total = request.Total;

    var pipeline = factory.Create<CheckoutArgs>("checkout")
        ?? throw new InvalidOperationException("checkout pipeline is not registered");

    try
    {
        var result = await pipeline.ExecuteAsync(args, cancellationToken);
        return result switch
        {
            PipelineResult.Ok    => Results.Ok(args),
            PipelineResult.Warn  => Results.Ok(new { args, warning = "completed with warnings" }),
            PipelineResult.Abort => Results.Conflict("checkout aborted"),
            _                    => Results.Problem("checkout failed"),
        };
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // The HTTP request was cancelled; rollback already ran.
        return Results.StatusCode(499);
    }
});

app.Run();
```
