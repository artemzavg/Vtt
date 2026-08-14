# Scene Service

## Bounded context

Владеет долговечным представлением tabletop space: scenes, grid, layers, spatial
objects, walls/doors, lights, persistent tokens/positions, notes/templates,
activation state and per-user/group fog exploration checkpoints. Не владеет
presence/drag intermediates (Session), actor HP/status (Gameplay), character
profile (Character) или binary map (Media).

## Aggregates and entities

### `Scene` aggregate

- campaign, name/folder/order/status/active visibility;
- dimensions/background `AssetId`/tile manifest ref;
- grid type (`square|hex-row|hex-column|gridless`), origin, cell size, distance,
  units and snapping policy;
- layer definitions/order/visibility/lock/audience;
- darkness/global illumination/fog policy;
- object index/revision; large object payload may be partitioned into substreams;
- edit history batch and current scene version.

### Spatial entities

- `Token`: id, source `CharacterRef|NpcSnapshotRef|Unlinked`, transform, size,
  elevation, appearance, bars definitions, nameplate, vision/detection config,
  controller/viewer ACL, object version;
- `Wall`: endpoints, type, movement/sight/light/sound restrictions, door state;
- `Light`: position/radii/angle/color/intensity/animation, wall constraint;
- `Tile`, `Drawing`, `Text`, `MeasuredTemplate`, `Note`, `SoundEmitter`;
- `Layer`: kind, z-order, audience, locked.

### `FogExploration` aggregate

Scope `(sceneId, audienceId)` where audience may be user/party token group:
chunked explored mask/vector deltas, checkpoint version, reset generation.
Manual GM reveal/hide is auditable and distinct from automatic exploration.

### `SceneActivation` / campaign projection

One active player-facing scene per configured audience by default; multi-scene
split party is supported through audience activations rather than global boolean.

## Invariants

- object belongs to one scene/layer and coordinates within bounded world extent;
- object command checks object version; unrelated objects edit concurrently;
- player cannot mutate GM/locked layer or token without controller relation;
- token source profile ref exact; source deletion leaves explicit unlinked snapshot;
- wall geometry valid/no NaN/infinite; complexity capped per scene/chunk;
- door state change follows type/capability and collision policy;
- fog reveal/hide bound to current reset generation and authorized audience;
- asset must be Ready/audience-compatible;
- activation publishes only ready metadata; hidden GM objects never enter player
  payload/projection, а не просто скрываются CSS.

## Spatial/visibility model

- World coordinates use deterministic fixed precision (e.g. integer microunits),
  not uncontrolled float accumulation in events.
- Server validates final positions, bounds, optional collision/movement rules.
- Client computes rendering/visibility in Web Worker from authorized wall/light
  subset. Server computes/validates authoritative reveal checkpoint and sensitive
  token visibility where cheating risk matters.
- Scene payload chunked by spatial region/layer; bootstrap sends viewport/relevant
  chunks, not full 100 MB document.
- Visibility cache key includes scene/wall/light/fog/token vision revisions.

## Domain events

- `SceneCreated/Renamed/Archived/Restored`;
- `SceneBackgroundSet`, `GridConfigured`, `SceneDimensionsChanged`;
- `LayerAdded/Reordered/VisibilityChanged/Locked`;
- `SceneObjectAdded/Transformed/PropertiesChanged/Removed` with typed subtype;
- `WallAdded/Changed/Removed`, `DoorStateChanged`;
- `LightAdded/Changed/Removed`, `SceneDarknessChanged`;
- `TokenPlaced/Moved/Rotated/SourceChanged/AccessChanged/Removed`;
- `SceneActivatedForAudience/Deactivated`;
- `FogExplored`, `FogManuallyRevealed/Hidden`, `FogReset`, `FogCheckpointCreated`;
- `SceneEditBatchApplied`, `SceneEditCompensated`.

Events use typed compact payload; a batch has bounded objects and per-object before/
after required for safe compensation. Raw image/fog bitmap not embedded in event.

## Integration events

- `SceneCreated.v1 { sceneId, campaignId, summary }`;
- `SceneActivated.v1 { campaignId, sceneId, audience, sceneVersion }`;
- `SceneConfigurationChanged.v1 { sceneId, sceneVersion, changedKinds[] }`;
- `SceneTokenChanged.v1 { sceneId, tokenId, objectVersion, sourceRef, transform,
  accessRevision }`;
- `SceneVisibilityRevisionChanged.v1 { sceneId, wallRevision, lightRevision,
  fogGeneration }`;
- `FogCheckpointUpdated.v1 { sceneId, audienceId, generation, checkpointVersion,
  artifactRef }`;
- `SceneArchived.v1`.

High-frequency live deltas не являются integration events.

## Связи

