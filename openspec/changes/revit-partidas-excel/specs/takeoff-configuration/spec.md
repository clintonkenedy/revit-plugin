# takeoff-configuration Specification

## Purpose

Defines how measurement behaviour is configured: the per-category criteria, the openings threshold, and the saved configurations an estimator reuses across models. The governing principle is that the criteria table is data, not code, and not something that exists only inside a dialog.

Every requirement is tagged with the increment that delivers it (I1–I4).

## Requirements

### Requirement: Usable Defaults Without Any Configuration

**Increment**: I1

The add-in SHALL ship built-in default criteria and a default openings threshold, and SHALL run correctly with no configuration file present. Absence of configuration MUST NOT be an error.

#### Scenario: First run with no configuration

- GIVEN a fresh installation with no configuration file
- WHEN the export runs over a model containing walls
- THEN walls are measured with the built-in default criterion and default threshold
- AND no configuration error is reported

#### Scenario: Effective configuration is reported

- GIVEN any export run
- WHEN it completes
- THEN the run reports which criteria, threshold values and boundary modes were actually applied

### Requirement: External, Versionable Criteria File

**Increment**: I2

Criteria SHALL be expressible in an external, human-editable, text-based configuration file that a user can version and share. Values present in the file SHALL override the built-in defaults per category; categories absent from the file SHALL keep their defaults. The criteria table MUST NOT be hardcoded as the only source of truth, and MUST NOT be reachable only through a dialog.

The exact file format and schema are a design decision and are deliberately not fixed here.

#### Scenario: File overrides one category only

- GIVEN a configuration file defining a criterion for Floors only
- WHEN the export runs over a model with walls and floors
- THEN floors use the file criterion
- AND walls use the built-in default

#### Scenario: Missing file falls back to defaults

- GIVEN a configuration file path that does not exist
- WHEN the export runs
- THEN built-in defaults are applied
- AND the run reports that no configuration file was found

### Requirement: Invalid Configuration Fails Loudly

**Increment**: I2

A configuration file that exists but cannot be parsed, or that declares an unknown category or an unsupported unit, SHALL cause the run to stop with a message identifying the offending entry. The system MUST NOT silently fall back to defaults when a file was supplied, because a silently ignored configuration produces a confidently wrong budget.

#### Scenario: Malformed configuration file

- GIVEN a configuration file with invalid syntax
- WHEN the export is requested
- THEN the run stops before writing any workbook
- AND the error message identifies the file and the failing location

#### Scenario: Unknown category in configuration

- GIVEN a configuration file declaring a criterion for a category the add-in does not support
- WHEN the export is requested
- THEN the run stops with an error naming that category
- AND no partial workbook is written

### Requirement: Openings Threshold Is Configurable per Category

**Increment**: I2

The openings threshold SHALL be configurable independently per category, expressed in that category's measurement unit. A category without an explicit threshold SHALL inherit the default. A negative threshold MUST be rejected as invalid configuration.

#### Scenario: Different thresholds per category

- GIVEN a configuration setting the wall threshold to 1.0 m² and the floor threshold to 0.5 m²
- WHEN the metrado is computed
- THEN wall openings below 1.0 m² are added back
- AND floor openings below 0.5 m² are added back

#### Scenario: Negative threshold is rejected

- GIVEN a configuration with a threshold of -1.0
- WHEN the configuration is loaded
- THEN loading fails with an error naming the category and the invalid value

### Requirement: Openings Boundary Mode Is Configurable

**Increment**: I2 (both modes already implemented in I1; this exposes the choice)

The openings boundary mode SHALL be configurable per category, with a closed domain of exactly two values, `exclusive` and `inclusive`, as defined in the `metrado-measurement` capability. A category without an explicit mode SHALL inherit the default, which is `exclusive`. Any value outside that closed domain MUST be rejected as invalid configuration.

The mode exists because metrado norms differ in wording — "vanos de área menor a X" versus "vanos hasta X" — and the reference product's documentation does not settle the equality case. It MUST NOT be generalised into a free-form comparison expression or an arbitrary operator setting.

#### Scenario: Boundary mode set per category

- GIVEN a configuration setting the wall boundary mode to `inclusive` and leaving floors unset
- AND a threshold of 1.0 m² for both
- WHEN the metrado is computed for a wall and a floor each having one opening of exactly 1.0 m²
- THEN the wall opening is added back
- AND the floor opening is not added back

#### Scenario: Invalid boundary mode is rejected

- GIVEN a configuration declaring a boundary mode that is neither `exclusive` nor `inclusive`
- WHEN the configuration is loaded
- THEN loading fails with an error naming the category and the invalid value
- AND the error lists the two accepted values

### Requirement: Saved, Reusable Configurations

**Increment**: I3

A complete configuration — criteria, thresholds, codification chain settings and material-layer settings — SHALL be nameable, saveable and reloadable. Reloading a saved configuration and re-running over an unchanged model MUST reproduce identical metrado values.

#### Scenario: Saved configuration reproduces its results

- GIVEN a configuration saved under a name, and an export produced with it
- WHEN the same configuration is reloaded and the export is re-run over the unchanged model
- THEN every metrado value is identical to the first run

#### Scenario: Switching configurations changes results predictably

- GIVEN two saved configurations differing only in the wall openings threshold
- WHEN the export runs with each in turn over the same model
- THEN the wall metrados differ only by the openings added back under each threshold

#### Scenario: Configuration is portable between models

- GIVEN a configuration saved on one model
- WHEN it is loaded while a different model is open
- THEN it loads successfully
- AND categories absent from the new model are skipped without error
