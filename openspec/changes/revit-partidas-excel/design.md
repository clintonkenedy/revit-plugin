# Design: Revit partidas + metrado export to Excel

## Technical Approach

Four assemblies, one Revit boundary. `Metrado.Revit2027` alone references the Revit API: reads parameters by `BuiltInParameter`, converts units via `UnitUtils`, emits `ElementTakeoff` DTOs, hosts the export command and shows the completion report. `Metrado.Configuration` turns criteria text into overrides. `Metrado.Domain` holds defaults, merge, chain and openings rule as pure functions. `Metrado.Excel` writes the workbook. Only the Revit assembly needs Windows, so the core rule tests on macOS. I1 is concrete; I2/I3 get extension points; I4 gets a seam, not an implementation.

## Architecture Decisions

| # | Chosen | Rejected | Rationale |
|---|---|---|---|
| 1 Domain TFM | `netstandard2.0;net10.0` | net10-only; nsd2.0-only | Reversal is asymmetric: the second leg costs one `<TargetFrameworks>` line plus an `IsExternalInit` polyfill; retargeting a net10-only Domain for a .NET 8 adapter is a rewrite. "Portability is near-free" holds **only because the JSON parser moved out** (#5): on `netstandard2.0` `System.Text.Json` is a NuGet package pulling `System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`, `System.Text.Encodings.Web`, `Microsoft.Bcl.AsyncInterfaces`. **Cost paid: a fourth project.** Revisit if Domain needs a net10-only BCL API. |
| 1b Excel TFM | `net10.0` | multi-target | ClosedXML formatting may vary per TFM; golden files must assert one output. |
| 2 Tests | xUnit `net10.0` (Domain, Configuration, Excel) + `SmokeCommand` in Revit | NUnit; in-process runner in I1 | `dotnet test` runs on macOS ARM64, where the rule lives. The Revit API cannot be mocked (sealed types, non-constructible `Document`) and no in-process runner was confirmed for 2027/.NET 10 when this was decided, so I1 ships `SmokeCommand`, run by the development harness. Two runners now claim 2027/.NET 10 support (Open Questions). **Decided (2.8, PR 26): the smoke layer stays on the development harness**, which has run it unattended since PR 18 (it starts Revit, answers the unsigned-add-in prompts, runs `SmokeCommand` by its ribbon id and reads its report). A runner is not adopted for it: isolation and the dependency closure, its two main checks, are facts about the context Revit loads the add-in into from its own manifest, and a test runner loads the tests, and Metrado with them, through its own add-in and context, replacing the very load the checks verify. Where a runner would pay is the Revit-bound readers, exercised today only by the harness's probes and the host exports; I3 decides that by trying one against the probes' ground truth. |
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
| `Found(text)`, it parses, and a category extraction reads names a source extraction does not read for it (PR 24, after review) | the command's own `ConfigError` from `ReadableSources`, naming the category, the sources and what extraction reads — run stops before the model is read, no workbook | — |

**I1 staging.** The criteria-file requirement is tagged I2 and its reader is task 2.1, so I1 ships no parser and the `Found` row above has no implementation yet. I1 therefore answers `Found` with `Err(ConfigError)` naming the file and stating that this version cannot read criteria files — because the specification's "MUST NOT silently fall back to defaults when a file was supplied" is unconditional and does not ask *why* the file could not be honoured. That error carries no `ConfigLocation`: nothing was parsed, and reporting line 0 would send the estimator hunting a syntax error in a file that is probably valid. Task 2.1 replaces that one branch.

`EffectiveCriteria` keeps `Source` and `Path` in agreement by construction. A `File` run must name its file, and `BuiltInDefaults` must not carry one — otherwise the completion report reads "criteria from criteria.json" for a run that honoured no file, which is the silent fallback wearing a filename. The path a caller probed is a different fact from the path in force, and resolution drops it on the `Absent` branch.

`Merge` is per-category then per-field coalesce: a category absent from the file keeps its default entirely; a present category inherits every field left `null` — threshold, mode, unit and sources alike. Unknown category names are rejected before merging, so a typo fails loudly instead of being ignored.

**Where the files live (decided by the user, PR 17).** The criteria file is `metrado.criteria.json` **beside the model**, so criteria are versioned and shared with the project they price. The workbook is written **beside the model too, and never over an existing file**: `<model> - metrado <yyyy-MM-dd HHmm>.xlsx`, with `(2)`, `(3)` on a clash, because estimators fill unit prices into the exported copy. **"The model" is the file the estimator opened, a workshared local copy included.** The first review of PR 17 moved the export beside the central model; the second showed that did more harm than good and it was undone: the central's path is recorded inside the `.rvt` and travels with every copy, so a model received from another firm pointed the export at that firm's folder, a local copy opened offline could not export at all, and two copies exporting in the same minute raced for one name in the central's folder. The limit this leaves: a criteria file kept only beside the central is not read, and the dialog, which names the folder it wrote to, says no criteria file was found. A model never saved is asked to be saved; a cloud model has no folder on disk and is told to copy the model to one (a workshared one opened detached first), because saving again never changes the answer. Either way nothing is written. **Files reach the folder by rename:** before the model is read, the workbook is reserved under a temporary name in the model's folder and renamed once there, which proves the folder allows what the commit needs (creating a file, and a rename, which deletes the old name); a folder that refuses either stops the export at once. The files are written, flushed to the disk, and renamed into place only when complete, the rename never replacing an existing file (the name's race guard; `FileMode.CreateNew` guards only the temporary names). A file failure (a folder that refuses, a full disk, a name taken meanwhile) leaves nothing under either name and says so in Metrado's own dialog; a folder that lets files be created but not deleted keeps one empty temporary file, which it lets no one remove. Any other failure, such as a write refusal during the model read, still ends in Revit's own failure dialog, with nothing written.

**The built-in criteria (task 2.2, PR 20).** One per category the add-in measures, all six extracted since task 2.6; every field can be overridden per category by the criteria file. The completion dialog lists all six, a zero exclusive threshold as "every opening is deducted".

| Category | Unit | Sources | Threshold | Mode |
|---|---|---|---|---|
| Walls | m2 | `HOST_AREA_COMPUTED` | 1.0 m2 | exclusive |
| Floors | m2 | `HOST_AREA_COMPUTED` | 1.0 m2 | exclusive |
| Roofs | m2 | `HOST_AREA_COMPUTED` | 1.0 m2 | exclusive |
| Railings | m | `CURVE_ELEM_LENGTH` | 0 m | exclusive |
| Doors | u | none (counted, N1) | 0 u | exclusive |
| Windows | u | none (counted, N1) | 0 u | exclusive |

Railings are measured in linear metres because that is how a metrado states them ("ml"), so the closed set of units gained the metre, written `m`. The specifications fix no unit for railings; a file can still set another. Doors and windows read no quantity source: the empty list is N1's counted category (task 2.3), measured as one instance each, in `u`, decided before any source is looked for. A category with no source in any other unit is refused, since a count is in units. Where no opening is ever added back the threshold is zero. The sources for floors, roofs and railings are the parameters' built-in names; that each yields the expected value in a real model is task 2.6's host evidence, not this table's.

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

## Floor and Roof Openings (decided in PR 25, on host evidence)

**The constraint is the walls'.** No read-only API returns what Revit subtracted for one opening of a floor or roof, and there is no `GetInstanceCutoutFromWall` for them. What the geometry does give is the host's **upper faces**: up-facing planar faces whose edge loops are the outer boundary (counterclockwise about the normal) and the holes (clockwise), each edge meeting a face whose generating elements (`GetGeneratingElementIds`) say which cut made it. An opening's sketch stands for the opening (`Sketch.OwnerId`); the host's own sketch stands for the host.

**The decision.** A hole's area (`ComputeAreaOfCurveLoops`) stands in for Revit's deduction only while three things hold. **The upper faces add up to `HOST_AREA_COMPUTED`**, so they are what Revit measured and a hole in them is area Revit removed. **The face's own loops add up to its area** (outer less holes), so each loop's computed area is the face's. Both to a millionth of a square foot, fixed, since an area's error does not grow with the floor. The upper faces met the computed area to 7e-8 ft2 on both samples; a face's loops met its area less closely, up to 2e-4 ft2 on faces holding no hole, where the face's own area strays from loops that match its sketch, so a hole on such a face is reported rather than trusted. **The hole is one opening's alone**, with nothing of the host inside it. `SurfaceTopology` (pure) turns the faces into facts; `SurfaceOpeningPolicy` (pure) decides. An opening is an `Opening` (shaft, by face, vertical), a family instance the host hosts (a skylight, a planter set into a floor), or an unattached void cut; it is measured by the sum of its holes. A hole the host's own outline draws is an opening too, named `<host>/hole-n`. **Joined elements** — a column, a beam, another floor — are neither measured nor reported: the material there is theirs, as with walls. An opening is **reported, not measured**, when it leaves no hole of its own (it notches an edge, or cuts only from below), names a face other than its holes' sides (an edge it notches, or a seat it leaves facing up, which Revit counts as area), shares a hole with another cut or with the host's outline, has a hole holding an island of the host (an outer loop inside it in plan), a hole on a face whose loops miss the face's area, or a hole with no area, or when the upper faces do not add up; a hole of the host's own outline is reported on the same grounds; an `Opening` the host hosts that no face names is reported and also withholds the host's own holes, since its hole would look like one of them. **In-place floors and roofs** are family instances with no computed area: they are not measured, and each is named in a warning, so their absence from the budget is not silent.

**Evidence** (`tools/Metrado.HostHarness`, mode `probe-hosts`: every cutter deleted, regenerated, read and rolled back; joins undone the same way; the model never saved; the add-in's own `SurfaceReader` decisions recorded before anything is deleted). On Revit 2027.2, **Snowdon** (39 floors and roofs with holes or cuts: 35 floors, 4 roofs): the upper faces add up to the computed area on 36, to 7e-8 ft2; the reader **measured 13 openings, each matching what deleting it gives back to 5e-5 m2, none over and none under**, and 11 holes of floors' own outlines, each an inner loop of the floor's own sketch to 1e-4 m2; it reported 19 with a reason (9 on faces whose loops miss their area, 8 edge notches, 2 shared holes). The only deductions neither measured nor reported were joined elements' (five floors, a roof, a stair). **Pacific** (18: 3 floors, 15 roofs, 11 of the roofs in its secondary design options "The Charmant" and "The Dover", which the export does not read, as for walls) has no hole in any upper face; 9 of its 15 probed roofs' upper faces do not add up, all 4 in force among them, fascias and roof joins reshaping their edges; it reported 4 (2 on roofs whose faces do not add up, 2 edge notches; one of the 4 on a roof in force), and the joined fascias' and roofs' deductions stand without a word, as a joined floor's do in a wall. **The export** (the ribbon command, same build) listed Snowdon's 185 floors and 26 roofs and Pacific's 16 and 8, every one unclassified since neither sample codes them, with the same reported openings as warnings (19 and 1); neither sample has an in-place floor or roof, so that warning is proved by its tests alone.

**What shaped the rule.** On Snowdon one shaft's hole measured 0.3167 m2 on six hosts (0.3169 on one roof) but gave back 0.0018 m2 less when deleted on five of them. Undoing every join near it changed nothing, and deleting it left no other cut behind: what set those five apart was that their face's loops, outer less holes, missed the face's area by that same 0.0018 m2, while on the sixth they added up to 1e-15 and the hole matched the deletion exactly. The per-face check is the rule that followed; it also reports the exact holes on those faces, the price of not knowing which loop is off. On Snowdon's 1172.999 m2 slab a 7.6645 m2 shaft gives back only 1.9226 m2 when deleted, because 5.7419 m2 of its loop is also a hole of the slab's own sketch; the hole's sides are partly the slab's own, so the rule reports both the shaft and that hole as shared. The review of PR 25 then found, on the pure code, three shapes no sample has that would overstate: an island of the host inside a hole, a cut that leaves a seat over a through-hole (both holes summed while Revit counts the seat), and a slack that grew with the face (above about 1800 m2 it would have let the 0.0018 m2 through). Each is now reported, and the slack is fixed.

**Limits that stay open.** A cut lying wholly inside an opening's hole names no face and cannot be seen: the hole includes it. That is right when it is a smaller opening, which is below any threshold the larger is; it would be wrong for a joined element, a column standing wholly inside a shaft below the threshold, whose footprint would be added back with the shaft. No sample has either. A hole whose every side is a joined element's face (a chase whose lining walls trim the slab) is taken as theirs: its deduction stands without a warning even when the hole is the floor's own or a shaft's. A roof's own-outline hole that straddles a ridge or hip leaves only notches, no inner loop, so it too stays deducted without a warning. Roofs whose upper faces do not add up report every opening they have. The probe is the regression instrument, as for walls.

## Material Layers (decided in PR 27, on host evidence and the user's choices)

**What Revit gives.** For a layered wall, floor or roof, `GetMaterialIds(false)` lists the materials Revit measures in it, and `GetMaterialVolume` and `GetMaterialArea` each one's volume and area; the type's `CompoundStructure` lists its layers, exterior (or top) first, with function, width and material. A read-only probe of 213 layered hosts on both samples (`probe-materials`) found the materials' volumes add up to `HOST_VOLUME_COMPUTED` to 3e-16 of it on every one; Revit gives **one entry per distinct material**, merging a material used by two layers (its area is then twice the wall's); membranes have no volume; a layer with no material is measured under the category's (Snowdon's "Default Wall"); no sample material carries a Keynote.

**Decided by the user (PR 27):** a layer line is in **m2 by default, m3 where the criteria say so** per layer function (a Peruvian metrado prices tarrajeo, enchapes, contrapisos, coberturas and masonry by area, concrete by volume; a membrane is always m2); a layer line is **coded by its material** — its Keynote, then the nominated shared parameter, else unclassified — never by the host's code, since a partida is one priced item and a wall is several; there is **one line per material, naming the layers it covers**, never a material split by width, which would invent a distribution; and **paint is warned about per painted host, not taken off**.

**The switch.** Material-layer takeoff is on per category in the criteria file, a `layers` field of a Walls, Floors or Roofs entry: `true` (every function in m2), an object such as `{ "Structure": "m3" }`, or `false`; left out, it inherits the built-in value, off for all six, so a run with no file reads no layer. Refused, with file, line and position: an unknown function, a unit other than m2 or m3, a membrane in m3, a function twice, any other value; and `layers` on Railings, Doors or Windows.

**Extraction (task 3.1, PR 27).** Only for a category the criteria layer, each host's reading gains its layers (`MaterialLayerReader`, read-only) and crosses the seam (`LayerTakeoff`, pure) as the whole volume `HOST_VOLUME_COMPUTED`, then each material as one `MATERIAL_VOLUME` and one `MATERIAL_AREA` entry carrying a `MaterialRef` with the material's own codes, beside the unchanged whole area; and the type's layers as a `LayerStructure` with the conditions under which an opening's share of a layer is not its area times the layer's width (wrapping at inserts, by the wall type or by any hosted door or window type whose Wall Closure is other than By host, which overrides the wall's; a vertically compound type; a variable layer; a shape-edited floor or roof; a structural deck). A value Revit could not give is left out, never read as zero; a type with no compound structure gives no layers and is measured whole.

