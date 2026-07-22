# Tempest — library layout rules

A library is three things: `Contracts/` for the data that crosses its boundary,
`Errors/` for its failures, and everything else at the project root — the
public surface and the machinery behind it, side by side. A processing stage
big enough to own types gets its own folder.

## `Contracts/` — data, nothing but data

DTO POCOs only: records, enums, and value types with no behavior, no validation,
no dependencies. They are the shapes that cross a boundary — what a parser hands
to the compiler, what one pipeline stage hands to the next — so both sides
reference them and neither owns them.

- Value equality throughout (records, `EquatableArray<T>`), so incremental
  caching and test assertions compare whole results directly.
- Data as-observed, never as-judged: `SourceMethod` carries an invalid shape
  rather than refusing to represent it; validation is a downstream concern.
- No methods beyond trivial conveniences (`Empty`, `IsNone`).

Examples: `Tempest.Parsing/Contracts/` (`TempestDocument` — the full parsed
document; `SourceMethod`, `SourceReactiveProperty`, `SourceHook` — members as
read; `HostKind`, `ReturnKind`, `ResolveResult`), `Tempest.Abstract/Contracts/`
(`SourceSpan`, `EquatableArray<T>`).

## The root — the surface and its machinery

Everything that isn't boundary data lives at the project root: the `public`
types that are the library's exposed surface (interfaces frontends implement,
entry points hosts call) next to the `internal` services behind them.

The surface:

- Public types are few, documented, stable; everything else is `internal`.
- **Public services can only return contract data.** Every return type is a
  `Contracts/` type (or a primitive/collection of them) — never an internal
  type, never a live object with behavior.
- **Public services never throw.** A fallible call returns `Result<T>` — the
  value, or an `IError` explaining why there isn't one (see `Errors/`).

The machinery:

- **Small, single-purpose services over grab-bag helpers.** A cluster of
  related helpers is its own `*Service` class (`SanitizeService`, not a
  `Helpers` region); orchestrators call services, they don't accumulate them.
- **Services are sealed instance classes, composed not called-statically.** An
  orchestrator holds its services as `private readonly X _x = new();` fields
  (or takes seams via constructor) and calls `_x.Method(...)` — never
  `SomeService.Method(...)` from another class. `static` *state* is reserved
  for genuine process-wide singletons and constants, kept as private fields
  inside the service that owns them.
- **Private is earned by locality.** A helper, tracker, or method used by
  exactly one class is `private` (Foresight-style); the moment a second class
  needs it, it widens to `internal` — or splits into its own service. Shared
  machinery is never `private`: the library, not the class, is the visibility
  boundary for anything two classes touch.
- **Inside a service, capabilities are parameters (the Zig rule).** Machinery
  helpers are static (private when only their class uses them, which is the
  norm); what one may do is visible in its signature: a tracker by ref means it
  may reposition (`CharTracker`, `TokenTracker`), the tokenized document means
  it may allocate slices of the source, a bare span means it may only look.
  Cursor state lives in small tracker structs nested inside the service that
  owns them — a tracker is machinery with exactly one owner. Text is allocated
  only through an explicit `ToSlice`-style call at the point of need.
- **A processing stage big enough to own types gets its own folder.** When a
  cluster of machinery is a stage with its own data (a tokenizer and its
  tokens, a parser and its nodes), it lives in a stage folder — `Tokenizer/`,
  `Parser/Nodes/` — holding both the service and the types that exist only for
  it.
- **Internal data records are top-level files** at the root (or in their stage
  folder) — never nested private types; a type worth declaring is worth a
  file. Promotion path: an internal record a public signature wants to expose
  becomes a Contract; the move is the review.
- Nothing outside the library may depend on internals (no `InternalsVisibleTo`
  escape hatches for consumers — tests only).

Examples: `Tempest.RazorParser/` root — `RazorParser` (the public frontend),
`RazorEntryReader` (the orchestrator), `CSharpSyntaxService`, `SanitizeService`,
`HookResolutionService`, `HostResolutionService`, `ParseCacheService`, and the
internal records `HookCandidate` + `CachedParse`; stage folders `Tokenizer/`
(`RazorTokenizer`, `RazorToken`, `TokenizedDocument`) and `Parser/`
(`RazorMarkupParser`, `Nodes/`). `Tempest.Parsing/` root — `IComponentParser`,
`ISourceResolver`, `SourceRegistry`.

## `Errors/` — failures as data

We don't do exceptions; we do result-type errors. A fallible public call
returns `Result<T>` carrying either the value or an `IError` — both defined in
`Tempest.Pipeline`, the shared library every pipeline stage references. Not in
`Tempest.Abstract`, because Abstract is the runtime package users reference and error
plumbing is pipeline-internal; not in `Tempest.Parsing`, because errors are
common to every stage, not just parsing. Each library's `Errors/` folder holds
its concrete error records.

- Records of strings and value types only — an error is contract data and
  cache-compares by value like everything else. An exception caught at the
  boundary is flattened to its type name and message, never carried live.
- Stable per-library code prefix (`RZP…` for the razor parser, `PRS…` for
  parsing), so the shell can report errors without knowing concrete types.
- Judgeable user-code conditions are NOT errors: an invalid `[Reactive]` shape
  is entry data the compiler diagnoses. Errors are for broken inputs and broken
  invariants — states the contracts cannot represent.
- Errors fail the call; findings that shouldn't (warnings, info) go through the
  `IDiagnostics` side-channel (`Tempest.Pipeline`) and the call still succeeds.
- `Result`/`Result<T>` are readonly record structs: `IsSuccess` proves `Value`
  through nullable analysis, and a plain value converts implicitly to a success.

Examples: `Tempest.RazorParser/Errors/` (`InvalidRazorSourceError` — the shell
handed in a `RazorSource` breaking its own contract; `RazorEngineError` — the
parse machinery failed unexpectedly), `Tempest.Parsing/Errors/`
(`DuplicateComponentError` — one component name registered twice).

## The dependency rule

- `Contracts` depends on nothing (within `Tempest.Abstract`, only on other
  contracts).
- Root types may use `Contracts`, `Errors`, and each other; only `public` root
  types and `Contracts` are visible outside the library.
- Public signatures expose only `Contracts` types, wrapped in `Result<T>` when
  fallible.

## Why

The pipeline is a chain of independent, testable stages (frontends → compiler →
emitter). Contracts make each seam a plain-value boundary that caches and
compares by equality; the public root types make each stage swappable behind
one small interface; everything else stays internal and free to evolve. A
reader can tell a type's blast radius from where it lives.
