# Tempest

Component-owned state for Blazor. Three attributes and a bus: `[Command]`, `[Reactive]`,
`[Event]` — a source generator emits one state twin per member.

## Architecture

Tempest-the-DSL is a small compiler. Every pipeline stage is its own library with a
single job; data crosses stages as plain records with value equality.

```
input: a C# class   ─┐
input: a .razor file ┴→  PARSE  →  ASSEMBLE  →  EMIT  →  host compiles it
```

| Library | Role |
|---|---|
| `Tempest.Model` | All pipeline data, zero dependencies. Two worlds with a clear boundary: `Tempest.Model.Entry` — members exactly as frontends read them (as written, possibly invalid, with spans); root namespace — the validated internal representation the tool works with. |
| `Tempest.Parsing` | The frontend contract: `IComponentParser<TSource>` — one source in, its entries out. |
| `Tempest.RazorParser` | Parse frontend for `.razor` files: text parser for `@code` blocks (the Razor compiler's output is invisible to other generators). |
| `Tempest.CSharpParser` | Parse frontend for ordinary C# classes, over Roslyn symbols. |
| `Tempest.Assembler` | The boundary crossing: dedupes, groups by component, runs the shape rules, and folds entries into `ComponentModel`s + `DiagnosticModel`s. Nothing invalid reaches the internal model by construction. |
| `Tempest.Emit` | `ComponentModel` → generated C# source text. One emitter, framework-neutral: emitted code references only `Tempest.Core` plus a small host-base surface. |
| `Tempest.Generators` | Thin Roslyn incremental shell: wires the pipeline into the compiler, maps model diagnostics onto Roslyn diagnostics. |
| `Tempest.Core` | Runtime, platform-neutral: the attributes, `CommandState`/`ReactiveState` families, `IEventBus`, `ITempestComponent`. |
| `Tempest.Blazor` | Blazor host: `StatefulComponent`/`StatefulLayoutComponent` bases, the event bus, DI. Ships the generator in its NuGet package. |

Conventions: engine classes are instance `sealed class`es with one public verb method
returning a domain record; boundary types live with their stage; diagnostics are data
(not an injected sink) because Roslyn incremental caching needs value-equatable outputs;
hard rules live in exactly one place — the assembler.
