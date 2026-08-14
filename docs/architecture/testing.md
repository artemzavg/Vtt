# Стратегия тестирования

Статус: `Required quality baseline`.

## 1. Цели

Тесты должны рано обнаруживать:

- нарушение доменных инвариантов и правил конкретной версии ruleset;
- duplicate/lost effects при retries и out-of-order delivery;
- утечку прав между кампаниями/персонажами;
- расхождение browser preview и server authoritative calculation;
- несовместимость API/event schema;
- regressions real-time ordering/reconnect и canvas performance;
- необратимые migration/replay ошибки.

Coverage — индикатор, не цель сам по себе. Важнее scenario coverage, mutation
score, contract compatibility и production-like integration.

## 2. Пирамида тестов

### Unit/domain — каждый commit

- xUnit, pure aggregate tests, no mocks for value objects;
- Given/When/Then event-sourced aggregate specifications;
- property-based tests (FsCheck) для dice parser, modifier algebra, point buy,
  resource counters, coordinate/grid conversion;
- golden cases для D&D character/level/action calculations;
- determinism: одинаковый ruleset/input/RNG result → идентичный output hash;
- permission policy matrix;
- serializer/upcaster fixtures для каждого stored event.

Targets:

- domain/application line coverage ≥90%, branch ≥85%;
- весь production code ≥80% line as a guardrail;
- mutation score ≥70% для rules, dice, authorization и gameplay core;
- 100% известных invariants имеют хотя бы positive + negative case.

### Component/integration — pull request

Testcontainers.NET поднимает реальные PostgreSQL, NATS, Redis и MinIO:

- append expected version/conflict/snapshot/rebuild;
- outbox publish crash windows, inbox deduplication, redelivery;
- projection gap/out-of-order recovery;
- API auth, validation, ProblemDetails, ETag/If-Match, idempotency;
- multipart upload, quota, scan/derivative state machine with safe fixtures;
- SignalR connect/join/resume, node handoff and backpressure;
- RLS/repository tenant filters adversarial tests;
- database migrations forward and rollback/expand-contract path.

Не заменять PostgreSQL SQLite in-memory. Mock допустим для внешнего email/OIDC,
но контракт проверяется WireMock.Net/recorded official schema fixtures.

### Contract — pull request and release

- OpenAPI breaking-change check;
- AsyncAPI/JSON Schema compatibility и consumer-driven fixtures;
- generated TypeScript client compiles against frontend;
- provider verification для критичных BFF compositions;
- current and previous event/API major compatibility;
- schema examples проходят security redaction check.

### Frontend — pull request

- Vitest + React Testing Library: user-observable behavior, не implementation details;
- store/query cache transitions: optimistic, ack, correction, conflict, reconnect;
- formula/provenance presentation и accessibility semantics;
- Pixi adapters unit-test spatial math separately from rendering;
- visual regression для sheet/builder/dialog/layout at reference viewports;
- axe-core + keyboard flows.

### E2E — merge/release

Playwright запускается против production-like composition:

1. register/login/invite/join/logout/revoke;
2. GM creates campaign, ruleset pinned, player accepted;
3. character from level 1 and level N with all ability methods;
4. equipment/spells/effects change derived stats with provenance;
5. upload map → create scene → walls/light/fog/token permissions;
6. two browser contexts see token move and reconnect/resync;
7. attack/save/damage/ammo/status/initiative/chat history;
8. forbidden player cannot see GM layer/hidden roll/other character;
9. refresh during pending command does not duplicate effect;
10. ruleset migration preview and incompatible choice resolution.

Critical smoke runs per deploy; full matrix nightly. Test accounts/fixtures are
ephemeral and uniquely namespaced.

### Performance/soak — nightly and pre-release

- k6 HTTP/WebSocket scenarios from capacity document;
- server BenchmarkDotNet microbenchmarks only for proven hot code (formula,
  visibility geometry, serialization);
- browser performance tests record INP-like interaction, frame budget, heap and
  texture memory on device matrix;
- 8–24 h soak for WebSocket/room/memory leaks;
- query plans and index regression captured for top read/write paths.

### Resilience/chaos — scheduled

