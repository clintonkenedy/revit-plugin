# Exploration: Revit partidas + metrado export to Excel

Feasibility is high and the technical risk is low, but the project is blocked on one decision that changes the code: **which Revit version(s) to target**. The single most important API symbol for this product — the `Assembly Code` parameter — was *renamed* in Revit 2026, so the version choice is not cosmetic. Everything else (Excel library, measurement rules, packaging, testability) has a clear, verified answer.

**Domain glossary (Spanish trade terms kept as domain vocabulary):** *partida* = budget line item / unit of work; *metrado* = measured quantity; *capitulo* = budget chapter; *linea de medicion* = measurement line (one measured instance).

---

## Current State

The repository is empty: `.git/`, `.atl/skill-registry.md`, and the `openspec/` scaffolding only. There is no source code, no build file, no test runner, and no CI. CodeGraph was not used because there is no code to index; this is a greenfield exploration, not an impact analysis.

The environment is split:

| Surface | Capability |
|---|---|
| macOS ARM64 (this host) | Editing and planning only. No `dotnet`, no Revit. |
| Remote Windows host over RDP | Build, run, debug, Revit installed. |

One verified fact softens that split: **Revit API reference assemblies are published on NuGet** (`Nice3point.Revit.Api.RevitAPI`, versions 2014–2027). Compiling a Revit add-in therefore does **not** require Revit to be installed — only *running* it does.

---

## Affected Areas

Nothing exists yet, so this section describes the modules the change will create rather than files it will touch.

- `src/{Product}.Domain/` — pure metrado rules, codification resolution, capitulo/partida/linea grouping. No Revit types. This is the testable core.
- `src/{Product}.Excel/` — Excel writer. No Revit types.
- `src/{Product}.Revit/` — the only assembly that references `RevitAPI.dll` / `RevitAPIUI.dll`. `IExternalApplication`, ribbon, extraction adapter.
- `src/{Product}.Revit/{Product}.addin` — add-in manifest.
- `openspec/config.yaml` — `open_questions`, `design` rules, and `test_command` all depend on the decisions below.

---

## 1. Revit API: which parameters actually carry the quantity

All parameter identities below were verified against the Revit API 2025 and 2026 `BuiltInParameter` enumeration and `ParameterTypeId` class.

| Quantity | `BuiltInParameter` | User-visible name | Applies to | Reliability as metrado |
|---|---|---|---|---|
| Area | `HOST_AREA_COMPUTED` | "Area" | Walls, floors, roofs, ceilings | **Not directly valid** — see openings rule below |
| Volume | `HOST_VOLUME_COMPUTED` | "Volume" | Walls, floors, roofs | Same caveat |
| Perimeter | `HOST_PERIMETER_COMPUTED` | "Perimeter" | Host objects | Usable |
| Length | `CURVE_ELEM_LENGTH` | "Length" | Curve-driven elements (walls, railings, MEP curves) | Usable |
| Room area / volume | `ROOM_AREA` / `ROOM_VOLUME` | "Area" / "Volume" | Rooms | Volume is only populated when the model's *Area and Volume Computations* setting enables volumes — **unverified whether this also gates `ROOM_AREA`; confirm on the Windows host** |
| Count | n/a | n/a | Any category | Count the collector results; there is no parameter |

Gotchas that will bite, in order of severity:

1. **Internal units are imperial.** Revit stores lengths in decimal feet, areas in ft², volumes in ft³, regardless of the project's display units. Conversion is mandatory via `UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.SquareMeters)`. `DisplayUnitType` **does not exist** in the 2025/2026 APIs (verified 404 on both); Revit 2021+ uses the `ForgeTypeId` model (`UnitTypeId`, `SpecTypeId`). If you multi-target back to 2020, this needs `#if REVIT2021_OR_GREATER`.
2. **`HOST_AREA_COMPUTED` is Revit's geometry, not the budget's metrado.** Revit already subtracts *all* openings. The reference product (Cost-It) re-adds openings below a user threshold. Consuming the raw parameter silently produces wrong budgets — this is the single most valuable rule in the product.
3. **Compound walls.** A wall's `Volume` is the whole sandwich. Per-layer takeoff needs `Element.GetMaterialIds` + `Element.GetMaterialArea` / `Element.GetMaterialVolume` (all verified present in 2025 and 2026). This is what makes the `Keynote`-by-material codification path meaningful.
4. **Parameter name lookup is language-dependent.** A Spanish Revit UI shows "Área", "Volumen", "Código de montaje". Always resolve by `BuiltInParameter`/`ForgeTypeId`, never by display name. Revit 2026 additionally changed the *user-visible name* of `INSTANCE_LENGTH_PARAM` from "Length" to "System Length" for structural beams and columns — another reason name lookup is fragile.
5. **Model correctness beats API correctness.** Walls not joined, elements modeled as Generic Models, missing levels — none of this throws. It silently yields a plausible but wrong metrado. The product needs a validation/warnings pass, not just an extraction pass.

