# Ruleset Service

## Bounded context

Владеет формальным описанием игровой системы: schemas, field/resource types,
choice/prerequisite logic, modifier algebra, action/effect semantics, expression
AST, dependency graph, validation, compilation, immutable published versions и
migration definitions. Не владеет конкретным персонажем, HP в игре или текстовым
каталогом существ; content references принадлежат Compendium.

Ключевой язык контекста: `Ruleset`, `Draft`, `Definition`, `ChoiceSet`, `Modifier`,
`Expression`, `Dependency`, `CompiledPackage`, `MigrationPlan`.

## Aggregates and entities

### `Ruleset` aggregate

- identity, slug, owner/maintainers, visibility;
- supported locales and content type contracts;
- published version refs, current recommended/deprecated state;
- package dependencies and compatibility policy.

### `RulesetDraft` aggregate

- base version, manifest draft, definition revisions;
- validation state/errors/warnings;
- collaborators and publish approvals;
- compiled artifact hash and golden test result.

Один draft может содержать много definition documents, но aggregate stream хранит
logical edits/versions; большие documents лежат в versioned document store owned
by service и адресуются content hash.

### Entities

- `FieldDefinition`: typed field, default, visibility, display hints;
- `ResourceDefinition`: current/max/recovery/consumption policy;
- `ChoiceSetDefinition`: options, cardinality, prerequisites, grants;
- `FeatureDefinition`: modifiers, actions, resources, child choices;
- `ActionDefinition`: activation, target/range, roll/check/save, outcome/effects;
- `ModifierDefinition`: operation, selector, priority, stacking group, condition;
- `ExpressionDefinition`: parsed bounded AST + static type/cost;
- `SheetSchema`: sections/layout hints, not executable UI;
- `MigrationDefinition`: source/target, mapping, manual decision points;
- `GoldenScenario`: inputs + expected deterministic outputs.

### `PublishedRulesetVersion` immutable document

Manifest, normalized definitions, dependency graph, compiled server/browser
artifacts, schema hashes, license metadata, test report and signature.

## Invariants

- published version immutable and content-addressed;
- dependency graph acyclic for computed fields; explicit fixed-point semantics
  запрещены в first version;
- every expression statically typed and within evaluation cost budget;
- selector/target only references declared schema paths/capabilities;
- choice cardinality/prerequisites solvable in golden scenarios;
- package dependencies pinned to exact compatible versions/hash;
- breaking definition change requires new ruleset semantic version;
- server and browser compiled artifacts pass identical golden corpus;
- no arbitrary JS/C#/network/filesystem/time/random access;
- dice expressions allowed only in explicit roll nodes, not silent derived values.

## Evaluation model

Input:

- pinned ruleset version hash;
- base facts and user choices;
- content mechanics snapshots/refs;
- context (`build`, `sheet`, `action`, `rest`, `migration`);
- explicit deterministic roll values for resolution when needed.

Output:

- computed fields/resources/actions;
- active/suppressed modifiers with precedence explanation;
- validation errors/warnings and unresolved choices;
- dependency/provenance graph;
- stable result hash and engine version.

Modifier order is data-defined but bounded: `base → set/min/max → additive →
multiplicative → caps → situational`. Exact algebra belongs to ruleset primitive,
not global D&D assumption.

## Domain events

- `RulesetCreated`, `RulesetMaintainerAdded/Removed`;
- `RulesetDraftCreated`, `DefinitionAdded/Replaced/Removed`;
- `PackageDependencyPinned`, `MigrationDefinitionAdded`;
- `RulesetDraftValidated`, `CompilationSucceeded/Failed`;
- `GoldenScenariosPassed/Failed`;
- `RulesetVersionPublished`, `RulesetVersionDeprecated/Restored`;
- `RulesetVisibilityChanged`.

## Integration events

- `RulesetVersionPublished.v1 { rulesetId, versionId, semanticVersion,
  artifactHash, schemaHash, compatibility, locales, licenseSummary }`;
- `RulesetVersionDeprecated.v1 { versionId, reason, replacementVersionId? }`;
- `RulesetAccessChanged.v1 { rulesetId, visibility, revision }`;
- `RulesetVersionRetracted.v1` only for security/legal quarantine; existing
  campaigns get explicit degraded/legal state, history not silently rewritten.

