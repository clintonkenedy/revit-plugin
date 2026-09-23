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
| 7 Closure | `dotnet publish` flat folder + load-time assertion | ILRepack; `build` output | **Counting is inclusive.** ClosedXML's verified closure is 7 transitive packages (`ClosedXML.Parser`, `DocumentFormat.OpenXml`, `.Framework`, `ExcelNumberFormat`, `RBush.Signed`, `SixLabors.Fonts`, `System.IO.Packaging`) → **8 third-party assemblies including `ClosedXML.dll`**. Only `publish` lands them flat. `OnStartup` asserts those **8 assemblies present by simple name** — assembly file names, not package names: `RBush.Signed` ships `RBush.dll` — **plus Metrado's own `Configuration`, `Domain` and `Excel`**, which the add-in equally does not need to start, in the add-in folder and names each missing one — the "diagnosable, never swallowed" scenario. Revit refs stay `Private=false`. |
| 8 Transport | git, Windows authoritative | RDP copy; shared folder | One auditable build authority; no unversioned binaries. |

## Configuration Resolution and Defaults

`CriteriaSet.Default` is a built-in constant in Domain, not a file: Walls → area, unit `m2`, ordered sources, threshold `Defaults.AreaThresholdSquareMetres` with `Defaults.Mode`. **The default mode is `exclusive`, pinned by that named constant and never by enum ordinal**, so reordering `BoundaryMode` cannot silently flip the metrado convention. The threshold value is named for the same reason: it decides which openings are added back, so it decides the budget.

The locator reports **three** states, not text-or-null (residual finding N3). Null has room for two facts and the locator has three, so "the file is there and could not be read" — permissions, an exclusive lock, a failing disk — would have to borrow null from "there is no file". Those two demand opposite responses: absence must fall back to the defaults, and a file that was supplied but could not be honoured must stop the run. One representation forces the run to pick one and be wrong about the other, invisibly, since the export still succeeds.

Absence is never an error; a supplied file is never ignored:

| `CriteriaFileLookup` | Outcome | `ConfigSource` |
|---|---|---|
| `Absent` — nothing was there to read | `Ok(CriteriaSet.Default)` | `BuiltInDefaults` |
| `Found(text)` and it parses | `Ok(Merge(Default, overrides))` | `File` |
| `Found(text)` and malformed / unknown category / unsupported unit / negative threshold / invalid mode | `Err(ConfigError)` — run stops, no workbook | — |
| `Unreadable(ConfigError)` — the file exists and I/O or permissions refused it | `Err(ConfigError)`, the locator's own, naming the file — run stops, no workbook | — |

**I1 staging.** The criteria-file requirement is tagged I2 and its reader is task 2.1, so I1 ships no parser and the `Found` row above has no implementation yet. I1 therefore answers `Found` with `Err(ConfigError)` naming the file and stating that this version cannot read criteria files — because the specification's "MUST NOT silently fall back to defaults when a file was supplied" is unconditional and does not ask *why* the file could not be honoured. That error carries no `ConfigLocation`: nothing was parsed, and reporting line 0 would send the estimator hunting a syntax error in a file that is probably valid. Task 2.1 replaces that one branch.

`EffectiveCriteria` keeps `Source` and `Path` in agreement by construction. A `File` run must name its file, and `BuiltInDefaults` must not carry one — otherwise the completion report reads "criteria from criteria.json" for a run that honoured no file, which is the silent fallback wearing a filename. The path a caller probed is a different fact from the path in force, and resolution drops it on the `Absent` branch.

`Merge` is per-category then per-field coalesce: a category absent from the file keeps its default entirely; a present category inherits every field left `null` — threshold, mode, unit and sources alike. Unknown category names are rejected before merging, so a typo fails loudly instead of being ignored.

