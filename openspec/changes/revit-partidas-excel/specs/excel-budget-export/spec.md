# excel-budget-export Specification

## Purpose

Defines the Excel workbook the add-in produces: rows grouped *capitulo* -> *partida* -> *linea de medicion* (chapter -> budget line item -> measurement line), every measurement line traceable back to the model element that produced it.

Every requirement is tagged with the increment that delivers it (I1–I4).

## Requirements

### Requirement: Capitulo, Partida and Linea Hierarchy

**Increment**: I1

The workbook SHALL express three grouping levels: capitulo, partida, and linea de medicion. Each partida SHALL appear under exactly one capitulo, and each measurement line under exactly one partida. The grouping level of every row MUST be identifiable from the row itself, not inferred from surrounding formatting.

#### Scenario: Three levels are present and attributable

- GIVEN a model producing two capitulos, five partidas and forty measurement lines
- WHEN the workbook is written
- THEN each of the forty lines identifies its partida
- AND each of the five partidas identifies its capitulo

### Requirement: Traceability Anchor on Every Measurement Line

**Increment**: I1

Every measurement line SHALL carry the `UniqueId` of the model element it represents, in a dedicated column. The value MUST be non-empty for every line.

#### Scenario: Every line is traceable

- GIVEN any exported workbook with at least one measurement line
- WHEN the `UniqueId` column is inspected
- THEN no measurement line has an empty `UniqueId`

#### Scenario: Line can be located back in the model

- GIVEN a measurement line with a given `UniqueId`
- WHEN that identifier is looked up in the source model
- THEN it resolves to exactly one element

### Requirement: Unclassified Block

**Increment**: I1

Elements resolved as unclassified SHALL be written into an explicit, clearly labelled unclassified block, separate from the coded capitulos. The block MUST list each element with its `UniqueId`, category, family and type. Unclassified elements MUST NOT be dropped, and MUST NOT be silently merged into a coded partida.

#### Scenario: Uncoded elements land in the unclassified block

- GIVEN a model where 12 of 40 walls resolve as unclassified
- WHEN the workbook is written
- THEN the unclassified block contains 12 entries
- AND the coded capitulos contain the other 28 walls

#### Scenario: No unclassified elements

- GIVEN a model where every element resolves to a code
- WHEN the workbook is written
- THEN the unclassified block is present and explicitly reports zero entries

### Requirement: Subtotals Reconcile with Their Lines

**Increment**: I1

Each partida SHALL carry a total equal to the sum of its measurement lines, and each capitulo a total equal to the sum of its partidas, within a stated rounding tolerance.

#### Scenario: Partida total equals the sum of its lines

- GIVEN a partida with five measurement lines
- WHEN the workbook is written
- THEN the partida total equals the sum of those five metrados within tolerance

#### Scenario: Capitulo total equals the sum of its partidas

- GIVEN a capitulo containing three partidas
- WHEN the workbook is written
- THEN the capitulo total equals the sum of the three partida totals within tolerance

### Requirement: Workbook Written Without Excel Installed

**Increment**: I1

The workbook SHALL be written directly as an Office Open XML `.xlsx` file. The export MUST NOT require Microsoft Excel, or any Office automation interface, to be installed on the machine running Revit.

#### Scenario: Export on a machine without Excel

- GIVEN a Windows host with Revit 2027 and no Microsoft Excel installation
- WHEN the export runs
- THEN an `.xlsx` file is produced
- AND it opens in a standards-compliant spreadsheet application

### Requirement: Deterministic Output Ordering

**Increment**: I1

For identical input the writer SHALL produce identical row ordering. Rows MUST be ordered by capitulo, then partida code, then a stable per-line key. Ordering MUST NOT depend on Revit's element iteration order.

#### Scenario: Same input produces identical output

- GIVEN one fixed set of measurement data
- WHEN the workbook is written twice
- THEN the cell contents of both workbooks are identical row for row

#### Scenario: Golden-file comparison is viable

- GIVEN a stored reference workbook for a fixture data set
- WHEN the writer runs over that fixture
- THEN the produced content matches the reference

### Requirement: Empty Result Produces a Valid Workbook

**Increment**: I1

An export over a model with no measurable elements SHALL still produce a valid workbook containing the headers and an explicit zero-count summary. The export MUST NOT crash, and MUST NOT leave a partially written or missing file.

#### Scenario: Model with zero measurable elements

- GIVEN a model containing no elements in any supported category
- WHEN the export runs
- THEN a valid `.xlsx` file is produced
- AND it states that zero measurement lines were exported

### Requirement: Unit Reported per Partida

**Increment**: I2

Each partida SHALL report the unit of measurement its total is expressed in, as resolved by the category criterion. Lines within one partida MUST share a single unit; a mismatch MUST raise a validation warning rather than sum incompatible quantities.

#### Scenario: Partida carries its unit

- GIVEN a wall partida measured in square metres
- WHEN the workbook is written
- THEN the partida row reports unit `m2`

#### Scenario: Incompatible units are not summed

- GIVEN measurement lines with differing units resolving to one partida
- WHEN the workbook is written
- THEN a validation warning is raised
- AND the incompatible quantities are not added together

### Requirement: Material-Layer Rows

**Increment**: I3

When material-layer takeoff is enabled, the writer SHALL emit one measurement line per material layer, each identifying its material and retaining the host element's `UniqueId`.

#### Scenario: Layered wall produces one line per layer

- GIVEN a wall with three material layers and layer takeoff enabled
- WHEN the workbook is written
- THEN three measurement lines are emitted for that wall
- AND all three carry the same host `UniqueId` and distinct material names