- kill realtime node during drag/action, verify reconnect + no duplicate commit;
- pause NATS consumer, restore and catch up;
- fail PostgreSQL primary in staging;
- Redis flush/restart, verify only ephemeral loss;
- object storage timeout/partial multipart;
- clock skew within configured bounds;
- packet loss/latency/offline via browser/network proxy;
- poison event quarantined without blocking entire partition.

### Security — continuous/release

- SAST, secret scanning, dependency/container scanning and SBOM;
- DAST on staging, fuzz dice/formula/package/upload parsers;
- OWASP ASVS-oriented auth/session tests;
- property/adversarial authorization tests across tenant/role/object ACL;
- rate-limit and resource-exhaustion tests;
- third-party pentest before paid/public launch and after major auth/plugin change.

## 3. Golden ruleset test suite

Каждая published ruleset version поставляет signed test vectors:

```json
{
  "caseId": "fighter-l5-standard-array-001",
  "ruleset": "dnd-srd-5-2@5.2.1+platform.1",
  "inputs": { "choices": [] },
  "expected": {
    "profileHash": "sha256:...",
    "derived": {},
    "actions": [],
    "validationCodes": []
  }
}
```

Один corpus исполняется:

- server Ruleset engine;
- Character aggregate calculation;
- browser worker preview;
- migration from previous compatible version;
- Gameplay action resolver subset.

Расхождение блокирует release. Random dice отделены: вектор передаёт заранее
заданные die outcomes, а RNG тестируется отдельно статистически и на audit shape.

## 4. Event-sourced testing

Для каждого агрегата:

- empty/history reconstruction;
- happy path and every rejection code;
- stale expected version;
- duplicate command id;
- snapshot + tail equals full replay;
- upcast old fixtures;
- unknown future additive metadata ignored;
- compensation appends new event, never mutates history.

Projection test:

1. apply event once;
2. apply duplicate;
3. apply older aggregate version;
4. simulate gap and resync;
5. rebuild from zero;
6. compare canonical checksum/query fixtures.

## 5. Real-time correctness harness

Модельная комната поднимает N clients и записывает:

- client command id/sequence;
- server room sequence;
- per-client received sequences;
- authoritative aggregate versions;
- resync snapshots/checkpoints.

Assertions:

- committed messages seen exactly once logically after dedupe;
- sequence monotonic per room, gaps always resyncable;
- forbidden client never receives payload, даже если UI не показывает его;
- slow client получает compact snapshot/disconnect, а не unbounded buffer;
- final token/action state совпадает у всех clients и backing read model.

## 6. Test data and environments

- Builders/factories создают минимальный valid aggregate, не giant shared fixture;
- synthetic maps/content only, no copyrighted production books in repo;
- deterministic seed names recorded, secrets never fixture values;
- local: fast subset + containers on demand;
- CI PR: unit/component/contract/frontend;
- preview environment: E2E/accessibility/security smoke;
- staging: production topology scaled down, load/chaos/replay;
- production: synthetic canaries with isolated tenant, no destructive chaos без
  approved game day.

## 7. CI quality gates

Pull request blocked by:

- format/analyzer/type/lint failure;
- test/coverage/mutation threshold regression beyond approved baseline;
- breaking OpenAPI/AsyncAPI change without version/deprecation;
- migration cannot apply to previous production schema snapshot;
- ruleset golden mismatch;
- critical/high dependency or image vulnerability without time-bound exception;
- Playwright critical path or accessibility critical violation;
- performance budget regression >10% on stable benchmark without approval.

Flaky test не ретраится бесконечно: один diagnostic retry допустим, затем test
quarantined только с owner, issue и deadline; critical path quarantine запрещён.

## 8. Production verification

- canary 1–5% traffic, automatic comparison of errors/latency/correctness;
- shadow calculation for new rules/automation without applying result;
- synthetic join, scene load, dice/chat and command every few minutes;
- post-deploy projection lag/replay checksum;
- feature flag rollback before binary rollback when safe;
- invariant violation creates high-severity alert and quarantines affected command,
  but не исправляет историю автоматически.

## 9. Definition of Done checklist

- domain and authorization tests;
- API/event examples and compatibility;
- integration with real dependencies;
- E2E for changed critical journey;
- metrics/traces/log redaction verified;
- load/performance budget if hot path changed;
- migration, rollback and replay plan;
- documentation/runbook/threat model updated;
- no unexplained coverage or mutation drop.