---

## 2. Codification strategy (element -> partida)

| Option | Mechanism | Pros | Cons | Effort |
|---|---|---|---|---|
| **A. Built-in params** | `Assembly Code` (by type) + `Keynote` (by material) | Zero model prep if the discipline already uses them; matches the reference product exactly; survives file exchange | Renamed in Revit 2026 (see below); often empty or misused in real incoming models | Low |
| **B. Project shared parameter** | A dedicated shared parameter, type or instance | Full control of the code namespace; instance-level discrimination (pipes by diameter) is natural | Requires the shared-parameter file to be bound in every incoming model — you said the modeling discipline may not be yours | Medium |
| **C. Rules engine in the add-in** | User-editable rules: category + filters -> code | Works on models you do not control; no model prep at all; the real differentiator | Rule language design, UI, persistence, validation. Largest surface | High |

**The 2026 breaking change (verified, and it lands exactly here):**

| Revit ≤ 2025 | Revit ≥ 2026 |
|---|---|
| `BuiltInParameter.UNIFORMAT_CODE` | `BuiltInParameter.ASSEMBLY_CODE` |
| `ParameterTypeId.UniformatCode` | `ParameterTypeId.AssemblyCode` |
| `BuiltInParameter.UNIFORMAT_DESCRIPTION` | `BuiltInParameter.ASSEMBLY_DESCRIPTION` |
| `BuiltInParameter.OMNICLASS_CODE` ("OmniClass Number") | `BuiltInParameter.CLASSIFICATION_CODE` ("Classification Number") |

The user-visible name "Assembly Code" did not change; only the API symbol did. `ParameterTypeId.UniformatCode` returns 404 in the 2026/2027 docs and `ParameterTypeId.AssemblyCode` returns 404 in the 2024/2025 docs — the two are mutually exclusive. Multi-targeting 2025 **and** 2026 requires `#if REVIT2026_OR_GREATER` on the core codification lookup. `Keynote` (`BuiltInParameter.KEYNOTE_PARAM` / `ParameterTypeId.KeynoteParam`) was **not** renamed.

Practical read: A and C are not rivals. A is the *default resolver*; C is the *fallback resolver* for models that did not do their homework. Modeling the codification as an ordered chain of resolvers (`AssemblyCode -> Keynote -> shared parameter -> rule -> unclassified`) costs almost nothing in v1 and buys the whole C path later without a rewrite. The "unclassified" bucket must be a first-class output, not an exception — it is what tells the user their model is not ready.

---

## 3. Measurement rules layer

Two things must be data, not code:

**Per-category criterion.** Follow the reference product: a criterion is an *ordered list of parameter sources*, take the first with a value. That makes the default table and the user override the same mechanism.

```yaml
rules:
  Walls:      { unit: m2, sources: [HOST_AREA_COMPUTED],  openingsThresholdM2: 1.0 }
  Floors:     { unit: m2, sources: [HOST_AREA_COMPUTED],  openingsThresholdM2: 1.0 }
  Railings:   { unit: ml, sources: [CURVE_ELEM_LENGTH] }
  Doors:      { unit: u,  sources: [] }                   # count
  StructuralFraming: { unit: m3, sources: [HOST_VOLUME_COMPUTED] }
```

**Openings threshold.** The rule is a *correction* applied on top of the raw parameter, not a replacement for it:

```
metrado = rawArea + sum(area(o) for o in openings(element) if area(o) < threshold)
```

Because Revit already subtracted every opening, anything below the threshold is added back. Enumerating an element's openings requires Revit geometry APIs (`HostObjectUtils`, opening/inserts queries) — that part is Revit-side. But the *arithmetic and the policy* are pure, and that is where the seam belongs: the Revit adapter produces `(rawArea, [openingAreas])`, the domain applies the rule. That single split is what makes the core rule unit-testable on macOS.

