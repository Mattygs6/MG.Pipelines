# MG.Pipelines — Registration Examples

Every example below uses this small domain — a checkout flow with three tasks:

```csharp
using System;
using System.Collections.Generic;
using MG.Pipelines;

public class CheckoutArgs
{
    public string CustomerId { get; init; } = "";
    public decimal Total { get; set; }
    public List<string> Trace { get; } = new();
}

public class ValidateCart : IPipelineTask<CheckoutArgs>
{
    public PipelineResult Execute(CheckoutArgs args)
    {
        args.Trace.Add("validate");
        return args.Total > 0 ? PipelineResult.Ok : PipelineResult.Fail;
    }
}

public class ChargeCard : IUndoablePipelineTask<CheckoutArgs>
{
    public PipelineResult Execute(CheckoutArgs args)
    {
        args.Trace.Add("charge");
        return PipelineResult.Ok;
    }

    public PipelineResult Undo(CheckoutArgs args)
    {
        args.Trace.Add("refund");      // rolled back when a later task fails
        return PipelineResult.Ok;
    }
}

public class SendReceipt : IPipelineTask<CheckoutArgs>
{
    public PipelineResult Execute(CheckoutArgs args)
    {
        args.Trace.Add("receipt");
        return PipelineResult.Ok;
    }
}
```

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
    new ChargeCard(),
    new SendReceipt(),
});

var result = pipeline.Execute(new CheckoutArgs { CustomerId = "abc", Total = 42m });
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
pipeline!.Execute(new CheckoutArgs { CustomerId = "abc", Total = 42m });
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
pipeline!.Execute(new CheckoutArgs { CustomerId = "abc", Total = 42m });
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
    // ...
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

## 5. Attribute + Configuration (override pattern)

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

## 6. Custom name resolution

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

## 7. Rollback (undoable tasks)

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
2. `SendReceipt.Undo` is **not** called (it isn't undoable).
3. `ReserveInventory.Undo`, then `ChargeCard.Undo` run in reverse.
4. The original exception is rethrown wrapped in a `PipelineException`.

Override `Pipeline<T>.Undo` if you need a custom rollback strategy (e.g. abort
on the first undo failure rather than continuing).

---

## 8. Result semantics

```csharp
public PipelineResult Execute(CheckoutArgs args)
{
    if (args.Total <= 0) return PipelineResult.Fail;          // stop, rollback, return Fail
    if (args.Total > 10_000) return PipelineResult.Abort;     // stop, rollback, return Abort
    if (CartIsExpiringSoon(args)) return PipelineResult.Warn; // continue, but escalate result
    return PipelineResult.Ok;
}
```

The pipeline's overall result is the **highest** task result observed (`Ok < Warn < Abort < Fail`).
A returned `Warn` does not stop the pipeline; `Abort` and `Fail` do, and trigger
rollback.

---

## 9. Putting it together (ASP.NET Core)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPaymentGateway, StripeGateway>();
builder.Services.AddSingleton<IPipelineNameResolver, TenantPipelineNameResolver>();
builder.Services.AddPipelines(typeof(CheckoutPipeline).Assembly);
builder.Services.AddPipelinesFromConfiguration(builder.Configuration.GetSection("Pipelines"));

var app = builder.Build();

app.MapPost("/checkout", (CheckoutArgs args, IPipelineFactory factory) =>
{
    var pipeline = factory.Create<CheckoutArgs>("checkout")
        ?? throw new InvalidOperationException("checkout pipeline is not registered");

    var result = pipeline.Execute(args);
    return result switch
    {
        PipelineResult.Ok   => Results.Ok(args),
        PipelineResult.Warn => Results.Ok(new { args, warning = "completed with warnings" }),
        PipelineResult.Abort => Results.Conflict("checkout aborted"),
        _ => Results.Problem("checkout failed"),
    };
});

app.Run();
```
