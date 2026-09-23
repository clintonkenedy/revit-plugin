# Design: Revit partidas + metrado export to Excel

## Technical Approach

Four assemblies, one Revit boundary. `Metrado.Revit2027` alone references the Revit API: reads parameters by `BuiltInParameter`, converts units via `UnitUtils`, emits `ElementTakeoff` DTOs, hosts the export command and shows the completion report. `Metrado.Configuration` turns criteria text into overrides. `Metrado.Domain` holds defaults, merge, chain and openings rule as pure functions. `Metrado.Excel` writes the workbook. Only the Revit assembly needs Windows, so the core rule tests on macOS. I1 is concrete; I2/I3 get extension points; I4 gets a seam, not an implementation.

## Architecture Decisions

| # | Chosen | Rejected | Rationale |
|---|---|---|---|
| 1 Domain TFM | `netstandard2.0;net10.0` | net10-only; nsd2.0-only | Reversal is asymmetric: the second leg costs one `<TargetFrameworks>` line plus an `IsExternalInit` polyfill; retargeting a net10-only Domain for a .NET 8 adapter is a rewrite. "Portability is near-free" holds **only because the JSON parser moved out** (#5): on `netstandard2.0` `System.Text.Json` is a NuGet package pulling `System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`, `System.Text.Encodings.Web`, `Microsoft.Bcl.AsyncInterfaces`. **Cost paid: a fourth project.** Revisit if Domain needs a net10-only BCL API. |
| 1b Excel TFM | `net10.0` | multi-target | ClosedXML formatting may vary per TFM; golden files must assert one output. |
| 2 Tests | xUnit `net10.0` (Domain, Configuration, Excel) + `SmokeCommand` in Revit | NUnit; in-process runner in I1 | `dotnet test` runs on macOS ARM64, where the rule lives. The Revit API cannot be mocked (sealed types, non-constructible `Document`) and no in-process runner was confirmed for 2027/.NET 10 when this was decided, so I1 ships `SmokeCommand`, run by the development harness. Two runners now claim 2027/.NET 10 support (Open Questions); whether one automates the smoke layer is I2 work (2.8). |
| 3 Layout | `Metrado.slnx` + `.slnf` | single project; legacy `.sln` | Only `Metrado.Revit2027` needs Windows; a `.slnf` makes that declarative. **Amended (PR 15): it needs Windows to *run*, not to *build*.** On macOS ARM64 / SDK 10.0.400 the project built with 0 warnings, including a probe calling real API surface (`UnitUtils.ConvertFromInternalUnits`, `UnitTypeId.SquareMeters`, `UIApplication`): the API comes from NuGet reference assemblies and `net10.0-windows` without WPF/WinForms cross-compiles. That was verified before the host shell existed and has not been re-run on macOS since. `Metrado.Revit2027.Tests` is different: it references the Windows desktop runtime, so it needs Windows to run. The filter still leaves both out; whether the add-in should join a macOS build loop is an open decision, not settled here. The `[mac]`/`[win]` tags therefore mark where a task can be **proved**, not where it can be compiled. `dotnet new sln` on SDK 10 emits **`.slnx`**, so the solution is `Metrado.slnx`; a `.slnf` filtering a `.slnx`, and `dotnet test <file>.slnf`, are both verified on 10.0.400/macOS. Write `.slnf` `projects[]` with backslashes (`Lib\Lib.csproj`), the canonical VS form — but 10.0.400/macOS also accepts forward slashes against `.slnx` and `.sln` alike (verified, no MSB4025), so a separator never proves a filter correct. Assert the **project list**. |
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

**Where the files live (decided by the user, PR 17).** The criteria file is `metrado.criteria.json` **beside the model**, so criteria are versioned and shared with the project they price. The workbook is written **beside the model too, and never over an existing file**: `<model> - metrado <yyyy-MM-dd HHmm>.xlsx`, with `(2)`, `(3)` on a clash, because estimators fill unit prices into the exported copy. **"The model" is the file the estimator opened, a workshared local copy included.** The first review of PR 17 moved the export beside the central model; the second showed that did more harm than good and it was undone: the central's path is recorded inside the `.rvt` and travels with every copy, so a model received from another firm pointed the export at that firm's folder, a local copy opened offline could not export at all, and two copies exporting in the same minute raced for one name in the central's folder. The limit this leaves: a criteria file kept only beside the central is not read, and the dialog, which names the folder it wrote to, says no criteria file was found. A model never saved is asked to be saved; a cloud model has no folder on disk and is told to copy the model to one (a workshared one opened detached first), because saving again never changes the answer. Either way nothing is written. **Files reach the folder by rename:** before the model is read, the workbook is reserved under a temporary name in the model's folder and renamed once there, which proves the folder allows what the commit needs (creating a file, and a rename, which deletes the old name); a folder that refuses either stops the export at once. The files are written, flushed to the disk, and renamed into place only when complete, the rename never replacing an existing file (the name's race guard; `FileMode.CreateNew` guards only the temporary names). A file failure (a folder that refuses, a full disk, a name taken meanwhile) leaves nothing under either name and says so in Metrado's own dialog; a folder that lets files be created but not deleted keeps one empty temporary file, which it lets no one remove. Any other failure, such as a write refusal during the model read, still ends in Revit's own failure dialog, with nothing written.

**The built-in criteria (task 2.2, PR 20).** One per category the add-in measures (extraction reads walls only until task 2.6); every field can be overridden per category by the criteria file. The completion dialog lists all six, a zero exclusive threshold as "every opening is deducted".

| Category | Unit | Sources | Threshold | Mode |
|---|---|---|---|---|
| Walls | m2 | `HOST_AREA_COMPUTED` | 1.0 m2 | exclusive |
| Floors | m2 | `HOST_AREA_COMPUTED` | 1.0 m2 | exclusive |
| Roofs | m2 | `HOST_AREA_COMPUTED` | 1.0 m2 | exclusive |
| Railings | m | `CURVE_ELEM_LENGTH` | 0 m | exclusive |
| Doors | u | none (counted, N1) | 0 u | exclusive |
| Windows | u | none (counted, N1) | 0 u | exclusive |

Railings are measured in linear metres because that is how a metrado states them ("ml"), so the closed set of units gained the metre, written `m`. The specifications fix no unit for railings; a file can still set another. Doors and windows read no quantity source: the empty list is N1's counted category, and until task 2.3 they are reported as having no source rather than counted. Where no opening is ever added back the threshold is zero. The sources for floors, roofs and railings are the parameters' built-in names; that each yields the expected value in a real model is task 2.6's host evidence, not this table's.

**The criteria file's shape (task 2.1, PR 19).** One JSON object, comments and trailing commas allowed, with one entry per category keyed by the category's name; every field is optional and a field left out inherits the built-in criterion's:

```jsonc
{
  // Walls: openings under 1 m2 stay in the metrado
  "Walls": { "unit": "m2", "sources": ["HOST_AREA_COMPUTED"], "threshold": 1.0, "mode": "exclusive" },
}
```

`unit` is one of the unit symbols (`m2`, `m3`, `u`, `m`); `sources` is the ordered list of quantity sources, where an empty list is a choice and never "left out" (it declares a counted category once N1 lands, task 2.3); `threshold` is a number in the category's unit; `mode` is exactly `exclusive` or `inclusive`. Nothing in the file is ignored: an unknown or repeated field, a value of the wrong kind and text after the closing brace are refused, because a misspelt `"treshold"` read as nothing would leave its category on the default without a word. `CriteriaFile` refuses what the file's shape shows; `CriteriaSet.Merge` refuses what only the product's criteria can judge (an unsupported or repeated category, a negative threshold). Either way the run stops as "The criteria file '<path>', line N, position M: ...", naming the category and the value; lines and positions count from 1, positions in characters as an editor shows them. Comments may go wherever whitespace between values may, but not between a name and its colon, where the JSON reader refuses them. A `\u` escape for half of a surrogate pair is refused like any other entry, at its place.

## Opening Measurement (decided in PR 16, on host evidence)

**The constraint.** The rule compares each opening's own quantity, so the adapter must say how much area Revit subtracted for each insert. No read-only Revit API returns that (Autodesk KB, Dec 2025: "Currently it is not possible to extract the opening areas of a calculated area from walls in Revit"). The exact method — delete the insert, regenerate, read, roll back — needs a transaction, which `TransactionMode.ReadOnly` forbids. Every measured opening is therefore an **outline standing in for the deduction**.

**The decision.** A family instance's outline comes from `ExporterIFCUtils.GetInstanceCutoutFromWall` and its area from `ComputeAreaOfCurveLoops` (compile-only reference `Nice3point.Revit.Api.RevitAPIIFC` 2027.2.0); a rectangular `Opening` uses `BoundaryRect`, its width taken along the wall. `OpeningPolicy` — pure, unit-tested — decides whether each outline may stand for the deduction. When it may not, the opening becomes a `ValidationWarning` naming it and the reason, **and its deduction stands**: an untrusted outline is never used as a number. A trusted outline can still differ from the deduction by a small amount (see *Evidence*), and because the rule is a threshold, that difference matters near it (see *Threshold band*).

An insert is **not an opening of the wall** unless it generates the wall's faces (`GetGeneratingElementIds`); hosts list inserts shared through joins that cut nothing. `Opening` elements count as cuts regardless, since one removed 11.4 m2 without being named by any face. An opening is **reported, not measured**, when it is a void cut, an embedded wall, of an unknown kind, hosted by another wall (a shadow cut through a join), without a computable outline or area, reaching beyond the wall solid's range, overlapping any other cut in the wall — another insert, or a joined floor, beam or column located by the faces it generates on the wall — since Revit deducts the union once, or in a wall with a condition that breaks outlines: not straight, slanted or tapered (`Wall.CrossSection`), sweeps or reveals in the type or across it, an edited profile, or an attached top or base. A write refused by Revit during the read-only command fails the run instead of becoming a reported opening.

Converted quantities are rounded to 1e-9 m2 at the seam, so the feet round trip's noise (1.0 m2 arriving as 0.9999999999999999) cannot flip the exact-threshold case.

**Evidence** (`tools/Metrado.HostHarness`, mode `probe-walls`: each insert deleted, regenerated, read and rolled back; the model never saved; walls chosen by Metrado's own `WallReader.Walls`, narrowed to straight walls with inserts). Metrado's `WallReader` on the Snowdon Towers sample (60 walls): **102 openings matching Revit's deduction to 1e-6 m2, none overstated, none understated**, 135 reported; on the Pacific Continental sample (45 walls): 28 exact, one overstated by 2.2e-6 m2 (geometric noise), six understated by at most 1e-4 m2, 30 reported. On both, **no insert that deducted area was left unmeasured and unreported**. Two decisions were reversed on this evidence and on review. Without joined cuts in the overlap check, eight openings in Snowdon's "Solar Wall" walls came out 0.03 m2 short; they overlap joined cuts and are now reported. Dropping the attached condition had measured 68 more Pacific openings, all within 1e-4 m2, but a wall attached to a pitched roof (a gable) can hold an outline inside its range that crosses its real edge, and neither sample has one; the condition is back.

**Threshold band — a requirement on task 1.24.** The rule adds an opening back when its quantity is below the threshold, so an outline short by ε flips an opening whose true deduction lies within ε above the threshold: added back whole where it should stay deducted. Measured outlines were within 1e-4 m2 on both samples, but that bound is empirical. The completion report must therefore flag every outline-measured opening whose quantity lies within a band of its category's threshold as possibly misclassified, without changing the number. The band is set from the probe evidence and re-checked when the probe is re-run.

**Limits that stay open.** Holes drawn with Edit Profile and reveals with no insert element are never detected as openings: they stay deducted, silently, unless an insert in the same wall triggers a condition. Containment compares ranges, not the wall's real edge; the conditions above cover the known non-rectangular walls. All phases are read, so a demolished wall and the infill Revit creates for a demolished insert are both measured — a product decision still owed. Stacked-wall parents and curtain walls are outside I1; basic walls, stacked members included, are read. The probe is the regression instrument: re-run it after every Revit update and on the first real project.

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
// and the two only ever diverge in the direction that under-reports. Provenance
// and the workbook's path never joined it: the completion dialog (1.24) reads
// ConfigSource and the file's path from the EffectiveCriteria the run was measured
// under, and the workbook's path from the command that chose it. Copying either
// here would be that second home again.
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

Resolution belongs to `Metrado.Configuration` because it ends in reading a criteria file and the JSON reader lives there (decision 5). The value types it produces stay in Domain, beside the `CriteriaFileLookup` it consumes: the host names them too (the completion dialog takes the provenance from `EffectiveCriteria`), and in Domain every layer can, without referencing the JSON reader.

**No metrado is a status, not a number.** `NoSource` carries `Result = null`, so "measured as 0.0" and "no source yielded a value" are different types, not the same double; `Apply` is unreachable without a selected `Quantity`, so it cannot invent a measured zero. `Measurement.Measure` is the composition that keeps that guarantee: it reaches `Apply` only from inside `SourceSelection.Match`'s selected branch, so there is no expression of type `Quantity` derivable from an unmeasured element. `UnitMismatch` fires when any opening's or the raw quantity's `Unit` differs from the threshold's `Unit`: the comparison is refused rather than performed across unit systems. Domain never converts — conversion stays in the Revit layer.

**`AppliedMode` MUST be written into the workbook**, not merely carried. `ExportTakeoffCommand` shows `RunReport` in a Revit `TaskDialog` on completion — exported total, unclassified count, effective criteria with thresholds and modes, whether no configuration file was found, and the warning list — satisfying "visible to the user without opening the workbook". The dialog's line count uses the budget sheet's phrase for the budget sheet's number (coded lines), and names the unclassified elements apart. **The full warning list is also kept beside the workbook** as `<workbook> - warnings.txt`, headed by the dialog's summary: a long list outruns the dialog and closes with it (a reviewer showed Snowdon's 221 warnings in the Win32 task dialog Revit's `TaskDialog` resolves to: expanding the details grew it past the screen's bottom and hid the last entries, which is where a run lists the elements it left unmeasured and the threshold-band flags). This is the host side of `model-validation-warnings`' "full warning list available alongside the workbook" (I3, 3.4), delivered early because 1.24 needed it.

The chain is an ordered `IReadOnlyList<ICodeResolver>` ending in `UnclassifiedResolver`, which always succeeds; an absent rule link is an absent list entry, so nothing needs stubbing. **No rule syntax, storage, ordering or UI is designed here.**

## Testing Strategy

| Layer | Where | What |
|---|---|---|
| Unit | macOS | Openings rule (all scenarios, both modes), clamping, `NoSource` vs measured zero, `UnitMismatch`, first-source-wins, chain precedence, whitespace codes, `PartidaKey` collapsing two types into one partida |
| Unit | macOS | Defaults with no file, per-field inheritance, single-category override, loader rejections (malformed, unknown category, negative threshold, invalid mode) |
| Integration | macOS | `EffectiveCriteria` → measurement → `TakeoffResult` + `RunReport` → workbook |
| Golden | macOS | Deterministic ordering, subtotal reconciliation, unclassified block, applied-mode cell, empty workbook |
| Host smoke | Windows/Revit | Isolated load, 11-assembly closure by name (8 third-party + 3 Metrado), `ASSEMBLY_CODE` under Spanish UI, opening enumeration, document unmodified, completion dialog |

## Threat Matrix

**N/A** — this change introduces no routing, shell, subprocess, VCS/PR automation, executable-file-classification or process-integration boundary. The one adjacent boundary, assembly loading into the Revit host process, has its failure surface (an incomplete dependency closure) covered by Decision 7's named-assembly startup assertion. No rows are applicable, so no threat tasks are generated.

## Migration / Rollout

No data migration; the add-in never writes to the document. Each increment ships as its own PR; rollback is deleting the `.addin`. If `UseRevitContext=False` misbehaves, fall back to bare `DocumentFormat.OpenXml`, shrinking the third-party closure from **8 assemblies to 3** (`DocumentFormat.OpenXml`, `.Framework`, `System.IO.Packaging`) under the same inclusive count — so the startup check's 11 become 6 with Metrado's own three.

`openspec/config.yaml` — **recommended, not applied by this phase**: register the seven projects; set `apply.test_command` and `verify.test_command` to `dotnet test Metrado.CrossPlatform.slnf`; set the single `verify.build_command` to `dotnet build Metrado.CrossPlatform.slnf` — the only build that runs on both hosts, since the field holds one string; the Windows-only `dotnet build Metrado.slnx` belongs in tasks. Close the settled `open_questions` (Revit version, Excel library, test framework); re-evaluate `strict_tdd` to `true` once test projects exist.

## Open Questions

- [x] Does *Area and Volume Computations* gate the quantity parameters the Walls criterion reads? Host-only. **Answered (2026-09-23): no, for volume computation; the room area boundary is unsettled.** The harness's `probe-area-settings`, on the Snowdon sample in a transaction it rolled back, reads each setting back after setting it and reads the rooms as the positive control. With volume computation turned off (read back off), rooms with a volume went from 67 to 0 while no wall's `HOST_AREA_COMPUTED` or `HOST_VOLUME_COMPUTED` changed (1156 walls). With the room area boundary moved from Finish to Center (read back), no room's area changed either, so that run shows nothing either way.
- [x] `.addin` install path and `ContextName` collision behaviour on Revit 2027. Host-only. **Partly answered (2026-09-22):** the per-user path `%AppData%\Autodesk\Revit\Addins\2027\` loads, with the assembly in a `Metrado\` subfolder resolved relative to the manifest; Revit registers that folder for context `METRADO.REVIT2027` (the `ContextName`, upper-cased). Collision behaviour is still untested. **Collision answered (2026-09-23):** Revit merges add-ins that declare the same `ContextName` into one context without a word. With the harness's manifest set to `Metrado.Revit2027`, the journal registered Metrado's folder and then the harness's for context `METRADO.REVIT2027`, and both add-ins started; Metrado's 11 assemblies resolved from its own folder, the one registered first (Revit's add-in loader resolves a shared context's names from its registered folders in order). Which folder wins when the two carry different versions of one assembly was not tried. The smoke run's isolation check now fails on such a merge (it names the other folder's assembly), which is how a development run finds it; an estimator's session has no such check, so the name must stay one no one else would choose.
- [x] Does an in-process NUnit runner exist for Revit 2027/.NET 10? Decides whether I2 automates the smoke layer. **Answered (2026-09-23): yes, by their publishers; neither tried here.** `ricaun.RevitTest.TestAdapter` 1.11.x (NUnit, runs tests inside Revit through an add-in; its changelog states "Support Revit 2027 with framework `net10`" from 1.11.0, released 2026-02-27; 1.11.1 followed on 2026-04-10) and `Nice3point.TUnit.Revit` 2027.0.2 (TUnit, `net10.0-windows`, published 2026-09-15). Task 2.8 decides whether one replaces the harness-driven smoke run.