The unit of configuration should be a file the user can version and share (YAML or JSON next to the add-in, or embedded in the project). Do not hardcode the table, and do not put it only in a dialog.

---

## 4. Excel output library

Verified from NuGet metadata and package READMEs (2026-09).

| Library | License | Framework reach | Excel required? | Verdict |
|---|---|---|---|---|
| **ClosedXML 0.105.1** | **MIT** | `netstandard2.0` / `2.1` → covers **both** net48 and net8.0 | No | **Recommended.** Highest-level API, no license friction. |
| **DocumentFormat.OpenXml 3.5.1** | **MIT** | net35 / net40 / net46 / netstandard2.0 / net8.0 / net10.0 | No | Lowest risk, lowest level. Verbose for styled, grouped output. |
| **EPPlus 8.7.0** | **Polyform Noncommercial 1.0.0**, commercial license required for commercial use | net35 / net462 / netstandard2.0 / net8.0+ | No | **Avoid unless a commercial license is bought.** |
| Excel Interop | n/a | n/a | **Yes** | Rejected — requires Excel installed, unstable in a host process. |

**The EPPlus licensing claim is confirmed, and it is worse than "just a license".** EPPlus 8 is a dual-license model: Polyform Noncommercial for personal/noncommercial use, paid commercial license otherwise. Beyond the legal question, EPPlus 8 **requires the license to be set at runtime** (`ExcelPackage.License.SetCommercial(key)` / `SetNonCommercialOrganization(name)`, or an `appSettings`/env var). Forgetting it is a runtime failure inside Revit, not a compile error. For a construction-industry add-in, "noncommercial" is not a safe assumption.

**ClosedXML's real cost:** it depends on `DocumentFormat.OpenXml (>= 3.1.1, < 4.0.0)`, `SixLabors.Fonts`, `RBush.Signed`, `System.Memory`, `System.Buffers`. In Revit **≤ 2025**, all add-ins share one assembly load context — if another installed add-in loads a different `DocumentFormat.OpenXml`, you get a version conflict at runtime. Mitigations: ILRepack/ILMerge the dependencies into your assembly (what the mainstream community template does), or target Revit 2026+ and use native isolation (see §5). If dependency conflicts turn out to be a real problem and you are stuck on 2025, plain `DocumentFormat.OpenXml` is the smaller blast radius.

None of the three requires Excel to be installed — all write OOXML directly.

---

## 5. Revit version / .NET branch consequences

Verified: **Revit 2025 and later are .NET 8 only**; add-ins must be recompiled and target `net8.0-windows`. Revit 2024 and earlier are .NET Framework 4.8. Revit 2027 API docs still carry the "Migrating From .NET 4.8 to .NET 8" page, which *suggests* 2027 is still .NET 8 — treat as **unverified inference**, confirm before committing.

| Branch | Runtime | What you gain | What you pay |
|---|---|---|---|
| **2024 and earlier** | .NET Framework 4.8 | Largest installed base in the field | Old C#/BCL, shared load context, legacy `.csproj` friction, no native isolation |
| **2025 only** | .NET 8 | Modern C#, SDK-style projects, `net8.0-windows` | Shared load context (isolation is 2026+); `UNIFORMAT_CODE` naming |
| **2026 / 2027** | .NET 8 | **Native add-in dependency isolation**; cleanest dependency story | Smallest installed base; `ASSEMBLY_CODE` naming; furthest from most client models |
| **Multi-target 2024–2027** | both | Covers the market | Conditional compilation on `UNIFORMAT_CODE`/`ASSEMBLY_CODE`, on `UnitTypeId`/`DisplayUnitType`, plus 2× build/test matrix — on a remote host you reach over RDP |

Concrete .NET 8 migration hazards Autodesk documents (relevant even for a greenfield project, because they shape the build): build warning `MSB3277` needs `<FrameworkReference Include="Microsoft.WindowsDesktop.App"/>`; `CA1416` needs `[assembly: SupportedOSPlatform("windows")]`; .NET 8 assembly probing differs and may need `runtimeconfig.json` / `deps.json` handling; `BinaryFormatter` is obsolete (affects WinForms image resources); `Encoding.Default` is now always UTF-8.

