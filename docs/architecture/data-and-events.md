# Данные, CQRS, Event Sourcing и события

Статус: `Proposed`.

## 1. Зачем и где Event Sourcing

Event Sourcing нужен для аудируемых, воспроизводимых бизнес-решений: создание и
прогресс персонажа, права кампании, публикация ruleset/content, изменение сцены,
броски и игровой runtime. Он не является журналом телеметрии.

Event-sourced:

- `Campaign`, `Membership`, `RulesetDraft`, `CompendiumPack`, `Character`,
  `Scene`, `FogExploration`, `ActorRuntime`, `Encounter`, `ChatChannel`, `MacroSet`;
- security-sensitive account/session transitions, если retention позволяет;
- server dice result и GM compensation.

Не event-sourced как отдельные доменные события:

- cursor position, viewport, hover, drag intermediates, typing indicator;
- health check, metrics, trace spans и CDN access logs;
- generated thumbnails/tiles — они воспроизводимы из source asset;
- search index documents — это rebuildable projections.

## 2. Хранилища и владение

Каждый сервис получает:

- собственную PostgreSQL logical database или schema + отдельного DB principal;
- Marten event store streams/snapshots для своих aggregate types;
- read-model tables, которые может пересоздать из своих событий и integration
  events с сохранёнными checkpoints;
- transactional outbox и consumer inbox/deduplication;
- собственные migrations и backup/restore owner.

Физический PostgreSQL cluster может быть общим на pilot, но:

- сервис не читает чужие таблицы;
- BI/search получают данные событиями/CDC в отдельное хранилище;
- foreign keys между contexts запрещены;
- глобальный идентификатор не означает общую транзакцию.

S3 object storage владеет binary blobs; Media владеет metadata и access policy.
Redis хранит только TTL/derivable state. NATS хранит integration stream по
retention policy, но event store остаётся источником доменной истории.

## 3. Stream и aggregate conventions

- ID: UUIDv7, строковое представление lowercase canonical.
- Stream: `{boundedContext}-{aggregateType}-{aggregateId}`.
- Append требует `expectedVersion`; конфликт возвращает `409 aggregate_conflict`.
- Snapshot — оптимизация загрузки, не источник истины; каждые N событий или при
  превышении measured replay budget.
- Event name: past tense, `PascalCase`, без технического `Updated`, например
  `CharacterAbilityMethodSelected`.
- Event type immutable. Несовместимая семантика получает новое имя/major schema.
- Timestamp — серверный UTC; client timestamp хранится отдельно только как hint.
- Удаление — domain tombstone/retention workflow, а не переписывание stream. Для
  privacy erasure PII отделяется и crypto-shred/anonymize-ится по policy.

## 4. Event envelope

```json
{
  "eventId": "0198...",
  "eventType": "CharacterProfilePublished.v1",
  "occurredAt": "2026-08-14T12:00:00Z",
  "producer": "character-service",
  "aggregate": {
    "type": "Character",
    "id": "0198...",
    "version": 42
  },
  "tenantId": "campaign-or-user-scope-id",
  "subjectId": "user-id-or-null",
  "correlationId": "request-or-workflow-id",
  "causationId": "command-or-parent-event-id",
  "schemaVersion": 1,
  "traceParent": "00-...",
  "data": {},
  "metadata": {
    "rulesetId": "dnd-srd-5-2",
    "rulesetVersion": "5.2.1+platform.1"
  }
}
```

Envelope не содержит access token, email, IP или полный user-agent. `subjectId`
псевдонимизирован. Private text включается только если событие принадлежит сервису,
которому оно нужно; integration event передаёт минимальные данные.

## 5. Domain events и integration events

Domain event описывает факт внутри модели и может быть детальным. Integration
event — стабильный минимальный публичный контракт. Не каждый domain event выходит
в bus.

Пример:

- Domain: `BuildChoiceReplaced(oldChoice, newChoice, invalidatedNodes)`.
- Integration: `CharacterProfilePublished(characterId, profileVersion,
  rulesetRef, projectionUri)`.

Publisher сохраняет domain events и outbox atomically в PostgreSQL. Relay
публикует outbox в JetStream. Consumer сначала записывает `eventId` в inbox в той
же транзакции, где меняет свою projection. Delivery semantics: at least once;
business effect: effectively once благодаря idempotency.

## 6. CQRS

### Commands

- именуются глаголом и отражают намерение (`EquipItem`, не `UpdateCharacter`);
- содержат `commandId`, actor/tenant context, expected aggregate version;
- валидируются в четыре слоя: shape → authorization → business preconditions →
  aggregate invariants;
- повтор с тем же `Idempotency-Key + principal + route` возвращает прежний result;
- async long-running workflow возвращает `202` + operation resource.

### Queries

- не загружают aggregate stream;
- используют screen-oriented projections, cursor pagination и ETag;
- возвращают `projectionVersion`, `asOf` и, где важно, `sourceAggregateVersion`;
- не раскрывают существование запрещённого ресурса: policy выбирает 403/404;
- stale read допустим в рамках bounded staleness SLO.

### Read-your-writes

Command response включает minimal authoritative result и новую aggregate version.
UI немедленно обновляет cache. Если экран требует projection, клиент:

1. применяет optimistic/minimal authoritative patch;
2. получает `projectionPending=true`;
3. принимает projection event через realtime либо делает conditional poll;
4. заменяет patch, когда `projectionVersion >= committedVersion`.