**Measurement (task 3.2).** An element is measured either wholly by layer or wholly whole, never in part: before any layer line exists, every layer of positive width must be attributed to a material Revit measures and every measured material to a layer, each material must have one unit, and the materials' volumes must add up to the whole volume within **1e-6 m3** (fixed, as PR 25 learnt a slack growing with size lets errors through; 285 times the rounding bound, 3,100 times below the smallest real material seen). Otherwise the element is measured whole, as before, with a warning naming the fault. Each opening is decided once, at the host's threshold. **An opening added back returns to each m2 line its share, the opening's area times the material's layer count**, where no condition holds. **An m3 line keeps Revit's deduction** of every opening, as does every line under a condition, and a warning says how much each keeps. PR 28 decided this on ground truth (below): the area share is exact, the volume share is not.

**Evidence** (`tools/Metrado.HostHarness`, modes `probe-materials` and `probe-layers`; read-only but for a Keynote set in a transaction that is rolled back; the model never saved). On Revit 2027.2 every wall, floor and roof in force read through Metrado's own reader — **1367 on Snowdon and 128 on Pacific** — reconciles, the materials' volumes meeting the whole to 2e-9 m3 at worst (the seam's rounding), with every layer of positive width attributed to a material Revit measures and every such material to a layer; none lacks a compound structure. A Keynote set on one material reads back through the reader on both samples. The conditions found: on Snowdon 1 wall wrapping at inserts, 36 hosts with a variable layer and 26 shape-edited floors or roofs; on Pacific 42 walls wrapping (41 by their type, one hosting a window whose Wall Closure is "Neither") and 6 shape-edited. Of the inserts in walls in force, Snowdon's 315 types with a Wall Closure all say By host (stored as 0), Pacific's 49 By host and 1 Neither (1); the rest carry none. The probe's first run caught `IsVerticallyCompound` true of every wall type, breaks or not; the fact is read as not `IsVerticallyHomogeneous()`, true of no host on either sample. An export with no criteria file, on this build, wrote the same lines and warnings as PR 26 (7 lines and 251 warnings on Snowdon, 0 and 31 on Pacific): no layer is read unless a criterion asks.

**The share, held to deletion (PR 28).** The probe `probe-layer-openings` deletes each opening Metrado measures in a layered host in force, in a transaction that is rolled back, and reads what the host and each material get back. Over 150 Snowdon and 35 Pacific openings, **each material's area gets back the host's gain times its layer count to 1e-13**, the gate the design set (1e-6); its **volume gets back the gain times its summed width only to 0.56%**, on either side, so a volume share would sometimes add back material Revit did not remove, and m3 lines keep the deduction instead. Ten Pacific openings in walls that wrap at inserts held the area share exactly, and a negative control that set a type to wrap changed nothing: the samples' inserts do not close the wall, so wrapping is kept as a condition on argument, not evidence. The same run found one wall opening, a rectangular opening in a Snowdon 24" foundation wall, measured at 21.74 m2 by the wall rule of PR 16 while deleting it gives back 19.94 m2: an overstatement that far above any threshold changes no metrado, but the rule allows none, and its cause (probably a cut hidden inside the opening, as for floors) is open.

**Limits that stay open.** One line per material, not per physical layer: plaster on both faces is one line unless modelled as two materials. A material's area follows join cleanup, so masonry measured by its Structure layer can read a few percent below the whole wall (down to 0.95 on Snowdon walls, 0.89 on Pacific's). One unit per function per category, and one threshold for all of a category's layers. No sample material is keyed, so layer codes are proved by unit tests and a rolled-back positive control only.

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
- [x] Does an in-process NUnit runner exist for Revit 2027/.NET 10? Decides whether I2 automates the smoke layer. **Answered (2026-09-23): yes, by their publishers; neither tried here.** `ricaun.RevitTest.TestAdapter` 1.11.x (NUnit, runs tests inside Revit through an add-in; its changelog states "Support Revit 2027 with framework `net10`" from 1.11.0, released 2026-02-27; 1.11.1 followed on 2026-04-10) and `Nice3point.TUnit.Revit` 2027.0.2 (TUnit, `net10.0-windows`, published 2026-09-15). Task 2.8 decided that neither replaces the harness-driven smoke run (Architecture Decision 2).