Multi-targeting is a solved problem in this ecosystem — the mainstream community templates generate `REVIT2026_OR_GREATER`-style constants and per-version solution configurations. It is not free, but it is not research either.

---

## 6. Add-in packaging and the Mac → Windows loop

**The `.addin` manifest** is XML registering the entry class. Revit 2026 added an optional `ManifestSettings` block — this is the native dependency isolation:

```xml
<RevitAddIns>
  <AddIn Type="Application">
    <Name>PartidasExport</Name>
    <Assembly>PartidasExport\PartidasExport.dll</Assembly>
    <AddInId>{new GUID}</AddInId>
    <FullClassName>PartidasExport.App</FullClassName>
    <VendorId>...</VendorId>
  </AddIn>
  <ManifestSettings>
    <UseRevitContext>False</UseRevitContext>
    <ContextName>PartidasExport</ContextName>
  </ManifestSettings>
</RevitAddIns>
```

Verified semantics: `UseRevitContext` defaults to `true`; setting it `false` loads the add-in into a separate assembly load context, isolating it from Revit's and other add-ins' dependencies. It applies to **all** add-ins in that manifest. Add-ins in the same folder share a context; a custom `ContextName` groups add-ins across folders. Autodesk's own caveat: once isolated, you must ship *complete* dependencies — you can no longer free-ride on another add-in having loaded them. This is the clean answer to the ClosedXML/OpenXml conflict risk, and it is **only available from Revit 2026**.

**Install locations** (all-users vs per-user) — commonly `%ProgramData%\Autodesk\Revit\Addins\{version}\` and `%AppData%\Autodesk\Revit\Addins\{version}\`. **Flagged as unverified**: I could not reach an authoritative Autodesk page for these exact paths in this session (403/JS-gated). Confirm on the Windows host before writing a deploy script.

**The Mac → Windows loop, realistically.** Three options, none exotic:

1. **Git as the transport.** Edit on the Mac, push, pull on the Windows host, build+run there. Simplest, auditable, and it makes the Windows host the only place that needs a toolchain. Recommended default.
2. **Remote dev / SSH from the Mac editor into the Windows host.** Better inner loop, more setup.
3. **Shared folder over the RDP session.** Fastest to start, worst for reproducibility. Not recommended as the primary path.

For deployment on the Windows host, an MSBuild post-build copy into the Addins folder is the pragmatic v1; an MSI installer is a later concern.

---

## 7. Testability — the seam that matters

The hard constraint: **anything that touches `Document`, `Element`, or `Parameter` requires Revit running.** The established community approach for Revit API tests runs the test framework *inside the Revit process*. There is no meaningful mock of the Revit API.

So the design question is not "how do we test Revit" — it is "how little code needs Revit".

```
┌─────────────────────────────────────────────────────────────┐
│ {Product}.Revit        net8.0-windows / net48                │  Revit required.
│   IExternalApplication, ribbon, ExtractionAdapter             │  Thin. Integration-tested
│   Revit types in, plain DTOs out  ─────────────┐              │  on Windows only.
└────────────────────────────────────────────────┼─────────────┘
                                                 ▼  DTOs only
┌─────────────────────────────────────────────────────────────┐
│ {Product}.Domain       netstandard2.0                        │  NO Revit reference.
│   MeasurementRules, OpeningsThresholdRule,                   │  100% unit-testable
│   CodeResolver chain, Capitulo/Partida/Linea grouping        │  ON THE MAC.
└─────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────┐
│ {Product}.Excel        netstandard2.0                        │  NO Revit reference.
│   Workbook writer over the grouped model                     │  Golden-file testable
└─────────────────────────────────────────────────────────────┘  ON THE MAC.
```

The adapter's output DTO is the seam. Something like:

```csharp
record ElementTakeoff(
    string UniqueId,        // Element.UniqueId — stable GUID, the traceability anchor
    string CategoryName,
    string FamilyName,
    string TypeName,
    string? AssemblyCode,
    string? Keynote,
    double RawAreaFt2,
    double RawVolumeFt3,
    double RawLengthFt,
    IReadOnlyList<double> OpeningAreasFt2,
    IReadOnlyDictionary<string, string> Parameters);
