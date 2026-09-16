# metrado-measurement Specification

## Purpose

Defines how a raw Revit quantity becomes a valid *metrado* (measured quantity for budgeting). This capability carries the single most important behaviour in the product: Revit already subtracts every opening from computed areas and volumes, so the raw parameter is geometry, not metrado, and exporting it unchanged ships a wrong budget that looks plausible.

Every requirement is tagged with the increment that delivers it (I1–I4).

## Requirements

### Requirement: Openings Threshold Correction

**Increment**: I1 (both modes implemented, `exclusive` default), exposed as user configuration in I2

For any element measured by a Revit-computed area or volume, the metrado SHALL be computed as:

```
metrado = rawQuantity + Σ q(o) for every opening o where q(o) ≺ threshold
```

where `rawQuantity` is the Revit-computed value with all openings already subtracted, `q(o)` is that opening's individual quantity, and `≺` is the configured boundary comparison.

The boundary comparison SHALL be configurable with exactly two modes, because metrado norms differ in their wording and the reference product's documentation does not settle the equality case:

| Mode | Comparison | Norm wording it matches |
|---|---|---|
| `exclusive` (default) | `q(o) < threshold` | "no se descuentan los vanos de área **menor a** X" |
| `inclusive` | `q(o) ≤ threshold` | "no se descuentan los vanos **hasta** X" |

The default SHALL be `exclusive`. The mode affects ONLY openings whose quantity is exactly equal to the threshold; every other opening behaves identically under both modes. The system MUST NOT expose any third mode or free-form comparison expression.

The active mode MUST be recorded on the measurement result and surfaced in the exported workbook, so a reviewer can tell which convention produced the numbers. A configurable rule whose convention is not reported produces two different defensible budgets from one model with no way to distinguish them.

Openings MUST be evaluated individually; the rule MUST NOT compare the sum of openings against the threshold.

#### Scenario: Sub-threshold opening is added back

- GIVEN a wall with raw area 18.0 m² and one opening of 0.6 m²
- AND a threshold of 1.0 m²
- WHEN the metrado is computed
- THEN the metrado is 18.6 m²

#### Scenario: Opening exactly at the threshold, exclusive mode

- GIVEN a wall with raw area 18.0 m² and one opening of exactly 1.0 m²
- AND a threshold of 1.0 m²
- AND the boundary mode is `exclusive`
- WHEN the metrado is computed
- THEN the metrado is 18.0 m²

#### Scenario: Opening exactly at the threshold, inclusive mode

- GIVEN a wall with raw area 18.0 m² and one opening of exactly 1.0 m²
- AND a threshold of 1.0 m²
- AND the boundary mode is `inclusive`
- WHEN the metrado is computed
- THEN the metrado is 19.0 m²

#### Scenario: Boundary mode is the only difference between the two modes

- GIVEN a wall with openings of 0.4, 1.0 and 2.5 m²
- AND a threshold of 1.0 m²
- WHEN the metrado is computed under `exclusive` and then under `inclusive`
- THEN the two results differ by exactly 1.0 m²
- AND the 0.4 m² opening is added back under both modes
- AND the 2.5 m² opening stays deducted under both modes

#### Scenario: Active boundary mode is reported

- GIVEN any export run
- WHEN the workbook is produced
- THEN the workbook records which boundary mode produced the metrado

#### Scenario: Above-threshold opening stays deducted

- GIVEN a wall with raw area 16.0 m² and one opening of 2.1 m²
- AND a threshold of 1.0 m²
- WHEN the metrado is computed
- THEN the metrado is 16.0 m²

#### Scenario: Mixed openings on one element

- GIVEN a wall with raw area 15.0 m² and openings of 0.4, 0.9 and 2.5 m²
- AND a threshold of 1.0 m²
- WHEN the metrado is computed
- THEN the metrado is 16.3 m²

#### Scenario: Element with no openings

- GIVEN a wall with raw area 20.0 m² and no openings
- WHEN the metrado is computed
- THEN the metrado equals 20.0 m²

#### Scenario: Zero threshold disables the correction

- GIVEN any element with openings
- AND a threshold of 0
- WHEN the metrado is computed
- THEN the metrado equals the raw quantity

### Requirement: Corrected Metrado Is Bounded by the Gross Quantity

**Increment**: I1

