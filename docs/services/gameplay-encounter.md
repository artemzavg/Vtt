# Gameplay & Encounter Service

## Bounded context

Владеет текущим игровым состоянием актёров и разрешением игровых действий:
current/max HP overlay, temporary HP, current class/spell/item resources, ammo/
charges/consumables, active statuses/effects, rests, death/concentration states,
encounters, combatants, initiative, rounds/turns, action execution and compensation.

Character владеет published static profile/loadout; Gameplay materializes exact
profile version into runtime. Scene владеет positions/geometry; Gameplay держит
только нужную spatial projection/version для range/cover/area validation.

## Aggregates and entities

### `ActorRuntime` aggregate

- actor id + source `CharacterId/ProfileVersion` or NPC instance/snapshot;
- campaign, current/max HP, temporary HP, death state;
- resource counters keyed by ruleset resource definition;
- ammo/charge/consumable counters keyed by character inventory item;
- active effects/status instances;
- concentration/linked effects;
- current runtime version, last profile rebase plan/hash;
- optional active encounter lease/reference.

### `Encounter` aggregate

- campaign/session/scene, state `Draft|Active|Paused|Completed|Aborted`;
- combatants referencing ActorRuntime and token;
- initiative scores/tie order/visibility;
- round, turn index, phase, delayed/ready/reaction windows;
- automation policy snapshot and exact ruleset engine version;
- resolved action log refs, pending prompts/timeouts.

### Entities/value objects

- `Combatant`, `InitiativeEntry`, `TurnCursor`;
- `ActionIntent`, `TargetSet`, `ActionResolution`;
- `RollRequirement/Reference`, `DamageComponent`, `SaveOutcome`;
- `ResourceCost`, `RuntimeEffect`, `Duration`, `StackingKey`;
- `ProfileRebasePlan`, `ManualAdjustment`, `CompensationReason`.

## Invariants

- ActorRuntime created/rebased only from authorized exact published profile;
- current resources clamped/handled only by ruleset-defined semantics;
- one idempotent action intent can resolve once; roll request id stable;
- action prerequisites, turn/reaction policy, target/access, range and resource
  availability validated at expected runtime/encounter/scene projection versions;
- cost spent and outcome applied in one owning aggregate transaction where one
  ActorRuntime; multi-target action uses Encounter orchestration with per-target
  idempotent results, not distributed transaction;
- damage order/stacking/resistance follows pinned ruleset artifact;
- immutable roll result cannot be changed; GM correction appends compensation;
- status source/duration/stacking explicit; expiration creates event;
- no hidden server reroll on retry;
- Character profile rebase during active action/turn serialized or queued;
- end encounter does not delete ActorRuntime; it remains canonical current state.

## Action resolution pipeline

1. Normalize intent and authorize actor/targets/action.
2. Load ActorRuntime + Encounter and local projections of profile/rules/scene.
3. Check source versions; stale sensitive projection returns `409 stale_context`
   with required refresh instead of guessing.
4. Produce roll request(s) with exact formula/visibility/modifiers.
5. Chat & Dice returns immutable results keyed by request id.
6. Deterministic rules engine resolves hit/save/outcome/effect/cost.
7. Append action + runtime events with expected versions. Multi-actor workflow
   records a process state and applies targets idempotently.
8. Return authoritative result; publish minimal `ActionResolved` and runtime deltas.
9. Chat renders explanation/action card from committed result.

For multi-target AoE, UI can show partial/pending target states until all are
applied. Failure retries missing targets; already applied targets dedupe by
`actionResolutionId + targetActorId`.

## Automation levels

- `manual`: calculate/roll and propose changes; GM/player explicitly applies.
- `assist`: common costs/result suggested with one confirmation.
- `automatic`: safe defined outcomes apply immediately; reaction/choice prompts
  still stop where rules require decision.

Policy inheritance: platform safe limit → campaign → encounter → action override.
Lower layer cannot enable unsafe capability disabled above. Every override audited.

## Domain events

### Actor runtime

- `ActorRuntimeCreated`, `ActorProfileRebased`;
- `HitPointsSet`, `DamageApplied`, `HealingApplied`, `TemporaryHitPointsChanged`;
- `ResourceSpent/Recovered/MaximumChanged`;
- `AmmunitionSpent/Restored`, `ItemChargeSpent/Recovered`;
- `StatusApplied/Refreshed/Stacked/Removed/Expired`;
- `ConcentrationStarted/CheckRequired/Ended`;
- `DeathStateChanged`, `RestStarted/Completed`;
- `ManualAdjustmentApplied`, `GameplayChangeCompensated`.

### Encounter/action

- `EncounterCreated/Started/Paused/Resumed/Completed/Aborted`;
- `CombatantAdded/Removed`, `InitiativeRolled/Set`, `InitiativeOrderResolved`;
- `RoundStarted`, `TurnStarted/Ended`, `CombatantDelayed/Readied`;
- `ActionDeclared`, `TargetsSelected`, `ActionRollRequested`;
- `AttackResolved`, `SavingThrowResolved`, `ActionResolved/Failed/Cancelled`;
- `ReactionWindowOpened/Resolved/Expired`.

## Integration events

- `ActorRuntimeChanged.v1 { actorId, campaignId, runtimeVersion, publicDelta,
  sourceProfileVersion, activeEncounterId? }`;
- `EncounterStarted.v1 { encounterId, campaignId, sceneId, combatants,
  rulesetRef, automationLevel }`;
