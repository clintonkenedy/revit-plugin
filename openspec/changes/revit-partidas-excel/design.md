# Design: Revit partidas + metrado export to Excel

## Technical Approach

Four assemblies, one Revit boundary. `Metrado.Revit2027` alone references the Revit API: reads parameters by `BuiltInParameter`, converts units via `UnitUtils`, emits `ElementTakeoff` DTOs, hosts the export command and shows the completion report. `Metrado.Configuration` turns criteria text into overrides. `Metrado.Domain` holds defaults, merge, chain and openings rule as pure functions. `Metrado.Excel` writes the workbook. Only the Revit assembly needs Windows, so the core rule tests on macOS. I1 is concrete; I2/I3 get extension points; I4 gets a seam, not an implementation.

## Architecture Decisions

| # | Chosen | Rejected | Rationale |
|---|---|---|---|
| 1 Domain TFM | `netstandard2.0;net10.0` | net10-only; nsd2.0-only | Reversal is asymmetric: the second leg costs one `<TargetFrameworks>` line plus an `IsExternalInit` polyfill; retargeting a net10-only Domain for a .NET 8 adapter is a rewrite. "Portability is near-free" holds **only because the JSON parser moved out** (#5): on `netstandard2.0` `System.Text.Json` is a NuGet package pulling `System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`, `System.Text.Encodings.Web`, `Microsoft.Bcl.AsyncInterfaces`. **Cost paid: a fourth project.** Revisit if Domain needs a net10-only BCL API. |
| 1b Excel TFM | `net10.0` | multi-target | ClosedXML formatting may vary per TFM; golden files must assert one output. |
| 2 Tests | xUnit `net10.0` (Domain, Configuration, Excel) + `SmokeCommand` in Revit | NUnit; in-process runner in I1 | `dotnet test` runs on macOS ARM64, where the rule lives. The Revit API cannot be mocked (sealed types, non-constructible `Document`) and no in-process runner is confirmed for 2027/.NET 10, so I1 ships `SmokeCommand`. Automating it is I2 work. |
| 3 Layout | `Metrado.slnx` + `.slnf` | single project; legacy `.sln` | Only `Metrado.Revit2027` needs Windows; a `.slnf` makes that declarative. `dotnet new sln` on SDK 10 emits **`.slnx`**, so the solution is `Metrado.slnx`; a `.slnf` filtering a `.slnx`, and `dotnet test <file>.slnf`, are both verified on 10.0.400/macOS. Write `.slnf` `projects[]` with backslashes (`Lib\Lib.csproj`), the canonical VS form — but 10.0.400/macOS also accepts forward slashes against `.slnx` and `.sln` alike (verified, no MSB4025), so a separator never proves a filter correct. Assert the **project list**. |
| 4 Seam DTO | immutable unit-tagged records; openings never pre-aggregated | Revit types downstream; flat row struct | The DTO is why Domain/Excel build with no Revit installed. Openings stay individual because the rule compares each one; a pre-summed field would make that MUST NOT unenforceable. `MaterialRef?` seats I3 without implementing it. |
| 5 Criteria file + parser home | JSON with comments (`JsonCommentHandling.Skip`, `AllowTrailingCommas`) in `Metrado.Configuration` (`net10.0`) | parser in Domain; YamlDotNet; Tomlyn; XML | `System.Text.Json` is in-box on `net10.0` → zero added closure, decisive given #7; in Domain it would falsify #1. `JsonException.LineNumber`/`BytePositionInLine` give the "failing location" free. `BoundaryMode` gets a converter listing both accepted values on error. A .NET 8 adapter retargets this project only. |
| 5b Excel library | **ClosedXML** (MIT) | EPPlus; bare OpenXML SDK | EPPlus is Polyform Noncommercial since v5 and needs a runtime licence call that throws inside the Revit process — licence trap plus startup failure mode. Bare OpenXML is MIT but verbose enough to hand-roll styling and subtotals. ClosedXML needs no Excel install; its cost is a known closure (#7). |
| 6 Config → rule | validated value objects passed as parameters | Domain reads the file; config singleton | Parsing and merging stay pure, so "malformed file stops the run" *and* "absent file uses defaults" are testable on macOS; path resolution stays in the adapter. `OpeningsThreshold` is valid-by-construction, so the rule needs no validation branch. |
| 7 Closure | `dotnet publish` flat folder + load-time assertion | ILRepack; `build` output | **Counting is inclusive.** ClosedXML's verified closure is 7 transitive packages (`ClosedXML.Parser`, `DocumentFormat.OpenXml`, `.Framework`, `ExcelNumberFormat`, `RBush.Signed`, `SixLabors.Fonts`, `System.IO.Packaging`) → **8 third-party assemblies including `ClosedXML.dll`**. Only `publish` lands them flat. `OnStartup` asserts those **8 assemblies present by simple name** in the add-in folder and names each missing one — the "diagnosable, never swallowed" scenario. Revit refs stay `Private=false`. |
| 8 Transport | git, Windows authoritative | RDP copy; shared folder | One auditable build authority; no unversioned binaries. |

## Configuration Resolution and Defaults

`CriteriaSet.Default` is a built-in constant in Domain, not a file: Walls → area, unit `m2`, ordered sources, threshold with `Defaults.Mode`. **The default mode is `exclusive`, pinned by that named constant and never by enum ordinal**, so reordering `BoundaryMode` cannot silently flip the metrado convention.

Resolution is total — absence is never an error:

| Input | Outcome | `ConfigSource` |
|---|---|---|
| No file (locator returns `null`) | `Ok(CriteriaSet.Default)` | `BuiltInDefaults` |
| File parses | `Ok(Merge(Default, overrides))` | `File` |
| File malformed / unknown category / unsupported unit / negative threshold / invalid mode | `Err(ConfigError)` — run stops, no workbook | — |

`Merge` is per-category then per-field coalesce: a category absent from the file keeps its default entirely; a present category inherits every field left `null` — threshold, mode, unit and sources alike. Unknown category names are rejected before merging, so a typo fails loudly instead of being ignored.

## Data Flow

    Revit Document
         │  (sole Revit API reference)
         ▼
    Metrado.Revit2027   ExportTakeoffCommand : IExternalCommand
      ExtractionService ── UnitUtils.ConvertFromInternalUnits ───┐
      CriteriaFileLocator ─► text | null ─► Metrado.Configuration│ pure
         ▼ ElementTakeoff[]               EffectiveCriteria ◄────┘
    Metrado.Domain
      CodificationChain: AssemblyCode → Keynote → Shared → Rule → Unclassified
      SelectSource (first with a value) ─► Apply(raw, openings[], threshold)
         ▼
      TakeoffResult (capitulo → partida → linea, warnings) + RunReport
         ├──────────────────────────► Metrado.Excel ── ClosedXML ──► .xlsx
         └─► TaskDialog (counts, effective config, warnings) ─► user, workbook not needed

## File Changes

| File | Action | Description |
|---|---|---|
| `src/Metrado.Domain/` | Create | `netstandard2.0;net10.0`, **no third-party deps**. DTOs, defaults, merge, chain, rule, `Result<,>` |
| `src/Metrado.Configuration/` | Create | `net10.0`, `System.Text.Json` (in-box). JSON text → overrides |
| `src/Metrado.Excel/` | Create | `net10.0`, ClosedXML. Workbook writer |
| `src/Metrado.Revit2027/` | Create | `net10.0-windows`, `Nice3point.Revit.Api.*` 2027.2.0. `IExternalApplication`, ribbon, adapter, `ExportTakeoffCommand`, `SmokeCommand`, `Metrado.addin` |
| `tests/Metrado.{Domain,Configuration,Excel}.Tests/` | Create | xUnit; Excel adds golden `.xlsx` fixtures |
| `Metrado.slnx` | Create | All seven — Windows only |
| `Metrado.CrossPlatform.slnf` | Create | Excludes `Metrado.Revit2027` — the macOS loop |
| `openspec/config.yaml` | Modify | **Recommended, NOT applied by this phase** |

## Interfaces / Contracts

```csharp
// One Result arity everywhere; hand-rolled in Domain, which has no third-party deps.
public readonly record struct Result<TValue, TError>
    { public bool IsOk { get; } public TValue Value { get; } public TError Error { get; } }

public readonly record struct Quantity(double Value, QuantityUnit Unit);   // always already converted

public sealed record ElementTakeoff(
    string UniqueId, string CategoryName, string FamilyName, string TypeName,
    string TypeKey,                            // type identity ONLY — not a grouping key
    CodificationReadings Codes,
    IReadOnlyList<RawQuantity> Quantities,     // openings already subtracted by Revit
    IReadOnlyList<OpeningQuantity> Openings);  // individual; NEVER pre-aggregated

public sealed record RawQuantity(string SourceKey, Quantity Amount, MaterialRef? Material = null);
public sealed record OpeningQuantity(string UniqueId, Quantity Amount);
public sealed record MaterialRef(string MaterialId, string MaterialName);   // null = whole element
public sealed record CodificationReadings(string? AssemblyCode, string? Keynote,
    IReadOnlyDictionary<string, string?> SharedParameters);

public readonly record struct PartidaKey(string Capitulo, string PartidaCode);
```

**Grouping key.** A partida is keyed by `PartidaKey` — capitulo plus **the code the chain resolved** — and by nothing else. Two distinct types resolving to `C1010` yield exactly one partida holding every contributing instance as its own linea. `TypeKey` is *not* a grouping key; it attributes type-level parameter reads and lets the writer name contributing types. `SourceKey` stays a string so I2's user-nominated sources need no domain change.

```csharp
public enum BoundaryMode { Exclusive, Inclusive }        // closed domain, no third value
public static class Defaults { public const BoundaryMode Mode = BoundaryMode.Exclusive; }

public readonly record struct OpeningsThreshold {        // valid by construction, unit-carrying
    public double Value { get; } public QuantityUnit Unit { get; } public BoundaryMode Mode { get; }
    public static Result<OpeningsThreshold, ConfigError> TryCreate(double v, QuantityUnit u, BoundaryMode m);
}

public sealed record CategoryCriterion(string Category, QuantityUnit Unit,
    IReadOnlyList<string> Sources, OpeningsThreshold Threshold);
public sealed record CategoryOverride(string Category, QuantityUnit? Unit,
    IReadOnlyList<string>? Sources, double? Threshold, BoundaryMode? Mode);  // null = inherit
public sealed record CriteriaSet(IReadOnlyDictionary<string, CategoryCriterion> ByCategory)
    { public static CriteriaSet Default { get; } }
public enum ConfigSource { BuiltInDefaults, File }
public sealed record EffectiveCriteria(CriteriaSet Criteria, ConfigSource Source, string? Path);

public static Result<CriteriaSet, ConfigError> Merge(CriteriaSet defaults, IReadOnlyList<CategoryOverride> o);
public static Result<EffectiveCriteria, ConfigError> Resolve(string? jsonText, string? path);  // null = no file

public static Quantity? SelectSource(ElementTakeoff e, CategoryCriterion c);  // first with a value; null = none
public static MetradoOutcome Apply(Quantity raw, IReadOnlyList<Quantity> openings, OpeningsThreshold t);

public enum MetradoStatus { Measured, NoSource, UnitMismatch }
public sealed record MetradoOutcome(MetradoStatus Status, MetradoResult? Result, ValidationWarning? Warning);
public sealed record MetradoResult(Quantity Metrado, Quantity Raw, Quantity Gross,
    BoundaryMode AppliedMode, double AppliedThreshold, bool ClampedToGross);

public sealed record RunReport(int ExportedLines, int UnclassifiedCount, int WarningCount,
    ConfigSource ConfigSource, string? ConfigPath, IReadOnlyList<AppliedCriterion> Applied,
    IReadOnlyList<ValidationWarning> Warnings, string WorkbookPath);

public interface ICodeResolver { string? Resolve(ElementTakeoff e); }  // null = no match
```

**No metrado is a status, not a number.** `NoSource` carries `Result = null`, so "measured as 0.0" and "no source yielded a value" are different types, not the same double; `Apply` is unreachable without a selected `Quantity`, so it cannot invent a measured zero. `UnitMismatch` fires when any opening's or the raw quantity's `Unit` differs from the threshold's `Unit`: the comparison is refused rather than performed across unit systems. Domain never converts — conversion stays in the Revit layer.

**`AppliedMode` MUST be written into the workbook**, not merely carried. `ExportTakeoffCommand` shows `RunReport` in a Revit `TaskDialog` on completion — exported total, unclassified count, effective criteria with thresholds and modes, whether no configuration file was found, and the warning list — satisfying "visible to the user without opening the workbook".

The chain is an ordered `IReadOnlyList<ICodeResolver>` ending in `UnclassifiedResolver`, which always succeeds; an absent rule link is an absent list entry, so nothing needs stubbing. **No rule syntax, storage, ordering or UI is designed here.**

## Testing Strategy

| Layer | Where | What |
|---|---|---|
| Unit | macOS | Openings rule (all scenarios, both modes), clamping, `NoSource` vs measured zero, `UnitMismatch`, first-source-wins, chain precedence, whitespace codes, `PartidaKey` collapsing two types into one partida |
| Unit | macOS | Defaults with no file, per-field inheritance, single-category override, loader rejections (malformed, unknown category, negative threshold, invalid mode) |
| Integration | macOS | `EffectiveCriteria` → measurement → `TakeoffResult` + `RunReport` → workbook |
| Golden | macOS | Deterministic ordering, subtotal reconciliation, unclassified block, applied-mode cell, empty workbook |
| Host smoke | Windows/Revit | Isolated load, 8-assembly closure by name, `ASSEMBLY_CODE` under Spanish UI, opening enumeration, document unmodified, completion dialog |

## Threat Matrix

**N/A** — this change introduces no routing, shell, subprocess, VCS/PR automation, executable-file-classification or process-integration boundary. The one adjacent boundary, assembly loading into the Revit host process, has its failure surface (an incomplete dependency closure) covered by Decision 7's named-assembly startup assertion. No rows are applicable, so no threat tasks are generated.

## Migration / Rollout

No data migration; the add-in never writes to the document. Each increment ships as its own PR; rollback is deleting the `.addin`. If `UseRevitContext=False` misbehaves, fall back to bare `DocumentFormat.OpenXml`, shrinking the closure from **8 assemblies to 3** (`DocumentFormat.OpenXml`, `.Framework`, `System.IO.Packaging`) under the same inclusive count.

`openspec/config.yaml` — **recommended, not applied by this phase**: register the seven projects; set `apply.test_command` and `verify.test_command` to `dotnet test Metrado.CrossPlatform.slnf`; set the single `verify.build_command` to `dotnet build Metrado.CrossPlatform.slnf` — the only build that runs on both hosts, since the field holds one string; the Windows-only `dotnet build Metrado.slnx` belongs in tasks. Close the settled `open_questions` (Revit version, Excel library, test framework); re-evaluate `strict_tdd` to `true` once test projects exist.

## Open Questions

- [ ] Does *Area and Volume Computations* gate the quantity parameters the Walls criterion reads? Host-only.
- [ ] `.addin` install path and `ContextName` collision behaviour on Revit 2027. Host-only.
- [ ] Does an in-process NUnit runner exist for Revit 2027/.NET 10? Decides whether I2 automates the smoke layer.
