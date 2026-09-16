# revit-model-extraction Specification

## Purpose

Defines how the add-in loads inside Revit 2027 and how it reads model data across the one boundary that touches the Revit API. Everything downstream consumes plain data transfer objects (DTOs), never Revit types.

Every requirement is tagged with the increment that delivers it (I1–I4).

## Requirements

### Requirement: Revit 2027 Add-in Registration in an Isolated Load Context

**Increment**: I1

The add-in SHALL be registered through a `.addin` manifest declaring `<ManifestSettings><UseRevitContext>False</UseRevitContext><ContextName>...</ContextName></ManifestSettings>`, and SHALL target Revit 2027 only. The add-in MUST NOT rely on any dependency being pre-loaded by Revit or by another add-in.

#### Scenario: Add-in loads in an isolated context

- GIVEN a Windows host with Revit 2027 installed
- WHEN the manifest is deployed to a Revit 2027 add-ins folder and Revit starts
- THEN the add-in loads without error and its command is reachable from the ribbon
- AND its dependencies resolve from its own deployment folder

#### Scenario: Incomplete dependency deployment is detectable

- GIVEN the isolated context is enabled
- WHEN a required dependency assembly is absent from the deployment folder
- THEN loading fails with a diagnosable error naming the missing assembly
- AND the failure MUST NOT be silently swallowed

### Requirement: Revit-Free Seam via Element Takeoff DTOs

**Increment**: I1

Only the Revit-facing assembly SHALL reference the Revit API. It MUST emit, per extracted element, a DTO carrying at minimum: the element `UniqueId`, category name, family name, type name, the codification parameter values it read, the raw computed quantities, and the individual areas of the element's openings. Revit types MUST NOT appear in the public surface of the measurement or export layers.

#### Scenario: Measurement layer compiles without the Revit API

- GIVEN the measurement and export assemblies
- WHEN they are built on a machine with no Revit installation and no Revit reference assemblies
- THEN the build succeeds

#### Scenario: Traceability anchor is always populated

- GIVEN any element included in extraction
- WHEN its DTO is produced
- THEN `UniqueId` is non-empty and equals `Element.UniqueId` for that element

### Requirement: Parameter Access by Built-in Identifier Only

**Increment**: I1

Parameters SHALL be resolved by `BuiltInParameter` or `ForgeTypeId`. The add-in MUST NOT look up any parameter by its user-visible display name. The codification parameter for Revit 2027 is `BuiltInParameter.ASSEMBLY_CODE`.

#### Scenario: Localised Revit UI does not change extraction

- GIVEN a Revit 2027 session whose UI language is Spanish, so the codification parameter displays as "Código de montaje"
- WHEN extraction runs over a model whose walls carry assembly codes
- THEN the same codes are read as in an English session

### Requirement: Per-Element Opening Enumeration

**Increment**: I1

For every host element measured by area or volume, the adapter MUST emit both the raw Revit-computed quantity and the individual quantity of each opening that Revit subtracted from it. The adapter MUST NOT pre-aggregate opening quantities into a single sum, because the correction rule depends on each opening's individual size.

#### Scenario: Wall with several openings

- GIVEN a wall hosting one door and two windows
- WHEN the wall is extracted
- THEN its DTO carries three distinct opening areas
- AND the raw area is Revit's computed area with all three already subtracted

#### Scenario: Wall with no openings

- GIVEN a wall hosting no openings
- WHEN the wall is extracted
- THEN its opening list is empty, not null

### Requirement: Read-Only Document Access

**Increment**: I1

Extraction SHALL NOT modify the Revit document. The add-in MUST NOT open a write transaction, and MUST NOT mark the document as modified.

#### Scenario: Export leaves the document untouched

- GIVEN an unmodified open model
- WHEN a full extraction and export completes
- THEN Revit does not report unsaved changes for that document

### Requirement: Multi-Category Extraction

**Increment**: I2

Extraction SHALL cover Walls, Floors, Roofs, Railings, Doors and Windows. Categories absent from the model MUST be skipped without error.

#### Scenario: Model missing a supported category

- GIVEN a model containing walls and floors but no railings
- WHEN extraction runs
- THEN walls and floors are extracted and no error is raised for railings

### Requirement: Material-Layer Extraction

**Increment**: I3

For compound elements, the adapter SHALL additionally emit per-material quantities obtained from the element's material identifiers, so that a layered element can be measured layer by layer rather than as one sandwich.

#### Scenario: Compound wall exposes its layers

- GIVEN a wall type composed of three material layers
- WHEN extraction runs with material-layer takeoff enabled
- THEN the DTO carries one quantity entry per material
- AND each entry identifies its material
