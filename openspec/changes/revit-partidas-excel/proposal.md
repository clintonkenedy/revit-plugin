# Proposal: Revit partidas + metrado export to Excel

## Intent

Revit's `Area`/`Volume` are geometry, not metrado: Revit subtracts every opening, so raw export ships wrong budgets. This add-in corrects quantities into Excel grouped capitulo -> partida -> linea de medicion (chapter, line item, measurement line). Reference: Cost-It, but the output is Excel, not a budget engine.

## Scope

S3, as ordered, independently shippable increments. **Target: Revit 2027 only** — the sole installed version; `net10.0-windows`.

### In Scope

| # | Increment | Delivers |
|---|---|---|
| I1 | Vertical slice | Walls -> `ASSEMBLY_CODE` -> openings threshold -> Excel with `UniqueId` + unclassified block; sets the Revit/Domain/Excel seam; native `.addin` isolation |
| I2 | Estimator-ready | Criteria file, six categories, Keynote fallback, units |
| I3 | Model fidelity | Material-layer takeoff, validation warnings, saved configurations |
| I4 | Rules engine | Category+filter -> code; **gated** until I2/I3 yield concrete rules |

I1 ships end to end before abstraction; a rules engine built before real rules exist is premature abstraction.

### Out of Scope

- Permanent: bidirectional sync, price databases, 4D/5D.
- Deferred: further Revit versions (2025 needs a guarded codification accessor + ILRepack; 2026 needs a .NET 8 adapter); MSI installer.

## Capabilities

### New Capabilities
- `revit-model-extraction`: 2027 adapter, DTO seam
- `partida-codification`: Assembly Code -> Keynote -> shared param -> rule -> unclassified
- `metrado-measurement`: per-category criteria, openings threshold
- `excel-budget-export`: capitulo/partida/linea writer
- `takeoff-configuration`: criteria file, saved configurations
- `model-validation-warnings`: pre-export correctness pass

### Modified Capabilities
None — `openspec/specs/` is empty.

## Approach

`src/*.Revit` (`net10.0-windows`) alone references the Revit API, emitting DTOs of raw imperial values plus opening areas. `src/*.Domain` and `src/*.Excel` hold the rules and unit-test on macOS (.NET SDK 10.0.400). Excel via ClosedXML (MIT). Dependency isolation via `.addin` `<ManifestSettings><UseRevitContext>False</UseRevitContext>`.

**Design must decide — Domain/Excel target framework.** `net10.0` tests natively on macOS with full modern C#/BCL; `netstandard2.0` preserves reuse if a Revit 2026 (.NET 8) adapter is added; `netstandard2.0;net10.0` buys both cheaply. Commercial intent is undecided and more versions may follow, so design decides this explicitly rather than defaulting.

## Affected Areas

| Area | Impact | Description |
|---|---|---|
| `src/*.Domain/` | New | Criteria, openings rule, resolver chain |
| `src/*.Excel/` | New | Workbook writer, golden-file tested |
| `src/*.Revit/` | New | `IExternalApplication`, ribbon, adapter, `.addin` |
| `openspec/config.yaml` | Modified | Projects, test command |

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Host licence is EDUCATION (NON COMMERCIAL) | High | Fine for learning/portfolio; resolve before any commercial distribution |
| Empty Assembly Code in models | High | Resolver chain; unclassified block |
| Isolated context must ship complete dependencies | Med | Verify ClosedXML transitive deps deploy; smoke-load in I1 |
| Imperial units mis-scale metrado | Med | `UnitUtils` at adapter, asserted |
| .NET 10 Revit tooling is new; few templates | Med | Keep Revit layer thin; treat build setup as I1 work |
| Single-version reach; no 2025/2026 host to test a back-port | Med | Revit types stay behind the adapter, so a version is added, not retrofitted |
| RDP-only build drift | Med | Git transport; Windows authoritative |

## Rollback Plan

Each increment ships as its own PR. Rollback is deleting the `.addin` manifest; the add-in never writes to the document. If `UseRevitContext=False` misbehaves, drop to bare `DocumentFormat.OpenXml` to shrink the dependency surface.

## Dependencies

- Windows host with **Revit 2027.2** (EDUCATION licence); NuGet `Nice3point.Revit.Api.*` `2027.2.0` (`net10.0-windows7.0`), ClosedXML
- macOS .NET SDK 10.0.400 for the Domain/Excel test loop
- Needs the Windows host to settle, in I1: `.addin` install paths for 2027; whether *Area and Volume Computations* gates `ROOM_AREA`; test framework

## Success Criteria

- [ ] I1 exports capitulo/partida/linea rows carrying `UniqueId`; uncoded elements land in the unclassified block
- [ ] Sub-threshold openings added back; result differs from raw `Area`
- [ ] Add-in loads on Revit 2027 in an isolated context (`UseRevitContext=False`)
- [ ] Domain and Excel tests green on macOS
