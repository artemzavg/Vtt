# Границы MVP и дорожная карта

Статус: `Proposed`. Даты намеренно не назначены: они зависят от размера команды,
лицензирования контента и результатов нагрузочных прототипов. Фазы определены
проверяемыми outcomes, а не календарём.

Подробный порядок реализации с отдельными DoR, DoD, ручными test plans,
затрагиваемыми сервисами и критическими Go/No-Go проверками находится в
[исполняемом roadmap разработки](../roadmap/README.md). Эта страница остаётся
кратким product-level представлением фаз и рисков.

## Стратегия поставки

Архитектура сразу сохраняет границы bounded contexts и отдельное владение
данными, но это не означает отдельный Kubernetes cluster или выделенную VM на
каждый сервис в alpha. Контейнеры можно совместно размещать; контракты, схемы и
pipelines остаются раздельными. Так микросервисы не превращаются в обязательные
12 оплачиваемых серверов до появления нагрузки.

Каждая фаза должна иметь feature flags, миграцию/rollback и измеряемые product +
reliability metrics. Следующая фаза не начинается, если основной пользовательский
путь предыдущей не проходит E2E и load smoke tests.

## Phase 0 — архитектурный runway

Цель: минимальный production-like skeleton и проверка рискованных гипотез.

- monorepo, service templates, .NET/React build, lint/analyzers;
- local Docker Compose: PostgreSQL, NATS JetStream, Redis, MinIO, OTel stack;
- Identity, Campaign и Edge skeleton;
- event envelope, outbox/inbox, idempotency, OpenAPI/AsyncAPI validation;
- WebSocket prototype: 1 GM + 5 players, reconnect/resume, ordered room sequence;
- PixiJS scene prototype: 100–300 tokens, pan/zoom/drag, Web Worker vision spike;
- rules expression parser/AST, deterministic evaluation и cycle detection spike;
- Testcontainers, Playwright, k6 baseline;
- threat model для auth, uploads, public content, formulas и realtime commands.

Exit criteria:

- 100 виртуальных комнат × 6 connections на стенде без потери authoritative
  commands; p95 delivery укладывается в целевой бюджет;
- повторная доставка любого integration event не меняет итог дважды;
- event stream можно восстановить в новую projection и сравнить checksum;
- один ruleset fixture одинаково вычисляется в .NET и browser worker.

## Phase 1 — vertical slice killer feature

Цель: закрытая alpha, доказывающая основную ценность.

- D&D open-content ruleset package с точной версией и атрибуцией;
- builder: abilities, species/origin, background, class, level N, HP, starting
  equipment, basic spells;
- derived sheet с provenance и explain panel;
- campaign/invite/roles/character ACL;
- media upload, одна scene, grid, tokens, basic fog/manual reveal;
- chat, server dice, arbitrary safe formulas;
- encounter: initiative, weapon/spell action, attack/save, damage/HP/ammo/status;
- reconnect, command idempotency, basic audit;
- admin-free operator runbook, backups и restore drill.

Exit criteria:

- новая группа создаёт персонажей и проводит типовой бой без ручного изменения
  вычисляемых полей;
- critical path E2E стабилен, domain line coverage ≥90%, mutation score ядра ≥70%;
- 100 CCU / 15 rooms проходят 60-минутный soak без correctness errors;
- восстановление из backup укладывается в alpha RTO/RPO.

## Phase 2 — playable MVP

Цель: публичный pilot, которым можно регулярно пользоваться.

- walls/doors, automatic fog exploration, light, darkvision, GM vision preview;
- folders, multiple scenes, handouts/journal minimum;
- complete level-up, multiclass, equip/attune effects, rests/resources;
- compendium search/facets, campaign homebrew drafts;
- templates/ruler/drawings/ping, undo/redo для setup;
- observer/guest join, moderation/report minimum;
- PWA shell, offline read cache и low-bandwidth mode;
- responsive tablet/mobile sheet;
- production observability, on-call alerts, status page.

Exit criteria:

- 1 000 CCU target profile выдержан с 30% headroom;
- published SLO dashboard и error-budget policy;
- accessibility audit основных flows WCAG 2.2 AA;
- restore test, key rotation и incident drill выполнены.

## Phase 3 — полноценная V1

- animated maps/tiles, playlists, ambient sounds;
- conditions/durations/concentration/reactions/auras deeper automation;
- roll tables, decks, macro hotbar;
- public homebrew publishing, moderation и immutable package versions;
- import/export, printable sheet;
- RU/EN localization и content variants;
- session calendar/RSVP;
- creator SDK для rules/content packages;
- billing/quotas при выбранной бизнес-модели.

Exit criteria: 10 000 CCU architecture load test, 99.9% SLO over agreed window,
documented incident response, security review and legal content review.

## Phase 4 — ecosystem

- второй официальный/партнёрский ruleset: проверка реальной system-agnostic модели;
- permissioned plugin sandbox и compatibility matrix;
- LFG/community discovery;
- marketplace only after rights, moderation, payouts and tax design;
- optional WebRTC voice/video service;
- multi-region read edges and region-affine campaigns;
- advanced regions, weather, elevation/pathfinding;
- AI-assisted recommendations only as optional, explainable suggestions.

## Feature flags и beta policy

- Flag key включает bounded context, feature и ruleset version.
- Rollout: internal → selected campaigns → percentage → general availability.
- Изменение event schema не скрывается только flag: old consumers обязаны
  корректно игнорировать additive fields.
- Automation feature имеет shadow mode: вычисляет результат, но не применяет,
  сравниваясь с ручным действием GM.
- Emergency kill switch отключает конкретную автоматику без остановки чата/сцены.

## Главные риски и способы проверки

| Риск | Ранний эксперимент | Решение при провале |
|---|---|---|
| Lighting/vision тормозит слабые устройства | worker prototype + device matrix | quality presets, simplified polygons, server precompute only where useful |
| Универсальная rule DSL не выражает D&D | golden scenarios из builder/combat | typed extension primitives; WASM только позже |
| Event Sourcing раздувает storage | representative event/retention test | snapshots, compaction derived telemetry; не писать ephemeral events |
| Realtime fan-out дорог | 1k/10k connection load test | room sharding, binary payload, managed SignalR option |
| Права протекают через projections/cache | adversarial multi-tenant tests | mandatory tenant key, policy SDK, cache key audit |
| Лицензия не покрывает желаемый compendium | content manifest legal audit | только SRD/open/user-owned content, partner licenses |
| Микросервисы замедляют малую команду | lead-time/incident metrics | совместное размещение и platform templates, не ломая ownership |

## Product metrics

- `CharacterCreationCompletionRate` и медианное время до playable sheet;
- доля автоматических значений, исправленных вручную;
- доля explain-panel opens, приведших к сохранённому/отменённому изменению;
- sessions started per created campaign и weekly returning campaigns;
- reconnect success, median join-to-interactive time;
- automation override rate по ruleset/action;
- количество correctness incidents на 1 000 encounters;
- доля homebrew packages, прошедших publish validation с первой попытки.
