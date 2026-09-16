# partida-codification Specification

## Purpose

Defines how a model element is assigned a *partida* (budget line item) code, and how elements that cannot be coded are still reported rather than lost. Codification is an ordered resolver chain whose shape exists from the first increment even though only its first link is implemented then.

Domain vocabulary: *partida* = budget line item; *capitulo* = budget chapter; *linea de medicion* = measurement line (one measured instance).

Every requirement is tagged with the increment that delivers it (I1–I4).

## Requirements

### Requirement: Ordered Codification Resolver Chain

**Increment**: I1 (chain and terminal link), extended in I2 and I4

Codification SHALL be an ordered chain of resolvers evaluated in this fixed order: Assembly Code, Keynote, shared parameter, rule, unclassified. The first resolver returning a non-empty code SHALL win, and later resolvers MUST NOT be consulted. `unclassified` is a terminal link that always succeeds, so codification MUST NOT throw for an uncodeable element.

#### Scenario: Earlier link wins over later links

- GIVEN an element carrying both a non-empty Assembly Code and a non-empty Keynote
- WHEN codification runs
- THEN the resolved code comes from Assembly Code
- AND the Keynote value is not used as the code

#### Scenario: Chain terminates on an uncodeable element

- GIVEN an element for which every configured resolver returns no value
- WHEN codification runs
- THEN the element is resolved as unclassified
- AND no exception is raised

### Requirement: Assembly Code Resolver

**Increment**: I1

The first resolver SHALL read the element type's codification parameter via `BuiltInParameter.ASSEMBLY_CODE`. A value that is absent, empty, or whitespace-only MUST be treated as unresolved and MUST pass control to the next link. A resolved code MUST be trimmed before use.

#### Scenario: Wall type with an assembly code

- GIVEN a wall whose type has Assembly Code `C1010`
- WHEN codification runs
- THEN the element resolves to partida code `C1010`

#### Scenario: Wall with no assembly code

- GIVEN a wall whose type has an empty Assembly Code
- WHEN codification runs with only the Assembly Code resolver enabled
- THEN the element resolves as unclassified
- AND it is still measured and exported

#### Scenario: Whitespace-only code is not a code

- GIVEN a wall whose type has Assembly Code `"   "`
- WHEN codification runs
- THEN the element resolves as unclassified, not to a blank-named partida

### Requirement: Unclassified Is a First-Class Result

**Increment**: I1

An unclassified element SHALL be a normal, reported outcome, not an error and not a dropped row. Unclassified elements MUST still be measured and MUST still reach the export with their `UniqueId`, category, family and type, so the user can find and fix them in the model.

#### Scenario: Unclassified elements survive to the output

- GIVEN a model in which 12 of 40 walls carry no resolvable code
- WHEN the export runs
- THEN all 40 walls appear in the output
- AND the 12 uncoded walls are attributed to the unclassified group

### Requirement: Structural Mapping from Model to Budget

**Increment**: I1

The extraction SHALL map the model onto the budget structure as: category to *capitulo*, element type to *partida*, element instance to *linea de medicion*. Distinct types that resolve to the same partida code MUST collapse into a single partida whose measurement lines include the instances of every contributing type.

#### Scenario: Two wall types share one partida code

- GIVEN wall types `WT-A` and `WT-B`, both with Assembly Code `C1010`
- AND three instances of `WT-A` and two of `WT-B`
- WHEN the export runs
- THEN exactly one partida `C1010` is produced
- AND it contains five measurement lines, each with its own `UniqueId`
- AND the partida total equals the sum of those five lines

### Requirement: Keynote Resolver

**Increment**: I2

A second resolver SHALL read the element's Keynote parameter by built-in identifier, applying the same empty and whitespace rules as the Assembly Code resolver.

#### Scenario: Keynote used as fallback

- GIVEN an element with an empty Assembly Code and Keynote `M-030`
- WHEN codification runs with both resolvers enabled
- THEN the element resolves to partida code `M-030`

### Requirement: Shared Parameter Resolver

**Increment**: I2

A third resolver SHALL read a user-nominated shared parameter, type-level or instance-level. If the shared parameter is not bound in the model, the resolver MUST return unresolved and MUST NOT raise an error, because incoming models are frequently authored by another discipline.

#### Scenario: Shared parameter not bound in the model

- GIVEN a configuration naming a shared parameter that the model does not define
- WHEN codification runs
- THEN the resolver returns unresolved for every element
- AND the chain continues to the next link without error

### Requirement: Rule Resolver Boundary Contract

**Increment**: I4 — CONTRACT ONLY, deliberately under-specified

The chain SHALL reserve a fourth link for a user-defined rule resolver, positioned after the shared parameter resolver and before `unclassified`. Its contract is: it receives one element's extracted data and returns either a resolved code or "no match". It MUST be free of Revit dependencies and MUST be side-effect free, so it stays unit-testable.

This specification deliberately does NOT define rule-authoring syntax, rule storage, rule ordering semantics, filter operators, or any user interface for rules. Those are gated until increments I2 and I3 produce concrete rules from real models. Specifying them now would be the premature abstraction this change exists to avoid, and any such detail MUST be added by a later change, not assumed here.

#### Scenario: Chain works with no rule resolver installed

- GIVEN the rule link is not implemented
- WHEN codification runs
- THEN the chain behaves as if the link returned "no match"
- AND the unclassified terminal still resolves the element

#### Scenario: Rule resolver plugs in without changing earlier links

- GIVEN a rule resolver satisfying the contract above
- WHEN it is installed into the chain
- THEN elements already resolved by an earlier link resolve identically to before
- AND only previously unclassified elements can change outcome