- Campaign: lifecycle/membership/policy/ruleset and activation audiences.
- Media: map/token/tile assets and quarantine/delete.
- Character: token source/profile/access changes.
- Compendium: NPC exact snapshot refs.
- Gameplay: token↔actor runtime mapping, ranges/areas/cover projection.
- Session: transient drag, room sequencing, final commands and broadcasts.
- Search: scene summary/GM notes only per authorized index.

## Public API

### Scene lifecycle

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/campaigns/{campaignId}/scenes` | name, dimensions, grid, optional backgroundAssetId/template | create scene |
| `GET /api/v1/campaigns/{campaignId}/scenes` | folder/status/audience/cursor | authorized summaries/navigation |
| `GET /api/v1/scenes/{sceneId}` | `include=layers,settings`, optional viewport/chunk cursor | authorized persistent scene read |
| `PATCH /api/v1/scenes/{sceneId}` | name/folder/order/dimensions/settings; `If-Match` | scene metadata/settings |
| `POST /api/v1/scenes/{sceneId}:activate` | audience ids/group; `If-Match` | publish active scene |
| `POST /api/v1/scenes/{sceneId}:archive` | `If-Match` | archive after session constraints |
| `POST /api/v1/scenes/{sceneId}:clone` | target campaign, include fog? false default | async safe clone |

### Objects and batches

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/scenes/{sceneId}/objects` | typed object (`token`,`wall`,`light`,`tile`,`drawing`,`template`,`note`), layerId, properties | add one object |
| `GET /api/v1/scenes/{sceneId}/objects` | kinds/layers/viewport/sinceVersion/cursor | authorized chunk/query |
| `PATCH /api/v1/scenes/{sceneId}/objects/{objectId}` | typed patch; object `If-Match` | change transform/properties |
| `DELETE /api/v1/scenes/{sceneId}/objects/{objectId}` | object `If-Match` | remove/tombstone |
| `POST /api/v1/scenes/{sceneId}/object-batches` | max N add/patch/delete ops + expected object versions | atomic bounded edit batch/one undo unit |
| `POST /api/v1/scenes/{sceneId}/edits/{editId}:compensate` | reason, expected affected versions | safe undo as new event; may reject conflicts |
| `POST /api/v1/scenes/{sceneId}/tokens/{tokenId}:move` | final transform, path summary optional, expected object version | authoritative final move |
| `POST /api/v1/scenes/{sceneId}/doors/{wallId}:set-state` | open/closed/locked/secret reveal | permissioned state transition |

### Layers/vision/fog

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/scenes/{sceneId}/layers` | kind/name/z/audience/locked | add layer |
| `PATCH /api/v1/scenes/{sceneId}/layers/{layerId}` | order/audience/lock; `If-Match` | configure layer |
| `GET /api/v1/scenes/{sceneId}/visibility` | `viewerSubjectId` or controlled token id, viewport | GM debug/authorized visibility summary, not secret objects for player |
| `POST /api/v1/scenes/{sceneId}/fog:reveal` | audience, geometry/chunks, generation; Idempotency-Key | manual reveal |
| `POST /api/v1/scenes/{sceneId}/fog:hide` | audience, geometry/chunks, generation | manual hide |
| `POST /api/v1/scenes/{sceneId}/fog:reset` | audience/all, confirmation; `If-Match` | new reset generation |
| `GET /api/v1/scenes/{sceneId}/fog/checkpoint` | own audience, since version | signed chunk/checkpoint refs |

## Realtime client protocol subset

Session forwards client messages, but Scene defines semantics:

- `scene.tokenDragStarted` — lock hint only;
- `scene.tokenDragDelta` — ephemeral, contains commandId/clientSeq/position;
- `scene.tokenMoveCommitted` — authoritative token/object version/position;
- `scene.objectPatched` — authorized client DTO stripped of secret properties;
- `scene.visibilityInvalidated` — revisions; client worker recomputes;
- `scene.resyncRequired` — fetch delta/snapshot.

## Storage/read models

- Scene stream plus partitioned object substreams if measured threshold exceeded;
- spatial PostgreSQL/PostGIS projection for viewport/area/range queries; geometry
  operations use tested fixed conversion;
- fog chunk artifacts object storage + metadata stream/checkpoints;
- `SceneNavigation`, `SceneBootstrapByAudience`, `SpatialObjectIndex`,
  `TokenAccess`, `VisibilityRevision`, `SceneEditHistory`.

## Key tests/SLI

- coordinate/grid/hex property tests, object concurrency, batch compensation;
- all role/layer/token visibility combinations and secret leakage snapshots;
- walls/doors/vision/darkvision golden geometry, fog generation/reset races;
- media quarantine, unlinked character/NPC, large scene chunks;
- reconnect final move vs ephemeral delta; no event per mousemove;
- browser device performance: 300 visible tokens/2k walls target profile;
- SLI: object command p95 <200 ms, bootstrap metadata p95 <500 ms, visibility
  invalidation peer p95 <150 ms, projection/fog checkpoint lag.
