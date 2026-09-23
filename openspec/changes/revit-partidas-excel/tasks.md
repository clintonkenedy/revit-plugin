# Tasks: Revit partidas + metrado export to Excel

Host marker on every task: **[mac]** runs on macOS (.NET SDK 10.0.400, `dotnet test Metrado.CrossPlatform.slnf`) — **[win]** requires the remote Windows host with Revit 2027.2 over RDP. `Metrado.Revit2027` (`net10.0-windows`) does not build on macOS; the RDP loop is slow, so [win] tasks are batched.

Traceability tag on each task: spec capability, design decision (`D#`), or residual finding (`N1`–`N4`, `OBS`).

## Increment I1 — Vertical slice (walls → Assembly Code → threshold → Excel)

- [x] 1.1 [mac] Scaffold `Metrado.slnx` with `src/Metrado.Domain` (`netstandard2.0;net10.0`, no third-party deps), `src/Metrado.Configuration` (`net10.0`), `src/Metrado.Excel` (`net10.0`, ClosedXML), `src/Metrado.Revit2027` (`net10.0-windows`, `Nice3point.Revit.Api.*` 2027.2.0 with `Private=false`), plus `tests/Metrado.{Domain,Configuration,Excel}.Tests` (xUnit `net10.0`). Add the `IsExternalInit` polyfill to Domain for the `netstandard2.0` leg. — D1, D1b, D2, D3
- [x] 1.2 [mac] Write `Metrado.CrossPlatform.slnf` excluding `Metrado.Revit2027`, and add a test that parses the filter and asserts the **exact resolved project list** (six projects present, `Metrado.Revit2027` absent). A green build never proves a filter selected the intended projects. — D3, OBS
- [x] 1.3 [mac] Domain seam primitives: `Result<TValue,TError>`, `QuantityUnit` (closed set incl. `m2`, `m3`, `u`), `Quantity`, `PartidaKey`, with equality/value tests. — `revit-model-extraction`, N2
- [x] 1.4 [mac] Domain DTOs: `ElementTakeoff`, `RawQuantity`, `OpeningQuantity`, `MaterialRef`, `CodificationReadings`. Test: openings list is empty, never null; `UniqueId` non-empty is enforced at construction. — `revit-model-extraction`, D4
- [x] 1.5 [mac] Domain error/warning types — `ConfigError` (file, failing location as line + position numbers, offending category, invalid value; **MUST NOT reference any `System.Text.Json` type**, it lives in Domain) and `ValidationWarning` (`UniqueId`, category, family, type, condition detected). Tests assert each MUST-level payload field is carried. — N2, `takeoff-configuration`, `model-validation-warnings`
- [x] 1.6 [mac] Domain `BoundaryMode` (exactly two values), `Defaults.Mode = BoundaryMode.Exclusive` as a **named constant, never an enum ordinal**, and `OpeningsThreshold.TryCreate` returning `Result<OpeningsThreshold, ConfigError>`. Tests: negative threshold rejected with category + value named; reordering the enum cannot flip the default. — D6, `takeoff-configuration`
- [x] 1.7 [mac] Domain `Apply(raw, openings, threshold)` — the openings correction. Tests cover every `metrado-measurement` I1 scenario: sub-threshold added back (18.0 + 0.6 → 18.6), exactly-at-threshold under both modes (18.0 vs 19.0), the two modes differ by exactly the boundary opening, above-threshold stays deducted, mixed openings (15.0 → 16.3), no openings, zero threshold disables the correction, openings evaluated individually and never summed against the threshold. — `metrado-measurement`
- [x] 1.8 [mac] Domain gross-bound clamp: `MetradoResult.ClampedToGross` plus a `ValidationWarning` naming the `UniqueId`; and `UnitMismatch` status when any opening's or the raw quantity's unit differs from the threshold's unit (refuse the comparison, never convert in Domain). Tests: openings consume the whole face → metrado equals gross and never exceeds it; inconsistent opening data is clamped and warned. — `metrado-measurement`
- [x] 1.9 [mac] Domain `SelectSource` — first source with a value wins; no source yields `MetradoStatus.NoSource` with `Result = null`. Test that "no source" and "measured 0.0" are different types, and that `Apply` is unreachable without a selected `Quantity`. — `metrado-measurement`
- [x] 1.10 [mac] Domain `CriteriaSet.Default` (Walls → area, `m2`, ordered sources, threshold with `Defaults.Mode`) and `Merge(defaults, overrides)` as per-category then per-field coalesce. Tests: absent category keeps its default entirely; a present category inherits every `null` field (threshold, mode, unit, sources); unknown category name is rejected before merging. — D6, `takeoff-configuration`
- [x] 1.11 [mac] Domain codification chain: `ICodeResolver`, ordered `IReadOnlyList<ICodeResolver>`, `AssemblyCodeResolver` (trim; absent/empty/whitespace-only is unresolved) and the always-succeeding `UnclassifiedResolver`. Tests: earlier link wins over Keynote, whitespace-only code does not create a blank-named partida, uncodeable element never throws. — `partida-codification`
- [x] 1.12 [mac] Domain grouping into `TakeoffResult` keyed by `PartidaKey` (capitulo + resolved code) and by nothing else, with `AppliedCriterion` (which criteria, thresholds and modes were actually applied) and `RunReport`. Tests: two wall types sharing `C1010` collapse into one partida holding five lineas; `TypeKey` is not a grouping key; unclassified elements are measured and reach the result. — N2, `partida-codification`, `takeoff-configuration`
- [x] 1.13 [mac] Domain empty measurement set: a model with no measurable elements produces a well-formed result with zero lineas that reports "no measurable elements found", never a success implying quantities were found. — `metrado-measurement`
- [x] 1.14 [mac] Define the **three-state** criteria-file locator result in Domain — `Found(text)` / `Absent` / `Unreadable(ConfigError)` — so a file that exists but cannot be read (permissions, lock, I/O) can never collapse to `Absent`. Tests: `Unreadable` stops the run; only `Absent` falls back to defaults. — N3, `takeoff-configuration`
- [x] 1.15 [mac] `Metrado.Configuration.Resolve(locatorResult, path)` → `Result<EffectiveCriteria, ConfigError>`; **pin `Resolve` as owned by `Metrado.Configuration`** and amend the design's Interfaces block to state that ownership. I1 covers the no-file path only (`Ok(CriteriaSet.Default)`, `ConfigSource.BuiltInDefaults`); JSON parsing is 2.1. — N4, D5, `takeoff-configuration`
- [x] 1.16 [mac] `Metrado.Excel` writer over `TakeoffResult`: capitulo/partida/linea rows whose grouping level is identifiable from the row itself, dedicated non-empty `UniqueId` column, labelled unclassified block (listing `UniqueId`, category, family, type; explicitly reporting zero when empty), partida and capitulo subtotals, and the **applied boundary mode written into the workbook**. Deterministic ordering by capitulo → partida code → stable per-line key, independent of Revit iteration order. — `excel-budget-export`, `metrado-measurement`
- [x] 1.17 [mac] Golden `.xlsx` fixtures for the writer: 12-of-40-unclassified fixture, zero-unclassified fixture, empty-result workbook with headers and a zero-count summary, and a write-twice byte-stable comparison. — `excel-budget-export`
- [x] 1.18 [mac] Integration test on macOS: `EffectiveCriteria` → measurement → `TakeoffResult` + `RunReport` → workbook, asserting at least one exported metrado differs from the corresponding raw Revit area. — D-Testing Strategy, `metrado-measurement`
- [x] 1.19 [win] `Metrado.Revit2027` host shell: `IExternalApplication`, ribbon button, and `Metrado.addin` declaring `<UseRevitContext>False</UseRevitContext>` + `<ContextName>`. Build `dotnet build Metrado.slnx` on the Windows host and confirm the add-in appears on the ribbon in Revit 2027.2. — `revit-model-extraction`, D3 **Proved on Revit 2027.2 (27.2.0.39), 2026-09-22, commit `91dae47` deployed with `deploy/Deploy-Metrado.ps1`:** the add-in folder registered for context `METRADO.REVIT2027`, `Metrado.Revit2027.dll` loaded into that context, `API_SUCCESS { Starting External Application: Metrado }`, and push button `ExportTakeoffCommand` added under `CustomCtrl_%Add-Ins%Metrado`. **Deploy with the script, not a plain build:** since 1.20 a `dotnet build` output lacks the closure `OnStartup` requires and correctly refuses to start. **`AddInLoadFailureMessage: NoError` is not evidence of startup:** it only says the manifest parsed, and it stays `NoError` on a run whose `OnStartup` threw. Read `Starting External Application` (`API_SUCCESS` / `API_ERROR`) and the push-button line instead. Revit asks again whenever the add-in binary changes, because the add-in is unsigned (`TaskDialog_Security_Unsigned_File_Loading`, default button *Do Not Load*); an unchanged binary loads without asking.
- [x] 1.20 [win] `OnStartup` dependency-closure assertion: verify the **8 third-party assemblies by simple name** (`ClosedXML`, `ClosedXML.Parser`, `DocumentFormat.OpenXml`, `DocumentFormat.OpenXml.Framework`, `ExcelNumberFormat`, `RBush`, `SixLabors.Fonts`, `System.IO.Packaging`) are present in the add-in folder, naming each missing one. Deploy via `dotnet publish` flat folder. Test by deleting one assembly and confirming the failure is diagnosable and never swallowed. — D7, `revit-model-extraction` **Amended:** the list originally read `RBush.Signed`, which is the package; the assembly it ships is `RBush.dll`, and a check by that name would fail every correct deployment. The list is now pinned against the add-in's `deps.json`, not maintained by hand. **Widened:** the check also requires Metrado's own `Configuration`, `Domain` and `Excel` — eleven assemblies — because the add-in starts without them too and would fail only on the first click. **Proved on Revit 2027.2, 2026-09-22:** with the full publish deployed the add-in starts (`API_SUCCESS`, button added); with `ExcelNumberFormat.dll` removed Revit records `API_ERROR { Starting External Application: Metrado }`, adds no button, and shows *External Tool Failure*, whose **Show details** reads `System.InvalidOperationException: Metrado cannot start: … missing from '…\Addins\2027\Metrado': ExcelNumberFormat.dll. Redeploy the add-in from a complete 'dotnet publish' output.` Two limits, recorded rather than worked around: the missing names sit behind *Show details*, not in the dialog's main text, and the journal records only `API_ERROR`, never the exception message.
- [ ] 1.21 [win] `ExtractionService`: read `BuiltInParameter.ASSEMBLY_CODE` (never by display name), convert with `UnitUtils.ConvertFromInternalUnits` using `UnitTypeId`/`SpecTypeId`, emit one `ElementTakeoff` per wall with `UniqueId`, category, family, type, and **individual, never pre-aggregated** opening quantities. Assert 107.639 ft² → 10.0 m² within tolerance. — `revit-model-extraction`, `metrado-measurement`, D4
- [ ] 1.22 [win] Read-only guarantee: extraction opens no write transaction and never marks the document modified. Verify Revit reports no unsaved changes after a full export. — `revit-model-extraction`
- [ ] 1.23 [win] Implement `CriteriaFileLocator` returning the three-state result from 1.14; map an I/O or permission failure to `Unreadable(ConfigError)` identifying the file. — N3
- [ ] 1.24 [win] `ExportTakeoffCommand` wiring (extract → resolve → codify → measure → write) and the completion `TaskDialog` showing `RunReport`: exported total, unclassified count, effective criteria with thresholds and modes, whether no configuration file was found, and the warning list — visible **without opening the workbook**. — `model-validation-warnings`, `takeoff-configuration`, D-Interfaces
- [ ] 1.25 [win] `SmokeCommand` + host smoke run on Revit 2027.2: isolated load, 11-assembly closure (8 third-party + Metrado's own 3, see 1.20), `ASSEMBLY_CODE` read under a Spanish UI ("Código de montaje"), opening enumeration on a wall with one door and two windows, document unmodified, completion dialog. Record answers to the design's three Open Questions (does *Area and Volume Computations* gate the wall quantity parameters; `.addin` install path and `ContextName` collision behaviour; does an in-process NUnit runner exist for 2027/.NET 10). — D2, design Open Questions
- [x] 1.26 [mac] Update `openspec/config.yaml`: register the seven projects; set `apply.test_command` and `verify.test_command` to `dotnet test Metrado.CrossPlatform.slnf`; set `verify.build_command` to `dotnet build Metrado.CrossPlatform.slnf`; delete the settled `open_questions` (Revit version, Excel library, test framework, API access pattern); re-evaluate `strict_tdd` to `true` now that test projects exist. — D-Migration/Rollout

## Increment I2 — Estimator-ready (criteria file, six categories, Keynote, units)

- [ ] 2.1 [mac] `Metrado.Configuration` JSON parser: `JsonCommentHandling.Skip` + `AllowTrailingCommas`, mapping `JsonException.LineNumber`/`BytePositionInLine` into `ConfigError`'s failing location, and a `BoundaryMode` converter whose error lists both accepted values. Tests: malformed syntax, unknown category, unsupported unit, negative threshold, invalid mode — each stops the run before any workbook is written. — D5, `takeoff-configuration`
- [ ] 2.2 [mac] Extend `CriteriaSet.Default` to the six categories (Walls, Floors, Roofs, Railings, Doors, Windows) with per-category unit, sources, threshold and mode. Tests: a file defining Floors only leaves Walls on the built-in default; wall threshold 1.0 m² and floor threshold 0.5 m² apply independently; wall mode `inclusive` with floors unset adds back the wall's 1.0 m² opening but not the floor's. — `takeoff-configuration`, `metrado-measurement`
- [ ] 2.3 [mac] **N1 — count-based measurement.** Add `MetradoStatus.Counted` and branch on an **empty `Sources` list** meaning "counted category, no quantity source" *before* `SelectSource` runs, so it is never conflated with "sources listed, none had a value". Metrado is the number of qualifying instances with unit `u`. Test: 14 doors resolving to one partida yield `14 u` and **zero** warnings — the current design would emit 14 spurious `NoSource` warnings. — N1, `metrado-measurement`
- [ ] 2.4 [mac] Keynote resolver and shared-parameter resolver in the chain (positions 2 and 3), applying the same empty/whitespace rules. Tests: empty Assembly Code + Keynote `M-030` resolves to `M-030`; a shared parameter not bound in the model returns unresolved for every element without raising, and the chain continues. — `partida-codification`
- [ ] 2.5 [mac] Excel: unit reported per partida as resolved by the category criterion; lines with differing units in one partida raise a `ValidationWarning` and are **not summed**. Golden fixture covers both. — `excel-budget-export`
- [ ] 2.6 [win] Extend extraction to the six categories; categories absent from the model are skipped without error. Add the Keynote built-in parameter read and the user-nominated shared-parameter read (type-level and instance-level, tolerating an unbound parameter). — `revit-model-extraction`, `partida-codification`
- [ ] 2.7 [win] Wire the criteria file path into `ExportTakeoffCommand` and surface `ConfigError` as a blocking dialog identifying the file and failing location; confirm no partial workbook is left on disk. — `takeoff-configuration`
- [ ] 2.8 [win] Act on 1.25's in-process-runner finding: either automate the smoke layer under that runner, or record in the design why the smoke layer stays manual. — design Open Question 3, D2

## Increment I3 — Model fidelity (material layers, validation pass, saved configurations)

- [ ] 3.1 [win] Material-layer extraction: emit one `RawQuantity` per material with a populated `MaterialRef` for compound elements, alongside the whole-element quantity. — `revit-model-extraction`
- [ ] 3.2 [mac] Domain material-layer metrado with a stated reconciliation tolerance; a layer sum that fails to reconcile with the whole-element quantity raises a `ValidationWarning`. — `metrado-measurement`
- [ ] 3.3 [mac] Domain pre-export validation pass producing the warning list: openings exceeding gross, zero or negative metrado on a modelled element, a supported-category element with no applicable criterion, a criterion that yielded no value from any source, and layer-reconciliation failures. Each warning carries `UniqueId`, category, family, type and the condition. Test that a clean model produces an empty list. — `model-validation-warnings`
- [ ] 3.4 [mac] Warnings are advisory: a run with fifteen warnings still writes the workbook, and the **full list** (not just a count) is available in `RunReport`. Only a `ConfigError` stops a run. — `model-validation-warnings`
- [ ] 3.5 [mac] Excel material-layer rows: one linea per layer, each naming its material and retaining the host element's `UniqueId`. Golden fixture: a three-layer wall emits three lines sharing one host `UniqueId` with distinct material names. — `excel-budget-export`
- [ ] 3.6 [mac] Saved configurations in `Metrado.Configuration`: name, save and reload a complete configuration (criteria, thresholds, codification chain settings, material-layer settings). Tests: reload + re-run over unchanged data reproduces identical metrado values; two configurations differing only in the wall threshold differ only by the openings added back; a configuration loaded against a different model skips absent categories without error. — `takeoff-configuration`
- [ ] 3.7 [win] Command/UI to pick and save a named configuration, and report the loaded configuration name in the completion `TaskDialog`. — `takeoff-configuration`

## Increment I4 — Rules engine (GATED — no implementation tasks)

- [ ] 4.1 [mac] Record the gate. I4 remains unimplemented: the chain already reserves the fourth link between the shared-parameter resolver and `unclassified`, and an absent rule link is simply an absent list entry, so nothing needs stubbing. **Unblocking condition:** I2 and I3 have shipped and produced concrete rules from real models. Rule syntax, storage, ordering semantics, filter operators and any rule UI MUST be specified by a later change, not assumed here. — `partida-codification` (I4 contract only)

## Review Workload Forecast

Re-forecast after PR 1 and PR 2 landed. The previous forecast was written before any code existed and before `strict_tdd` was `true`; it is superseded because it was measurably wrong, not because the plan changed. Task list, ordering and scope are unchanged.

Estimated changed lines: 9000-13000
400-line budget risk: High
Chained PRs recommended: Yes
Decision needed before apply: No

Chain strategy: stacked-to-main

Already landed: 2026 changed lines across PRs 1-2 (tasks 1.1-1.6, 1.26). Projected total for the change: **11000-15000**, against the original estimate of 6000-9000 — roughly **1.7x**.

### Why the original forecast was wrong

`strict_tdd` was `false` when it was written and while PR 1 ran. Task 1.26 flipped it to `true`, so from PR 2 onward every task carries a full test suite. PR 2 produced 87 tests for 4 tasks. The original forecast counted implementation lines only and did not count the tests those same tasks mandate.

| PR | Tasks | Estimated | Measured | Factor |
|----|-------|-----------|---------:|-------:|
| 1 | 1.1, 1.2, 1.26 | 250-350 | 460 | 1.5x |
| 2 | 1.3-1.6 | 350-450 | 1566 | 3.9x |

Per-commit evidence (`additions + deletions`, all merged and green):

| Commit | Lines | Scope |
|--------|------:|-------|
| `9889641` | 373 | solution, seven projects, `.slnf`, filter assertion (1.1, 1.2) |
| `9581315` | 87 | `openspec/config.yaml` reconciliation (1.26) |
| `f4e4505` | 156 | `Result<TValue,TError>` |
| `3cf53ad` | 342 | `QuantityUnit`, `Quantity`, `PartidaKey`, `Guard` |
| `22ffcc3` | 372 | the five seam DTOs |
| `082ece9` | 339 | `ConfigError`, `ConfigLocation`, `ValidationWarning` |
| `c9b55c7` | 352 | `BoundaryMode`, `Defaults.Mode`, `OpeningsThreshold` |
| `4fe03eb` | 5 | analyzer warning fix |

### Estimating basis

**≈390 changed lines per `[mac]` task**, derived from PR 2's measured 1566 lines across 4 tasks under `strict_tdd: true`, where test lines equalled or exceeded implementation lines and coherent type-group commits landed at 340-372. **≈130-330 per `[win]` task**, lower because `Metrado.Revit2027` sits outside strict TDD — it carries no xUnit suite and is proved by `SmokeCommand` plus manual RDP verification, so the test multiplier that drove PR 2's 3.9x does not apply. The `[win]` figure is the weaker half of this basis: it is reasoned from scope, not yet measured, and should be recalibrated after PR 15.

Per-task ranges below are the ≈390 basis adjusted for evident scope, and sum bottom-up to ≈10900 — consistent with the 9000-13000 band.

### Budget granularity — read this before slicing

The 400-line budget was honoured **per commit**, not per PR. PR 1 totalled 460 and PR 2 totalled 1566 at PR level; every individual commit stayed under 400. No `size:exception` was recorded, which is only accurate under the per-commit reading.

The table below applies the budget at **PR level**, per the chained-PR hard rule. That is what drives the PR count from 15 to 32. If the team prefers the per-commit reading it has actually been using, the same work collapses to roughly 14 remaining PRs holding ~30 commit-sized units — but that choice should be made explicitly rather than inherited by accident.

### Proposed PR slicing

Remaining work only (tasks 1.7 onward). PRs 1-2 are shown as measured actuals; PRs 3-32 are forecast. Golden `.xlsx` fixtures are excluded from the authored count but remain in snapshot identity.

Test-command legend: **DOM** `dotnet test tests/Metrado.Domain.Tests` — **CFG** `dotnet test tests/Metrado.Configuration.Tests` — **XLS** `dotnet test tests/Metrado.Excel.Tests` — **ALL** `dotnet test Metrado.CrossPlatform.slnf` — **WIN** `dotnet build Metrado.slnx` on the Windows host.

| PR | Inc | Tasks | Scope | Est. lines | Test | Runtime harness | Rollback boundary |
|----|-----|-------|-------|-----------:|------|-----------------|-------------------|
| 1 | I1 | 1.1, 1.2, 1.26 | Solution, seven projects, `.slnf` + project-list assertion, `config.yaml` | **460 actual** | ALL | N/A — build config only | Landed |
| 2 | I1 | 1.3-1.6 | Domain seam types, `ConfigError`, `ValidationWarning`, `OpeningsThreshold` | **1566 actual** | DOM | N/A — pure value types | Landed |
| 3 | I1 | 1.7 | Openings correction `Apply` + all eight I1 measurement scenarios | 420-480 | DOM | N/A — pure function | Revert `Apply`; PR 2 types survive |
| 4 | I1 | 1.8 | Gross clamp + `UnitMismatch` status | 340-400 | DOM | N/A — pure function | Revert clamp/status; the rule from PR 3 survives |
| 5 | I1 | 1.9 | `SelectSource`, `NoSource` distinct from measured zero | 240-300 | DOM | N/A — pure function | Revert the selector |
| 6 | I1 | 1.10 | `CriteriaSet.Default` + per-category per-field `Merge` | 420-480 | DOM | N/A — pure function | Revert criteria files |
| 7 | I1 | 1.11 | `ICodeResolver` chain, `AssemblyCodeResolver`, `UnclassifiedResolver` | 370-430 | DOM | N/A — pure function | Remove chain files; measurement unaffected |
| 8 | I1 | 1.12 | `TakeoffResult` grouping, `AppliedCriterion`, `RunReport` | 420-480 | DOM | N/A — pure function | Revert grouping; chain from PR 7 survives |
| 9 | I1 | 1.13 | Empty measurement set reports "no measurable elements" | 150-210 | DOM | N/A — pure function | Revert the empty-set branch |
| 10 | I1 | 1.14 | Three-state locator result `Found`/`Absent`/`Unreadable` (N3) | 220-280 | DOM | N/A — result type only; I/O lands in PR 17 | Revert the result type |
| 11 | I1 | 1.15 | `Resolve` + ownership pinned in design Interfaces (N4) | 270-330 | CFG | N/A — no file I/O yet | Revert `src/Metrado.Configuration/` + the design amendment |
| 12 | I1 | 1.16 | ClosedXML writer: rows, subtotals, unclassified block, mode, ordering | 500-600 | XLS | N/A — writer output is asserted in PR 13 | Revert `src/Metrado.Excel/`; Domain untouched |
| 13 | I1 | 1.17 | Golden `.xlsx` fixtures + write-twice byte stability | 260-340 | XLS | N/A — the goldens are the harness | Revert fixtures + their tests |
| 14 | I1 | 1.18 | macOS end-to-end integration test | 210-280 | ALL | ALL end to end | Revert the integration test only |
| 15 | I1 | 1.19, 1.20 | Host shell, `.addin` isolation, 11-assembly closure assertion (8 third-party + 3 Metrado) | **877 actual** (est. 340-420, ≈2.3x; 13 commits, largest 250) | WIN | Revit 2027.2: ribbon loads; delete one assembly → named failure | Delete `Metrado.addin`; nothing else changes |
| 16 | I1 | 1.21, 1.22 | `ExtractionService` + read-only guarantee | 350-430 | WIN | Revit 2027.2: 107.639 ft² → 10.0 m², document unmodified | Revert extraction; PR 15 shell still loads |
| 17 | I1 | 1.23, 1.24 | Locator I/O, `ExportTakeoffCommand`, completion `TaskDialog` | 380-460 | WIN | Revit 2027.2: full export, report readable without opening the workbook | Revert command wiring |
| 18 | I1 | 1.25 | `SmokeCommand` + recorded answers to the three Open Questions | 300-380 | WIN | Revit 2027.2 smoke run under a Spanish UI | Revert `SmokeCommand` + design notes |
| 19 | I2 | 2.1 | JSON parser, location mapping, `BoundaryMode` converter | 450-550 | CFG | N/A — parsing is pure; path wired in PR 25 | Revert parser; I1 defaults path survives |
| 20 | I2 | 2.2 | Six-category defaults with per-category unit, sources, threshold, mode | 400-500 | DOM | N/A — pure function | Revert extended defaults; Walls default survives |
| 21 | I2 | 2.3 | N1 `Counted` status, empty-`Sources` branch before `SelectSource` | 220-280 | DOM | N/A — pure function | Revert the `Counted` member and its branch |
| 22 | I2 | 2.4 | Keynote + shared-parameter resolvers at chain positions 2 and 3 | 370-430 | DOM | N/A — pure function | Remove the two links; chain still terminates |
| 23 | I2 | 2.5 | Per-partida unit, mixed-unit warning, never summed | 270-330 | XLS | N/A — covered by golden fixture | Revert the unit column + golden |
| 24 | I2 | 2.6 | Six-category extraction, Keynote and shared-parameter reads | 360-440 | WIN | Revit 2027.2: six-category model export | Revert extensions; I1 wall path survives |
| 25 | I2 | 2.7, 2.8 | Criteria path wiring, blocking `ConfigError` dialog, runner decision | 280-360 | WIN | Revit 2027.2: bad criteria file leaves no partial workbook | Revert wiring; defaults path survives |
| 26 | I3 | 3.1 | Material-layer extraction with populated `MaterialRef` | 220-280 | WIN | Revit 2027.2: three-layer wall | Revert layer emission; `MaterialRef` stays null |
| 27 | I3 | 3.2 | Layer metrado + stated reconciliation tolerance + warning | 320-380 | DOM | N/A — pure function | Revert layer metrado |
| 28 | I3 | 3.3 | Pre-export validation pass, five warning conditions | 450-550 | DOM | N/A — pure pass over extracted data | Revert the pass; I1 clamp warning survives |
| 29 | I3 | 3.4 | Warnings advisory, full list in `RunReport` | 170-230 | DOM | N/A — pure function | Revert the advisory branch |
| 30 | I3 | 3.5 | Material-layer Excel rows, one linea per layer | 320-380 | XLS | N/A — covered by golden fixture | Revert layer rows + golden |
| 31 | I3 | 3.6 | Saved configurations: name, save, reload, reproducible re-run | 500-600 | CFG | N/A — round-trip asserted in tests | Revert save/load; single-file path survives |
| 32 | I3/I4 | 3.7, 4.1 | Configuration picker + `TaskDialog` name; record the I4 gate | 260-340 | WIN | Revit 2027.2: save, reload, re-run | Revert the picker; 4.1 is documentation only |

**30 remaining PRs, 32 total against the original 15** — the PR count roughly doubles because the line total is ~1.7x and the per-PR budget is fixed.

Eight forecast PRs sit above 400: **3, 6, 8, 12, 19, 20, 28, 31**. Four of them admit a second cohesive split and should be split rather than excepted — PR 12 (rows/ordering, then subtotals + unclassified block), PR 19 (parser, then converter + error mapping), PR 28 (warning conditions, then the pass), PR 31 (save, then reload + reproducibility). That raises the remaining count to ~34 and keeps the zero-exception record intact.

The other four — **PR 3, 6, 8, 20** — cannot be split without separating a behaviour from the tests that prove it: the eight openings scenarios are one rule, `Default` and `Merge` are meaningless apart, grouping and `AppliedCriterion` are one result shape, and the six-category defaults are one table. Recommend `size:exception` for those four. Do not shrink any PR by deleting tests, comments or goldens.

**Stacked-to-main** is the cached strategy: each PR merges to main in order. PRs 3-14 must merge in sequence because each depends on the previous one's Domain types; PRs 15-18 depend on PR 14's completed cross-platform pipeline.