## Связи

- Campaign pins exact published version and requests migration preview.
- Compendium supplies mechanics content contracts/snapshots and validates refs;
  Ruleset publishes expected content schemas.
- Character and Gameplay cache compiled packages by immutable artifact hash.
- Search indexes public metadata; Media may host icons, not compiled executable.
- Identity/Campaign provide author/collaborator permission projections.

Compiled artifact distribution is pull-by-hash from authenticated object/CDN
location plus event invalidation; no sync evaluation call on every sheet update.

## Public API

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/rulesets` | `name`, `slug`, `visibility`, `license`, locales | создать ruleset shell |
| `GET /api/v1/rulesets` | `q`, `visibility`, `systemTag`, `locale`, cursor | доступные systems |
| `GET /api/v1/rulesets/{rulesetId}` | none | metadata, versions, permissions |
| `PATCH /api/v1/rulesets/{rulesetId}` | metadata/visibility; `If-Match` | изменить shell, не published versions |
| `POST /api/v1/rulesets/{id}/drafts` | optional `baseVersionId` | создать editable draft |
| `GET /api/v1/ruleset-drafts/{draftId}` | include summary/errors | draft status/version |
| `PUT /api/v1/ruleset-drafts/{draftId}/definitions/{definitionId}` | kind, schemaVersion, document; `If-Match` | add/replace typed definition |
| `DELETE /api/v1/ruleset-drafts/{draftId}/definitions/{definitionId}` | `If-Match` | remove if dependency rules allow |
| `GET /api/v1/ruleset-drafts/{draftId}/definitions` | kind/cursor | list documents |
| `POST /api/v1/ruleset-drafts/{draftId}:validate` | validation profile | async full schema/graph/license validation |
| `POST /api/v1/ruleset-drafts/{draftId}:compile` | target engines (`server`,`browser`) | async compile + golden tests |
| `POST /api/v1/ruleset-drafts/{draftId}:publish` | semanticVersion, changelog, artifact hash confirmation | immutable publish, requires valid compile |
| `GET /api/v1/ruleset-versions/{versionId}` | none | manifest/schema hash/compatibility/license |
| `GET /api/v1/ruleset-versions/{versionId}/artifact` | target | short-lived signed immutable artifact URL |
| `POST /api/v1/ruleset-versions/{versionId}:evaluate` | bounded facts/choices/context | author/debug preview, rate/cost limited; not hot app path |
| `POST /api/v1/ruleset-versions/{source}/migrations:preview` | targetVersionId, normalized source facts | compatibility result/manual choices |
| `POST /api/v1/ruleset-versions/{versionId}:deprecate` | reason, replacement | maintainer action; `If-Match` |

### Definition document limits

- request max 1 MiB initially; larger package imports async;
- expression length/depth/node cost bounded;
- max definitions/dependencies/choice options per package set by quota;
- error path uses JSON Pointer + stable code (`expression_cycle`,
  `unknown_selector`, `unsatisfied_choice_cardinality`).

## Internal API

| Method/path | Параметры | Назначение |
|---|---|---|
| `GET /internal/v1/ruleset-versions/{id}/compiled/{target}` | service identity, expected hash | artifact/cache fill |
| `POST /internal/v1/ruleset-versions/{id}/evaluate` | typed normalized request, deadline | controlled cache-miss/fallback evaluation |
| `POST /internal/v1/ruleset-versions/{id}/content-contracts:validate` | entry schemas/hashes | Compendium publish validation |

## Storage/read models

- streams Ruleset/RulesetDraft; immutable definition/artifact documents by hash;
- projections `RulesetCatalog`, `VersionManifest`, `DraftValidation`,
  `DependencyReverseIndex`, `MigrationCompatibility`;
- compiled cache local memory + disk/object immutable; eviction safe.

## Key tests and SLI

- parser/property/fuzz, cycle/complexity, deterministic serialization/hash;
- golden corpus server/browser, modifier conflict/stacking, invalid references;
- malicious package cannot call IO/network/eval or exhaust time/memory;
- publish race, immutable artifact, migration compatibility;
- SLI: compile p95 by package size, evaluation p95 <10 ms cached typical profile,
  artifact availability 99.9%, zero divergent golden outputs.
