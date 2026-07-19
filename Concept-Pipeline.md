# Concept: the pipeline

Tempest-the-DSL is a three-stage compiler. All three stages already exist tangled inside
`TempestGenerator.cs` — this is an extraction, not an invention.

```
input: a C# class   ─┐
input: a .razor file ┴→  PARSE  →  MODEL  →  EMIT  →  host compiles it
```

| Stage | Library | What it is |
|---|---|---|
| Model | `Tempest.Model` | The DSL's vocabulary as plain records, zero dependencies — two worlds with a clear boundary. `Tempest.Model.Entry`: the entry point — members exactly as frontends read them (as written, possibly invalid, with spans). Root namespace: the validated internal representation the tool works with — what a component *is*. |
| Parse | `Tempest.Parsing` + frontends | Behavior and contract only — no data definitions. Frontends — `Tempest.RazorParser` (text parser for `.razor` `@code` blocks), a Roslyn-symbol parser for C# classes *(next)* — implement `IComponentParser<TSource>` and produce `Tempest.Model.Entry` records. `ComponentAssembler` is the boundary crossing: it runs the shape rules and folds entries into internal `ComponentModel`s + `DiagnosticModel`s; nothing invalid reaches the internal model by construction. |
| Emit | `Tempest.Emit` *(later)* | Model → C# source text. **One emitter, not one per UI framework** — see below. |
| Shell | `Tempest.Generators` | Thin Roslyn incremental wiring: calls Parse, hands the model to Emit, maps model diagnostics back to Roslyn. |

## The decisions

- **The model is the product.** Records with value equality (Roslyn's incremental caching
  depends on it), a neutral `SourceSpan` instead of Roslyn's `Location`, diagnostics as
  data (`DiagnosticModel`) instead of direct Roslyn calls. The whole pipeline becomes
  testable as: text in → records out → string out. No compiler harness.
- **Emission is framework-neutral.** The emitted twin code references only Tempest.Core
  types plus a small protected surface every host base agrees to provide
  (`RegisterTempestHandlers`, `SubscribeEvent`, `Dispatch`). Blazor's `StatefulComponent`
  implements that surface with `InvokeAsync`/`StateHasChanged`; a WinUI base implements it
  with `DispatcherQueue`. Same emitted text compiles inside either. Framework-specifics
  live in host base classes at *runtime*, never in generated code. The emitter only forks
  if a host genuinely cannot conform to the shared surface.
- **Pipeline libraries target net10.0.** One consequence to resolve at step 6: an assembly
  the Roslyn generator DLL references in-process must be netstandard2.0, so either the
  shell re-targets/links the pipeline code at packaging time, or the generator invokes the
  pipeline another way. Decide when we get there.

## Steps (each keeps the build green)

- [x] 1. Names: `Tempest.Model` / `Tempest.Parsing` / `Tempest.Emit`.
- [x] 2. **`Tempest.Model`**: `ComponentModel`, `MethodModel` (commands & events),
      `ReactiveModel`, `HookModel`, `SourceSpan`, `DiagnosticModel`, `EquatableArray<T>`.
- [x] 3. **`Tempest.RazorParser`**: extract the razor text parser (brace matcher, string
      skipper — the hairiest code in the repo) → first unit tests ever on it.
      *(The generator keeps its private copy until step 6 — netstandard2.0 can't
      reference the net10.0 pipeline in-process; see the note above.)*
- [ ] 4. Extract the C#-symbol parser into its own library, both frontends behind
      `Tempest.Parsing`'s `IComponentParser<TSource>`.
- [ ] 5. **`Tempest.Emit`**: extract `GenerateComponent` as `model → string` → snapshot tests.
- [ ] 6. Slim `Tempest.Generators` to orchestration; pack the pipeline DLLs into the package.