## 7. Событийный каталог верхнего уровня

Полные поля указаны в документации сервисов. Ниже — routing map.

| Integration event | Producer | Главные consumers |
|---|---|---|
| `UserDeactivated.v1` | Identity | Campaign, Character, Session, Chat |
| `CampaignMembershipChanged.v1` | Campaign | Edge authz cache, all tenant services, Session |
| `CampaignRulesetPinned.v1` | Campaign | Character, Scene, Gameplay, Compendium |
| `RulesetVersionPublished.v1` | Ruleset | Character, Compendium, Gameplay, Search |
| `RulesetVersionDeprecated.v1` | Ruleset | Campaign, Character, operator alerts |
| `CompendiumEntryPublished.v1` | Compendium | Search, Ruleset dependency validator |
| `CharacterProfilePublished.v1` | Character | Gameplay, Scene, Search, Session |
| `CharacterAccessChanged.v1` | Character | Campaign authz projection, Session |
| `AssetReady.v1` | Media | Scene, Compendium, Character, Search |
| `AssetQuarantined.v1` | Media | owning service, notifications/audit |
| `SceneActivated.v1` | Scene | Session, Search |
| `SceneTokenChanged.v1` | Scene | Session, Gameplay |
| `FogCheckpointUpdated.v1` | Scene | Session |
| `SessionStarted.v1` | Session | Campaign, Chat, metrics projections |
| `SessionEnded.v1` | Session | Campaign, Chat, retention workers |
| `ActorRuntimeChanged.v1` | Gameplay | Session, Character composite projection |
| `EncounterTurnChanged.v1` | Gameplay | Session, Chat |
| `ActionResolved.v1` | Gameplay | Chat, Search/audit projection |
| `RollRecorded.v1` | Chat & Dice | Gameplay requester, Session, audit |
| `ChatMessageCreated.v1` | Chat & Dice | Session, Search where permitted |

## 8. Process managers / sagas

### Character completion

1. Character validates pinned compiled ruleset and content refs.
2. Appends `CharacterBuildCompleted` and publishes profile.
3. Gameplay consumes profile and creates/rebases `ActorRuntime` idempotently.
4. Scene tokens referencing character receive new display/profile revision.
5. Timeout does not roll back completed character; operation показывает pending
   runtime materialization and retries. Compensation only if product explicitly
   requires deleting the character.

### Ruleset migration

1. Campaign requests migration preview.
2. Ruleset computes compatibility report; Character evaluates affected choices.
3. GM approves immutable migration plan.
4. Campaign pins target version, then characters create individual migration
   drafts; gameplay stays on old profile until each profile published.
5. Mixed profile versions are explicit and shown to GM; forced migration is an
   audited command, not hidden background rewrite.

### Media upload

1. Media reserves quota and returns presigned multipart URL.
2. Client uploads directly to object storage.
3. Complete command verifies size/hash/content type.
4. Worker scans, decodes under limits and creates derivatives/tiles.
5. `AssetReady` or `AssetQuarantined`; owner service may reference only Ready.

## 9. Ordering, concurrency и deduplication

- Глобального порядка нет. Порядок гарантируется только в aggregate stream и
  room sequence.
- Consumer не сравнивает wall-clock timestamps для выбора победителя.
- Integration event несёт aggregate version; consumer игнорирует старую версию,
  буферизует небольшой gap либо инициирует projection resync.
- Client command несёт `clientSequence` для diagnostics, но concurrency решает
  server aggregate/token version.
- Scene editing допускает optimistic concurrency по объекту, не блокирует всю
  сцену; массовая операция имеет batch command и один audit record.

## 10. Schema evolution

- additive optional fields — backward compatible minor change;
- removing/renaming/changing meaning — новый event/API major;
- consumers обязаны игнорировать неизвестные additive fields;
- upcasters превращают старое stored event представление в актуальное in-memory,
  не переписывая историю;
- contract CI проверяет producer/consumer fixtures и AsyncAPI compatibility;
- минимум: текущая и предыдущая public API major поддерживаются в объявленное
  deprecation window;
- replay в shadow database обязателен перед выпуском upcaster/projection changes.

## 11. Retention и snapshots

Рекомендуемый стартовый baseline:

- domain events: срок жизни продукта/агрегата + policy; security review для PII;
- chat: configurable campaign retention, default 1 year; immutable roll records
  могут иметь отдельный срок;
- integration bus: 7–30 дней, достаточных для incident replay; canonical replay
  идёт из owning service;
- presence/typing/cursor: seconds/minutes only;
- idempotency results: 24 часа для interactive commands, дольше для uploads/
  payments-like operations;
- snapshots: после 100–500 событий или replay >50 мс, подтверждается telemetry;
- fog checkpoints: chunked snapshot + bounded vector deltas; compact after scene
  idle/end session без потери domain audit о reveal/reset.

## 12. Disaster recovery и replay

- PITR PostgreSQL + encrypted cross-failure-domain backups;
- object versioning/soft delete для source media;
- NATS не считается единственной резервной копией событий;
- ежемесячный automated restore в isolated environment;
- projection rebuild имеет checkpoint, throttling и tenant filters;
- checksum/golden query comparison до переключения read traffic;
- replay не вызывает внешние side effects: consumers имеют replay mode/outbox
  suppression и отдельные effect handlers.