## Data Flow

    Revit Document
         │  (sole Revit API reference)
         ▼
    Metrado.Revit2027   ExportTakeoffCommand : IExternalCommand
      ExtractionService ── UnitUtils.ConvertFromInternalUnits ──────────┐
      CriteriaFileLocator ─► CriteriaFileLookup ─► Metrado.Configuration│ pure
                             (Found | Absent | Unreadable)              │
         ▼ ElementTakeoff[]                     EffectiveCriteria ◄─────┘
    Metrado.Domain
      CodificationChain: AssemblyCode → Keynote → Shared → Rule → Unclassified
      Measure = SelectSource (first with a value) ─► Apply(element, raw, openings[], threshold)
         ▼
      TakeoffResult (capitulo → partida → linea, warnings) + RunReport
         ├──────────────────────────► Metrado.Excel ── ClosedXML ──► .xlsx
         └─► TaskDialog (counts, effective config, warnings) ─► user, workbook not needed

## File Changes

| File | Action | Description |
|---|---|---|
| `src/Metrado.Domain/` | Create | `netstandard2.0;net10.0`, **no third-party deps**. DTOs, defaults, merge, chain, rule, `Result<,>` |
| `src/Metrado.Configuration/` | Create | `net10.0`, `System.Text.Json` (in-box). Owns resolution (`CriteriaResolver.Resolve`): `CriteriaFileLookup` → `EffectiveCriteria`, and JSON text → overrides |
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
// Members are numbered from 1 throughout, so the zero every C# enum field can reach
// names no state and an unset value is detectable instead of silently meaning the
// first member. This is what makes "never by enum ordinal" enforceable rather than
// merely asserted.
public enum BoundaryMode { Exclusive = 1, Inclusive = 2 }   // closed domain, no third value
public static class Defaults {
    public const BoundaryMode Mode = BoundaryMode.Exclusive;
    public const double AreaThresholdSquareMetres = 1.0;    // the default area threshold, named
}

public readonly record struct OpeningsThreshold {        // valid by construction, unit-carrying
    public double Value { get; } public QuantityUnit Unit { get; } public BoundaryMode Mode { get; }
    public static Result<OpeningsThreshold, ConfigError> TryCreate(
        double v, QuantityUnit u, BoundaryMode m, string? category = null);  // named in the error
}

public sealed record CategoryCriterion(string Category, QuantityUnit Unit,
    IReadOnlyList<string> Sources, OpeningsThreshold Threshold);
public sealed record CategoryOverride(string Category, QuantityUnit? Unit,
    IReadOnlyList<string>? Sources, double? Threshold, BoundaryMode? Mode);  // null = inherit
public sealed record CriteriaSet(IReadOnlyDictionary<string, CategoryCriterion> ByCategory)
    { public static CriteriaSet Default { get; } }
public enum ConfigSource { BuiltInDefaults = 1, File = 2 }

// Source and Path are consistent by construction: File must name its file,
// BuiltInDefaults must not carry one. See Configuration Resolution above.
public sealed record EffectiveCriteria(CriteriaSet Criteria, ConfigSource Source, string? Path);

// The locator's three states. Match is the only way to the text and to the error,
// so three states cannot be sorted back into two on the way out — and the type
// deliberately exposes no discriminator, because any single bool would partition
// three into two and re-open N3 in one honest-looking line.
public sealed class CriteriaFileLookup {
    public static CriteriaFileLookup Found(string text);          // empty/whitespace allowed; null is not a reading
    public static CriteriaFileLookup Absent { get; }
    public static CriteriaFileLookup Unreadable(ConfigError e);   // error required: it must name the file
    public T Match<T>(Func<string, T> found, Func<T> absent, Func<ConfigError, T> unreadable);
}

// Two states, so a measured 0.0 cannot impersonate "no source had a value".
// Match is the only route to the quantity, which is what makes Apply unreachable
// without one — Quantity? would not, because GetValueOrDefault() manufactures 0.0.
public sealed class SourceSelection {
    public static SourceSelection None { get; } public static SourceSelection Of(Quantity q);
    public bool HasQuantity { get; } public T Match<T>(Func<Quantity, T> selected, Func<T> none);
}