The corrected metrado MUST NOT exceed the element's gross quantity, defined as `rawQuantity + Σ q(o)` over all openings. A computation that would exceed it indicates inconsistent extraction input, and the system SHALL clamp to the gross quantity and record a validation warning rather than emit an impossible metrado.

#### Scenario: Openings consume the whole element

- GIVEN a wall whose raw area is 0.0 m² because its openings cover its full face
- AND every opening is below the threshold
- WHEN the metrado is computed
- THEN the metrado equals the gross area of the wall
- AND it is never greater

#### Scenario: Inconsistent opening data is reported, not exported

- GIVEN extraction data whose opening quantities exceed the gross quantity
- WHEN the metrado is computed
- THEN the metrado is clamped to the gross quantity
- AND a validation warning identifies the element by `UniqueId`

### Requirement: Metrado Is Distinguishable from the Raw Revit Quantity

**Increment**: I1

The computed metrado SHALL be carried separately from the raw Revit quantity, and the raw quantity MUST NOT be substituted for the metrado anywhere downstream.

#### Scenario: Correction is observable in the output

- GIVEN a model whose walls have sub-threshold openings
- WHEN the export runs
- THEN at least one exported metrado differs from the corresponding raw Revit area

### Requirement: Unit Conversion from Revit Internal Units

**Increment**: I1

Revit stores lengths, areas and volumes in imperial internal units regardless of the project's display units. Conversion SHALL be performed in the Revit-facing layer using `UnitUtils.ConvertFromInternalUnits` with `UnitTypeId`/`SpecTypeId` identifiers. Every quantity crossing the seam MUST declare its unit explicitly, and the threshold comparison and the metrado arithmetic MUST be performed in one consistent unit system.

#### Scenario: Area converted from square feet to square metres

- GIVEN a wall whose internal computed area is 107.639 ft²
- WHEN the quantity is converted for measurement in square metres
- THEN the value is 10.0 m² within rounding tolerance

#### Scenario: Threshold and opening quantities share a unit

- GIVEN a threshold expressed in m²
- WHEN openings are compared against it
- THEN the opening quantities used in the comparison are in m²
- AND no imperial value is compared against a metric threshold

### Requirement: Per-Category Measurement Criterion

**Increment**: I1 (fixed default for Walls), extended in I2

Each category SHALL have a measurement criterion defining its output unit and the ordered list of quantity sources to read; the first source with a value SHALL be used. In I1 the criterion for Walls is fixed: area in m² with the openings threshold applied. In I2 criteria SHALL cover the six supported categories and become user-configurable.

#### Scenario: Default criterion applies with no configuration

- GIVEN a model with walls and no user configuration
- WHEN the metrado is computed
- THEN walls are measured as area in m² with the openings correction applied

#### Scenario: First available source wins

- GIVEN a criterion listing two ordered quantity sources
- AND an element where the first source has no value
- WHEN the metrado is computed
- THEN the second source is used

#### Scenario: No source yields a value

- GIVEN an element for which no source in its criterion has a value
- WHEN the metrado is computed
- THEN the element is reported with no metrado and a validation warning
- AND it MUST NOT be silently assigned zero as if it were measured

### Requirement: Count-Based Measurement

**Increment**: I2

Categories measured by count SHALL have no quantity source; their metrado is the number of qualifying instances, with unit `u`.

#### Scenario: Doors are counted

- GIVEN 14 door instances resolving to a single partida
- WHEN the metrado is computed
- THEN the partida total is 14 with unit `u`

### Requirement: Material-Layer Metrado

**Increment**: I3

When material-layer takeoff is enabled, an element SHALL be measurable per material layer. The sum of the layer quantities for a given element MUST reconcile with that element's whole-element quantity within a stated tolerance, and a discrepancy MUST raise a validation warning.

#### Scenario: Layer quantities reconcile with the element total

- GIVEN a compound wall with three material layers
- WHEN layer metrado is computed
- THEN the sum of the layer volumes reconciles with the wall volume within tolerance

### Requirement: Empty Measurement Set

**Increment**: I1

A model containing no elements in any supported category SHALL produce an empty but well-formed measurement result. The system MUST NOT fail, and MUST NOT report a success that implies quantities were found.

#### Scenario: Model with zero walls

- GIVEN a model containing no walls
- WHEN the metrado is computed for I1 scope
- THEN the result contains zero measurement lines
- AND the run reports that no measurable elements were found