```

Given that record, the openings-threshold rule, the criterion resolution, the codification chain, the grouping and the Excel bytes are all testable with hand-written fixtures — no Revit, no Windows. That is the highest-leverage architectural decision in this exploration.

`Element.UniqueId` is verified present in 2025/2026 and is the stable per-instance identifier the reference product uses for traceability. Carry it into every linea de medicion row from day one; retrofitting traceability is painful.

**Caveat on targeting `netstandard2.0` for the core:** it builds and tests on macOS with the .NET SDK and is consumable from both net48 and net8.0 — but the .NET SDK is not currently installed on this Mac. Installing it is a prerequisite for any local test loop, and it is the one toolchain step that makes the Mac useful beyond editing.

**Unverified:** no test framework has been chosen or validated. `openspec/config.yaml` currently records `strict_tdd: false` with `test_command: ""`, which remains correct until this is decided.

---

## 8. Candidate scopes for a first release

| | Scope | Includes | Excludes | Proves |
|---|---|---|---|---|
| **S1** | *Thinnest useful* | One category (Walls). `Assembly Code` codification only. Fixed default criterion (m²). **Openings threshold included** — it is the product, not a nicety. Flat Excel sheet: capitulo / partida / linea, with `UniqueId`. Elements without a code go to an explicit "unclassified" block. | Multi-category, Keynote, rules engine, configuration UI, material layers | The whole vertical slice end-to-end, including the one rule that makes the output *correct* rather than merely plausible |
| **S2** | *Useful to a real estimator* | S1 + configurable per-category criteria table (external file) + Walls/Floors/Roofs/Railings/Doors/Windows + `Keynote` fallback in the codification chain + unclassified report + unit conversion to the project's display units | Rules engine, material-layer takeoff, UI beyond a simple dialog | That the rules layer is genuinely data-driven |
| **S3** | *Approaching the reference product* | S2 + user-defined rules engine (category + parameter filters -> code) + per-material layer takeoff via `GetMaterialArea`/`GetMaterialVolume` + saved/reusable configurations + model validation warnings | Bidirectional sync, price databases, 4D/5D — permanently out of scope | Nothing new architecturally; it is scale, and it is where most of the effort lives |

S1 is deliberately *not* "export Revit's Area column to Excel". That version would ship a wrong number and teach the user to distrust the tool. The openings threshold belongs in the first release.

---

## Recommendation

1. **Decide the Revit version first.** Nothing else is safely designable until then, because the codification parameter symbol itself depends on it.
2. **Architect for the seam now** (`Revit` adapter / `Domain` / `Excel`), regardless of which version wins. It costs nothing at this size and it is the only thing that gives you a test loop on the Mac.
3. **ClosedXML**, unless dependency conflicts on a Revit ≤ 2025 target force a drop to bare `DocumentFormat.OpenXml`. Not EPPlus.
4. **Ship S1**, with the openings threshold included.
5. **Codification as an ordered resolver chain** from day one, even if v1 only implements the first link.

---

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| `UNIFORMAT_CODE` → `ASSEMBLY_CODE` rename at 2026 | Code will not compile across the boundary | Decide the version; if multi-targeting, isolate behind a single `#if`-guarded accessor |
| Incoming models have empty `Assembly Code` | The product produces nothing useful on real client files | Resolver chain + a loud, first-class "unclassified" report |
| Dependency version conflict with other add-ins on Revit ≤ 2025 | Runtime failure inside Revit, hard to diagnose | ILRepack the dependencies, or target 2026+ and set `UseRevitContext=false` |
| Imperial internal units silently mis-scale the metrado | Wrong budgets that look plausible | `UnitUtils` conversion at the adapter boundary; assert it in domain tests |
| No test loop exists anywhere yet | Regressions land invisibly | Install the .NET SDK on the Mac; keep Domain/Excel at `netstandard2.0`; pick a test framework during design |
| Remote-only build/debug over RDP | Slow iteration, easy drift between hosts | Git as the transport; Windows host is the single build authority |
| Incorrectly modeled BIM input | Garbage-in produces a confident wrong budget | Validation/warnings pass is a scoped feature, not an afterthought (S3, or a stub in S2) |

---

## Ready for Proposal

**Not yet.** Feasibility is established and the technical options are resolved, but the target Revit version is a genuine fork that changes source code, and the user owns it. Once that single decision lands — plus scope selection — the proposal can be written immediately.
