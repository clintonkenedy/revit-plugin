# model-validation-warnings Specification

## Purpose

Defines the correctness pass that tells the user their model is not ready. Incorrectly modelled input does not throw in the Revit API — it silently yields a plausible but wrong metrado. This capability exists so that garbage input is visible rather than confidently exported.

Every requirement is tagged with the increment that delivers it (I1–I4).

## Requirements

### Requirement: Unclassified Count Surfaced at Export Time

**Increment**: I1

The export SHALL report, at completion, how many elements resolved as unclassified and how many were exported in total. This report MUST be visible to the user without opening the workbook. This is the seed of the validation pass; the full pass arrives in I3.

#### Scenario: Unclassified elements are announced

- GIVEN a model where 12 of 40 walls resolve as unclassified
- WHEN the export completes
- THEN the user is shown that 40 elements were exported and 12 are unclassified

#### Scenario: Clean model reports zero

- GIVEN a model where every element resolves to a code
- WHEN the export completes
- THEN the user is shown an unclassified count of zero

### Requirement: Pre-Export Validation Pass

**Increment**: I3

Before the workbook is written, the system SHALL run a validation pass over the extracted data and produce a list of warnings. Each warning MUST identify the offending element by `UniqueId`, its category, family and type, and MUST state the condition detected.

#### Scenario: Warnings are attributable to elements

- GIVEN a model containing at least one suspect element
- WHEN validation runs
- THEN each warning names the element `UniqueId` and the condition detected

#### Scenario: Clean model produces no warnings

- GIVEN a model with no detectable correctness problems
- WHEN validation runs
- THEN the warning list is empty

### Requirement: Suspect Metrado Detection

**Increment**: I3

Validation SHALL detect, at minimum: an element whose opening quantities exceed its gross quantity; an element whose metrado is zero or negative; an element in a supported category with no applicable measurement criterion; an element whose criterion yielded no value from any source; and material-layer quantities that fail to reconcile with the whole-element quantity.

#### Scenario: Openings exceed the gross quantity

- GIVEN an element whose opening quantities sum above its gross quantity
- WHEN validation runs
- THEN a warning identifies that element and the inconsistency

#### Scenario: Zero metrado on a modelled element

- GIVEN a wall present in the model whose computed metrado is zero
- WHEN validation runs
- THEN a warning identifies that element

#### Scenario: No applicable criterion

- GIVEN an element in a supported category for which no criterion resolves
- WHEN validation runs
- THEN a warning identifies the element and the missing criterion

### Requirement: Warnings Do Not Block the Export

**Increment**: I3

Validation warnings SHALL be advisory. The export MUST still produce a workbook when warnings exist, because the warnings themselves are the user's instruction list for fixing the model. Only a configuration error, as defined in `takeoff-configuration`, stops a run.

#### Scenario: Export proceeds despite warnings

- GIVEN a validation pass that produced fifteen warnings
- WHEN the export continues
- THEN a workbook is written
- AND the user is informed that fifteen warnings were raised

#### Scenario: Warnings are recoverable in the output

- GIVEN an export run that raised warnings
- WHEN the user inspects the run result
- THEN the full warning list is available alongside the workbook, not only as a count