// The functions above are owned as set out in the Function Ownership table below.
public enum MetradoStatus { Measured = 1, UnitMismatch = 2, NoSource = 3 }  // I2 adds Counted (N1)
public sealed record MetradoOutcome(MetradoStatus Status, MetradoResult? Result, ValidationWarning? Warning);
public sealed record MetradoResult(Quantity Metrado, Quantity Raw, Quantity Gross,
    BoundaryMode AppliedMode, double AppliedThreshold, bool ClampedToGross);

// Staged deliberately. WarningCount and NoMeasurableElements are DERIVED, never
// stored: a count carried beside the list it counts is a second home for one fact,
// and the two only ever diverge in the direction that under-reports. ConfigSource /
// ConfigPath arrive with the completion dialog that reads them (1.24) and
// WorkbookPath with the writer that produces it (1.16) — a field no producer can
// fill yet would have to be defaulted, and a defaulted provenance is a claim.
public sealed record RunReport(int ExportedLines, int UnclassifiedCount,
    IReadOnlyList<AppliedCriterion> Applied, IReadOnlyList<ValidationWarning> Warnings)
{
    public bool NoMeasurableElements { get; }   // ExportedLines == 0
    public int WarningCount { get; }            // Warnings.Count
}

public interface ICodeResolver { string? Resolve(ElementTakeoff e); }  // null = no match
```

**Function ownership** (residual finding N4 — the interface listing above no longer said which of the four projects owns which function):

| Owner | Function |
|---|---|
| `Metrado.Domain` | `CriteriaSet.Merge(CriteriaSet defaults, IReadOnlyList<CategoryOverride> o) → Result<CriteriaSet, ConfigError>` |
| `Metrado.Domain` | `Measurement.SelectSource(ElementTakeoff e, CategoryCriterion c) → SourceSelection` |
| `Metrado.Domain` | `Measurement.Apply(ElementTakeoff e, Quantity raw, IReadOnlyList<Quantity> openings, OpeningsThreshold t) → MetradoOutcome` |
| `Metrado.Domain` | `Measurement.Measure(ElementTakeoff e, CategoryCriterion c) → MetradoOutcome` — the composition callers use |
| **`Metrado.Configuration`** | `CriteriaResolver.Resolve(CriteriaFileLookup lookup, string? path) → Result<EffectiveCriteria, ConfigError>` |

Resolution belongs to `Metrado.Configuration` because it ends in reading a criteria file and the JSON reader lives there (decision 5). The value types it produces stay in Domain: `RunReport` reports the provenance, and Domain cannot reference this assembly — so `ConfigSource` and `EffectiveCriteria` could not live anywhere else without duplicating the enum.

**No metrado is a status, not a number.** `NoSource` carries `Result = null`, so "measured as 0.0" and "no source yielded a value" are different types, not the same double; `Apply` is unreachable without a selected `Quantity`, so it cannot invent a measured zero. `Measurement.Measure` is the composition that keeps that guarantee: it reaches `Apply` only from inside `SourceSelection.Match`'s selected branch, so there is no expression of type `Quantity` derivable from an unmeasured element. `UnitMismatch` fires when any opening's or the raw quantity's `Unit` differs from the threshold's `Unit`: the comparison is refused rather than performed across unit systems. Domain never converts — conversion stays in the Revit layer.

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
- [ ] `.addin` install path and `ContextName` collision behaviour on Revit 2027. Host-only. **Partly answered (2026-09-22):** the per-user path `%AppData%\Autodesk\Revit\Addins\2027\` loads, with the assembly in a `Metrado\` subfolder resolved relative to the manifest; Revit registers that folder for context `METRADO.REVIT2027` (the `ContextName`, upper-cased). Collision behaviour is still untested.
- [ ] Does an in-process NUnit runner exist for Revit 2027/.NET 10? Decides whether I2 automates the smoke layer.
