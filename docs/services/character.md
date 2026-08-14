# Character Service

## Bounded context

Владеет персонажем как результатом build/progression choices: identity, ruleset
pin, draft wizard, ability assignment, origin/class/level choices, proficiencies,
spells known/prepared, inventory ownership/loadout, equipment/attunement choices,
derived static profile, provenance и object ACL.

Не владеет current HP, spent slots/resources, ammunition counter, active conditions
или initiative — это `Gameplay & Encounter`. Не владеет definitions/text — это
Ruleset/Compendium. Composite sheet собирает static profile + gameplay runtime.

## Aggregates and entities

### `Character` aggregate

- id, owner/campaign ref, name/portrait/token defaults;
- status `Draft|Playable|Retired|Archived|MigrationRequired`;
- pinned ruleset version + engine version/content set revision;
- build choices organized by progression node/level;
- ability score method/assignments and audited roll refs;
- origin/species/background/class/subclass/multiclass/feat/proficiency choices;
- spellbook/known/prepared choices;
- owned inventory and loadout/equip/attunement choices;
- custom fields/features with structured/manual mode;
- last published `CharacterProfileVersion`/hash;
- ACL revision and GM overrides with reason.

Character may be created outside campaign; `campaignId=null`, owner-only ACL. Joining
campaign links it through explicit command after compatibility/access check.

### `CharacterBuildDraft` entity/state

- wizard graph, completed/invalidated/pending nodes;
- unresolved choices, warnings/errors;
- preview profile/provenance hash;
- autosave version and expiry policy.

### `CharacterProfile` immutable versioned document

- normalized base facts, derived fields/saves/skills/defenses/senses/movement;
- resources definitions/max/recovery (not current values);
- actions with formulas/target/save/cost metadata;
- spell access/preparation and loadout;
- modifier/provenance graph and warnings;
- exact ruleset/content refs, engine version, profile hash.

### Value objects

`AbilityGenerationMethod`, `AbilityAssignment`, `ChoicePath`, `LevelGrant`,
`EntrySnapshotRef`, `InventoryItem`, `EquipmentSlot`, `Attunement`, `OverrideReason`,
`ProfileHash`, `CharacterAccessRelation`.

## Invariants

- every required choice resolved before `Playable`;
- fixed array/point-buy values used once and budget exact per ruleset;
- random ability result references immutable server roll unless GM explicitly
  approves manual/homebrew method;
- class/level choices and prerequisites valid for pinned version;
- no spell/proficiency/item choice outside allowed sets without audited override;
- equip/attunement/stacking limits and dependencies enforced by ruleset;
- published profile deterministic for exact inputs/artifact/content refs;
- current gameplay counters never silently copied into build aggregate;
- ACL changes cannot remove the last owner;
- migration never mutates old profile; it creates draft and new published version.

## Main command flows

### New character/level N

1. `CreateCharacter` pins exact ruleset/content set.
2. UI gets ordered build nodes from ruleset + current draft.
3. Each `SetBuildChoice` validates locally cached compiled package, appends event,
   invalidates only dependent nodes and returns preview/diff.
4. Ability random rolls requested from Chat & Dice and linked by roll id.
5. `CompleteBuild` validates full graph/golden engine, publishes immutable profile.
6. Gameplay consumes profile and creates/rebases runtime with explicit preserve/
   recalculate policy for current values.

### Level up/respec/migration

- create draft based on published profile;
- add progression nodes in chronological order, even when target level is N;
- preview new max HP/resources/actions/spells and invalid choices;
- publish only on confirmation; old profile remains usable during draft;
- runtime rebase policy lists preserved current ratio/value, capped resources and
  removed action cleanup; user/GM confirms destructive consequences.

## Domain events

- `CharacterCreated`, `CharacterLinkedToCampaign/Unlinked`;
- `BuildDraftStarted/Abandoned`, `BuildChoiceSet/Replaced/Cleared`;
- `AbilityMethodSelected`, `AbilityRollLinked`, `AbilityScoresAssigned`;
- `OriginSelected`, `ClassLevelAdded`, `SubclassSelected`, `FeatSelected`;
- `ProficiencySelected`, `SpellLearned/Prepared/Unprepared`;
- `InventoryItemGranted/Added/Removed`, `ItemEquipped/Unequipped`;
- `ItemAttuned/Unattuned`, `CustomFeatureAdded`, `CharacterOverrideApplied`;
- `BuildValidationFailed`, `CharacterProfilePublished`;
- `RulesetMigrationDraftCreated/Completed`;
- `CharacterAccessGranted/Revoked`, `CharacterRetired/Archived`.

## Integration events

- `CharacterCreated.v1 { characterId, ownerId, campaignId?, rulesetRef }`;
- `CharacterProfilePublished.v1 { characterId, profileVersion, profileHash,
  rulesetRef, campaignId?, summary, artifactRef }`;
- `CharacterCampaignLinkChanged.v1 { characterId, campaignId?, accessRevision }`;
- `CharacterAccessChanged.v1 { characterId, relationsSummary, accessRevision }`;
- `CharacterPortraitChanged.v1 { characterId, assetId, revision }`;
- `CharacterRetired.v1 { characterId, campaignId? }`.

Integration profile event не несёт полную biography/private notes. Gameplay pulls
authorized mechanical artifact by exact version/hash.

## Связи