- `EncounterTurnChanged.v1 { encounterId, round, activeCombatantId, turnVersion }`;
- `ActionResolved.v1 { resolutionId, encounterId?, actorId, targetSummaries,
  rollRefs, outcomeSummary, resultingVersions }`;
- `StatusLifecycleChanged.v1 { actorId, statusInstanceId, definitionRef, state,
  publicSummary }`;
- `EncounterEnded.v1 { encounterId, summaryRef }`.

Private GM-only target/data stripped from public integration summary; Session asks
for recipient DTO when needed.

## Связи

- Character profile publish creates/rebases runtime; Character does not consume
  current HP back into build.
- Ruleset compiled artifact drives deterministic action/effect/rest semantics.
- Compendium provides exact mechanical snapshots for action/item/status refs.
- Scene events maintain token/spatial projection; final movement remains Scene.
- Chat & Dice supplies immutable rolls and stores action cards/log.
- Campaign supplies members/automation policy; Session routes commands/results.

Avoid synchronous Character/Compendium calls in action hot path: consume immutable
artifacts/projections ahead of session. A missing artifact prevents action with
clear preload/retry, not a hidden fallback rule.

## Public API

### Actor runtime

| Method/path | Body/query | Назначение |
|---|---|---|
| `GET /api/v1/actors/{actorId}/runtime` | optional include effects/resources | authorized current state/version/profile ref |
| `POST /api/v1/actors/{actorId}/adjustments` | typed `damage|healing|set-hp|resource|ammo`, value, reason, visibility; expected version | manual audited change |
| `POST /api/v1/actors/{actorId}/rests` | rest type, optional choices; expected version | resolve configured recovery operation |
| `POST /api/v1/actors/{actorId}/statuses` | exact definition ref, source actor/action, duration, parameters | apply/validate status |
| `DELETE /api/v1/actors/{actorId}/statuses/{statusInstanceId}` | reason; expected version | remove/compensate |
| `GET /api/v1/actors/{actorId}/actions` | encounterId/context, filters | executable actions with availability/cost/explanations |
| `GET /api/v1/actors/{actorId}/provenance/{fieldOrActionPath}` | context | combined profile + runtime modifier explanation |

### Encounters

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/campaigns/{campaignId}/encounters` | sceneId, name, automationLevel, combatant refs | create draft |
| `GET /api/v1/campaigns/{campaignId}/encounters` | state/session/cursor | summaries/history |
| `GET /api/v1/encounters/{encounterId}` | include combatants/turn/effects/log cursor | authorized current view |
| `POST /api/v1/encounters/{id}/combatants` | actorId, tokenId, visibility/group, optional initiative | add/link |
| `DELETE /api/v1/encounters/{id}/combatants/{combatantId}` | disposition; `If-Match` | remove safely |
| `POST /api/v1/encounters/{id}:start` | initiative policy; `If-Match` | validate profiles/scene and roll/request initiative |
| `POST /api/v1/encounters/{id}:pause` | reason; `If-Match` | pause turn automation |
| `POST /api/v1/encounters/{id}:complete` | confirmation; `If-Match` | close encounter, retain runtime |
| `POST /api/v1/encounters/{id}/turns:next` | expected active combatant/turn version | run expirations and advance |
| `POST /api/v1/encounters/{id}/initiative:roll` | combatant ids, visibility | server rolls as idempotent batch |
| `PUT /api/v1/encounters/{id}/initiative/{combatantId}` | score/tie order/reason; `If-Match` | GM override |

### Actions, saves and compensation

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/encounters/{id}/actions` | actorId, actionRef, target refs/point/template, parameters, expected actor/encounter/scene versions | execute or create pending prompts |
| `GET /api/v1/action-resolutions/{resolutionId}` | none | state, prompts, rolls, outcomes, resulting versions |
| `POST /api/v1/action-resolutions/{id}/choices` | promptId, selection, expected resolution version | answer damage type/upcast/reaction/etc. |
| `POST /api/v1/action-resolutions/{id}/saving-throws` | target-specific decision/auto consent | resolve requested saves when not automatic |
| `POST /api/v1/action-resolutions/{id}:cancel` | reason; expected version | cancel only before committed irreversible steps |
| `POST /api/v1/action-resolutions/{id}:compensate` | affected outcomes, reason, confirmation; GM capability | append inverse domain changes, preserve audit |
| `POST /api/v1/actions:preview` | same intent without roll/commit | range/cost/targets/formula warnings; short-lived context hash |

`actions:preview` не гарантирует commit: final expected versions and resources are
checked again. Client cannot submit its own roll total as authoritative.

## Storage/read models

- streams ActorRuntime/Encounter/ActionResolution process;
- local immutable `CharacterCombatProfile`, `RulesArtifact`, `SceneCombatProjection`;
- `ActorRuntimeView`, `EncounterTracker`, `ExecutableActionView`,
  `ActionResolutionView`, `EffectTimeline`, `GameplayAudit`;
- snapshots important for long-lived actor runtime; every event remains replayable.

## Key tests/SLI

- attack/save/crit/half damage/resistance/temp HP/order golden scenarios;
- resources/ammo exactly once under retry/concurrency;
- multi-target partial failure/retry and compensation;
- status stacking/duration/turn boundaries/concentration/reactions;
- profile rebase preserves current values according to explicit plan;
- stale scene/profile/actor version and authorization/hidden target;
- deterministic replay produces identical runtime/action hash;
- SLI: typical action p95 ≤300 ms excluding human prompt, dice p95 ≤150 ms,
  peer result ≤150 ms after commit, correctness duplicate/lost target zero in tests.