- Campaign validates membership/ruleset/content and consumes ACL summary.
- Ruleset artifacts execute choice/derived evaluation.
- Compendium exact refs supply features/items/spells/content mechanics.
- Chat & Dice owns random rolls used in ability/HP generation.
- Media supplies ready portrait/token assets.
- Gameplay consumes profiles and owns current state; publishes runtime changes for
  composite sheet, но не изменяет character build.
- Scene references character/profile for token defaults and access.
- Search indexes only allowed summary/public characters.

## Public API

### Character lifecycle/read

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/characters` | `name`, `rulesetVersionId`, optional `campaignId`, `targetLevel`, `templateRef` | create draft |
| `GET /api/v1/characters` | campaignId/status/ruleset/cursor | characters visible to principal |
| `GET /api/v1/characters/{characterId}` | none | metadata, status, ACL, latest profile ref/version |
| `PATCH /api/v1/characters/{id}` | name/biography/portrait defaults; `If-Match` | editable non-build metadata |
| `GET /api/v1/characters/{id}/profile` | optional `version`, `include=provenance` | immutable static profile/read model |
| `GET /api/v1/characters/{id}/audit` | category/cursor; permission | human-readable history/provenance changes |
| `POST /api/v1/characters/{id}:retire` | reason; `If-Match` | retire without deleting history |
| `POST /api/v1/characters/{id}:archive` | `If-Match` | hide/archive |

### Builder

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/characters/{id}/build-drafts` | mode `initial|level-up|respec|migration`, target level/version | start from chosen profile |
| `GET /api/v1/character-builds/{buildId}` | `include=steps,preview,validation` | wizard state and version |
| `GET /api/v1/character-builds/{buildId}/steps` | optional after step | ruleset-driven ordered nodes/options refs |
| `PUT /api/v1/character-builds/{buildId}/choices/{choicePath}` | selected option/value/ref; `If-Match` | set/replace one typed choice, returns invalidations/diff |
| `DELETE /api/v1/character-builds/{buildId}/choices/{choicePath}` | `If-Match` | clear and invalidate dependents |
| `POST /api/v1/character-builds/{buildId}/ability-rolls` | method/formula visibility; Idempotency-Key | request audited server roll and link result |
| `PUT /api/v1/character-builds/{buildId}/ability-assignment` | method, values-to-fields, point budget decisions; `If-Match` | validate ability scores |
| `POST /api/v1/character-builds/{buildId}:validate` | full/step scope | authoritative validation/preview |
| `POST /api/v1/character-builds/{buildId}:complete` | preview hash, accepted warning codes; `If-Match` | publish profile atomically |
| `DELETE /api/v1/character-builds/{buildId}` | none | abandon draft, not published profile |

`choicePath` is opaque/URL-safe id issued by ruleset graph, not arbitrary JSON path.

### Inventory/spells/loadout outside build

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/characters/{id}/inventory/items` | exact entry ref or custom item, quantity/acquisition; `If-Match` | add owned item; price rules may require campaign policy |
| `PATCH /api/v1/characters/{id}/inventory/items/{itemId}` | ownership quantity/notes/container; `If-Match` | change static inventory ownership |
| `DELETE /api/v1/characters/{id}/inventory/items/{itemId}` | `If-Match`, disposition | remove; runtime reconciliation required |
| `POST /api/v1/characters/{id}/loadout/equip` | itemId, slot, optional replace; `If-Match` | equip and republish profile |
| `POST /api/v1/characters/{id}/loadout/attune` | itemId; `If-Match` | attune if allowed |
| `PUT /api/v1/characters/{id}/spells/{entryVersionId}` | state known/prepared, source choice; `If-Match` | adjust allowed spell selection |
| `DELETE /api/v1/characters/{id}/spells/{entryVersionId}` | `If-Match` | remove/unprepare per rules |
| `GET /api/v1/characters/{id}/recommendations/spells` | role/preferences/locale | explainable candidates; optional/degradable |

Static inventory `quantity owned` and gameplay `remaining consumable/ammo` are
distinguished in response. Consumable spend commands go to Gameplay.

### ACL/campaign

| Method/path | Body/query | Назначение |
|---|---|---|
| `PUT /api/v1/characters/{id}/access/{subjectId}` | relations owner/editor/controller/viewer; `If-Match` | object ACL |
| `DELETE /api/v1/characters/{id}/access/{subjectId}` | `If-Match` | revoke relations |
| `POST /api/v1/characters/{id}:link-campaign` | campaignId, requested ACL; `If-Match` | compatibility + permission link |
| `POST /api/v1/characters/{id}:unlink-campaign` | disposition/export; `If-Match` | unlink if no active session constraints |

## Read models

- `CharacterSummaryByOwner/Campaign`, `BuildWizardView`, `CharacterProfileDocument`,
  `CharacterSheetStaticView`, `InventoryView`, `SpellSelectionView`,
  `CharacterAccessView`, `ProfileMigrationDiff`;
- large provenance graph stored compressed/content-addressed; query can request
  one field/action path instead of full graph.

## Key tests and SLI

- every ability method, budget/max/duplicates and audited RNG linkage;
- level 1/N, multiclass/preconditions, HP policy/Constitution changes;
- feature/item stacking, attunement, spell grants/prepared limits;
- dependency invalidation and preview hash stale conflict;
- exact ruleset/content refs and browser/server golden parity;
- ACL cross-campaign, owner outside campaign, GM override reason;
- duplicate completion/profile event and Gameplay rebase idempotency;
- SLI: cached choice preview p95 <100 ms server compute, command p95 <300 ms,
  profile publish p95 <1 s or async for huge homebrew, zero profile hash divergence.
