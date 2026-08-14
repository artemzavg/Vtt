# Верхнеуровневая архитектура

Статус: `Proposed target architecture`.

## 1. Архитектурный стиль

Система состоит из independently deployable сервисов, выровненных по bounded
contexts. Внутри сервиса применяется layered/hexagonal architecture:

`API/Consumers → Application commands/queries → Domain → Ports → Infrastructure`.

Запрещён общий `Domain`-проект с сущностями всех сервисов. Разрешены небольшие
platform packages: event envelope, observability, auth middleware, result/error
types и test fixtures. Они не содержат бизнес-правил.

Микросервисность — граница владения и выпуска, а не требование выделять машине по
сервису. Alpha использует совместное размещение контейнеров; production может
масштабировать hot paths независимо.

## 2. Системный контекст

```mermaid
flowchart LR
    U["Player / GM browser"]
    A["Platform operator"]
    CDN["CDN + WAF"]
    EDGE["Edge Gateway / BFF"]
    RT["Session & Realtime"]
    DOM["Domain microservices"]
    OBJ["S3-compatible object storage"]
    IDP["External OIDC providers"]
    OBS["Telemetry / alerting"]

    U --> CDN
    CDN --> EDGE
    U <-->|"WebSocket"| RT
    EDGE --> DOM
    RT --> DOM
    CDN --> OBJ
    EDGE --> IDP
    A --> OBS
    DOM --> OBS
    RT --> OBS
```

## 3. Микросервисы и bounded contexts

| Сервис | Bounded context / владение | Основная нагрузка |
|---|---|---|
| Edge Gateway / BFF | внешний контракт, композиция read models, rate limits | HTTP, auth edge |
| Identity & Access | account, credentials, platform sessions, consent | security-sensitive |
| Campaign | campaign, membership, invitations, ACL/policies | authorization writes |
| Ruleset | system schemas, mechanics, DSL, immutable versions | compile/evaluate rules |
| Compendium | content packs/entries, license/provenance | content read/search |
| Character | character build/progression, choices, loadout, derived profile | complex aggregate writes |
| Media | upload lifecycle, metadata, derivatives, quotas | object processing |
| Scene | persistent scene setup, walls/lights/tokens/fog checkpoints | spatial state |
| Session & Realtime | rooms, presence, join tickets, ordered transient deltas | WebSocket fan-out |
| Gameplay & Encounter | actor runtime, HP/resources/statuses, initiative/actions | authoritative automation |
| Chat & Dice | messages, macros, immutable rolls, RNG | append-heavy |
| Search & Projections | cross-context denormalized read/search views | eventually consistent reads |

Платформенные зависимости (NATS, PostgreSQL, Redis, object storage,
OpenTelemetry collector) не являются bounded contexts и не публикуют доменные API.

## 4. Контейнерная схема

```mermaid
flowchart TB
    subgraph Browser
      React["React SPA / PWA"]
      Pixi["PixiJS canvas"]
      Worker["Rules + vision Web Workers"]
      IDB["IndexedDB cache"]
      React --- Pixi
      React --- Worker
      React --- IDB
    end

    subgraph Edge
      CDN["CDN/WAF"]
      BFF["ASP.NET Core Gateway/BFF"]
      RNodes["SignalR realtime nodes"]
    end

    subgraph Domain
      IAM["Identity"]
      Campaign["Campaign"]
      Rules["Ruleset"]
      Comp["Compendium"]
      Char["Character"]
      Media["Media"]
      Scene["Scene"]
      Session["Session"]
      Play["Gameplay"]
      Chat["Chat/Dice"]
      Search["Search/Projection"]
    end

    subgraph Data
      PG["PostgreSQL databases\nMarten event store + read models"]
      NATS["NATS JetStream"]
      Redis["Redis"]
      S3["S3-compatible storage"]
      OS["OpenSearch when needed"]
    end

    React --> CDN --> BFF
    React <-->|"WebSocket + MessagePack"| RNodes
    BFF --> IAM & Campaign & Rules & Comp & Char & Media & Scene & Play & Chat & Search
    RNodes --> Session
    Session --> Scene & Play & Chat
    IAM & Campaign & Rules & Comp & Char & Media & Scene & Session & Play & Chat --> PG
    IAM & Campaign & Rules & Comp & Char & Media & Scene & Session & Play & Chat <--> NATS
    RNodes & Session --> Redis
    Media --> S3
    CDN --> S3
    Search --> PG
    Search -."growth tier".-> OS
```

Стрелки к `PG` означают отдельные logical database/schema credentials на сервис;
прямые cross-service joins запрещены. На старте несколько баз могут жить в одном
HA PostgreSQL cluster, затем горячие базы переносятся без смены контрактов.

## 5. Технологический стек

### Backend

- **.NET 10 LTS**, ASP.NET Core 10. На дату среза .NET 10 активен до 2028-11-14
  согласно [официальной support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
- C# с nullable reference types, analyzers и warnings-as-errors для production code.
- ASP.NET Core Minimal APIs для focused endpoints; SignalR + MessagePack protocol
  для real-time; gRPC только для измеренных внутренних hot paths.
- Command/query handlers через собственные узкие интерфейсы; FluentValidation для
  boundary validation. Domain model не зависит от framework mediator.
- PostgreSQL + Marten для event streams/snapshots/projections; Dapper или EF Core
  для специализированных read models. Один aggregate stream — одна optimistic
  concurrency boundary.
- Npgsql; UUIDv7; JSONB только для versioned documents, не как замена модели.
- NATS JetStream для durable integration events и work queues; outbox/inbox.
- Redis для presence, short-lived join tickets, distributed rate limits и
  realtime node routing. Redis не является source of truth.
- S3-compatible storage (MinIO local; managed S3/R2-compatible production).
- OpenSearch вводится после доказанной нехватки PostgreSQL FTS/facets, а не в MVP.
- SixLabors.ImageSharp/libvips-based isolated media worker; ClamAV or provider
  malware scan; никогда не декодировать untrusted media в API process.

### Frontend

- **React 19.2**, TypeScript strict; это latest major/minor на дату среза по
  [официальной странице React versions](https://react.dev/versions).
- Vite current stable, версии фиксируются lockfile; pnpm workspace.
- TanStack Query для server state; Zustand/Redux Toolkit допустимы только для
  UI/session state, но не для дублирования всего server cache.
- PixiJS/WebGL2 для canvas; WebGPU как progressive enhancement после profiling.
- Web Workers + OffscreenCanvas where supported для visibility polygons, grid
  indexing, rules preview и тяжёлой обработки.
- IndexedDB/Dexie для immutable assets metadata, ruleset cache, scene snapshot и
  pending idempotent commands; Service Worker для app shell.
- React Testing Library, Vitest, Playwright; axe-core accessibility checks.

### Delivery и эксплуатация

- Monorepo: `src/services/*`, `src/web`, `contracts`, `tests`, `deploy`, `docs`.
- Docker/Compose local; OCI images; Kubernetes + Helm только production growth
  tier. Малый pilot допустимо запускать на orchestrated VMs.
- OpenTofu/Terraform для инфраструктуры; GitHub Actions CI/CD; SBOM, image scan,
  signed artifacts и provenance.
- OpenTelemetry SDK/Collector, Prometheus, Grafana, Loki, Tempo, Serilog.
- OpenAPI 3.1, AsyncAPI 3, JSON Schema/Protobuf для contracts; generated clients.

### Почему не GraphQL как основной write API

Команды имеют доменную семантику, идемпотентность и ожидаемую версию агрегата;
это прозрачнее в command endpoints. BFF может позже предоставить GraphQL для
сложных read-only экранов, но не становится владельцем доменных данных.

## 6. Ruleset architecture

Ruleset package состоит из:

- `manifest`: system id, semantic version, locale variants, dependencies, license;
- typed schemas: character sections, fields, resources, choices, content types;
- definitions: abilities/classes/features/items/spells/conditions/actions;
- expression AST: arithmetic, dice expression references, conditions, selectors;
- modifier algebra: add/set/min/max/multiply, advantage, grants, resistance;
- dependency graph и evaluation order;
- presentation metadata: labels/icons/layout hints, но не executable React code;
- migrations from compatible previous versions;
- golden examples и expected calculations.

Published version immutable. Кампания pin-ит точную версию; migration выполняется
командой с отчётом о несовместимых choices. Серверный engine является истиной,
а browser-реализация используется для мгновенного preview и сверяется contract
fixtures. Будущий advanced extension допускает signed WASM с capability/time/
memory limits, но не входит в initial platform.

## 7. Согласованность и взаимодействие

- Внутри агрегата: strong consistency через expected stream version.
- Между агрегатами одного сервиса: process manager + события; транзакция только
  где владелец и инвариант действительно общие.
- Между сервисами: at-least-once events, idempotent consumers, no distributed 2PC.
- UI read models: eventual consistency с `projectionVersion` и явным pending state.
- Для команды, ответ которой нужен пользователю сейчас, сервис возвращает accepted
  aggregate version и minimal result; projection может догнать позже.
- Cross-service orchestration хранит state machine, timeout и compensation; нельзя
  строить длинную цепочку синхронных HTTP вызовов.

## 8. Основные потоки

### Создание персонажа

```mermaid
sequenceDiagram
    participant UI as React Builder
    participant BFF as Edge BFF
    participant C as Character
    participant R as Ruleset
    participant P as Compendium
    participant G as Gameplay
    participant BUS as NATS

    UI->>BFF: POST /characters (rulesetVersion, campaignId)
    BFF->>C: CreateCharacter
    C-->>UI: draft + version
    UI->>BFF: PUT choice (If-Match, Idempotency-Key)
    BFF->>C: SetBuildChoice
    C->>R: local cached compiled package / evaluate
    C-->>UI: accepted version + validation + preview
    UI->>C: CompleteBuild
    C->>C: append CharacterBuildCompleted
    C->>BUS: CharacterProfilePublished
    BUS-->>G: create/rebase ActorRuntime
    BUS-->>P: update usage projection
    C-->>UI: completed profile reference
```

Ruleset evaluation в steady state использует локальный immutable compiled package,
полученный по event/cache; синхронный вызов Ruleset в диаграмме — cache miss/admin
flow, а не обязательный hop каждого изменения.

### Перемещение токена

```mermaid
sequenceDiagram
    participant UI as Player Canvas
    participant RT as Realtime Node
    participant S as Session Sequencer
    participant SC as Scene
    participant Peers as Room Clients

    UI->>RT: TokenDragDelta(commandId, seq, x, y)
    RT->>S: authorize + route to room shard
    S-->>Peers: ephemeral delta + roomSequence
    UI->>RT: TokenDragEnd(expectedTokenVersion, final position)
    S->>SC: MoveToken (idempotent)
    SC-->>S: committed tokenVersion / correction
    S-->>Peers: TokenMoveCommitted
```

Курсор и промежуточные координаты имеют TTL и не event-source-ятся. Финальное
перемещение, если оно изменяет игровое состояние, сохраняется.

### Атака и урон

```mermaid
sequenceDiagram
    participant UI as Action UI
    participant RT as Session
    participant G as Gameplay
    participant D as Chat & Dice
    participant BUS as NATS
    participant Peers as Players

    UI->>RT: ExecuteAction(actor, action, targets, expectedVersion)
    RT->>G: authorized command
    G->>G: validate range/profile/resources/status
    G->>D: ResolveRoll(requestId, formula, visibility)
    D-->>G: immutable roll result
    G->>G: append ActionResolved + DamageApplied + ResourceSpent
    G-->>RT: result + new actor/encounter versions
    RT-->>Peers: authoritative action delta
    G->>BUS: GameplayStateChanged
    D->>BUS: RollRecorded / ChatEntryCreated
```

Если dice вынесен в отдельный сервис, `ResolveRoll` имеет строгий timeout и
idempotent request id. Для минимизации latency допустима библиотека RNG/formula
в Gameplay, но владелец immutable roll record и публичного контракта остаётся
Chat & Dice; выбор фиксируется ADR после load prototype.

## 9. Развёртывание по масштабу

### Local

Все сервисы и зависимости в Compose; frontend dev server. Опционально service
profiles позволяют поднимать только нужные bounded contexts.

### Pilot

Два app nodes за load balancer, отдельный worker node, managed/HA PostgreSQL или
проверенная primary+replica, Redis, NATS, object storage/CDN. Несколько сервисных
контейнеров совместно размещены, но имеют отдельные credentials, health checks и
deployable images.

### Growth

Kubernetes, HPA по CPU + WebSocket connections + queue lag, dedicated realtime
node pools, PostgreSQL clusters grouped by workload, NATS cluster, Redis HA,
multi-AZ object storage/CDN. Campaign/session получает `regionId` и `roomShard`.

### Large scale

Region-affine campaigns, global routing to home region, read-only global content
replication, independent scene/chat/gameplay shards. Active-active mutation одной
кампании не требуется; controlled failover проще и безопаснее для ordering.

## 10. Архитектурные запреты

- общий database/schema или cross-service SQL join;
- synchronous request chain глубже двух domain hops в interactive path;
- произвольный JS/C# из ruleset, macro или compendium;
- event с полным access token/PII/защищённым текстом без необходимости;
- использование Redis/cache как canonical state;
- долговечное хранение каждого cursor/drag/presence delta;
- authorization только в UI или BFF;
- изменение опубликованного event payload задним числом;
- unbounded list endpoint, dice count, formula recursion, upload или WebSocket frame;
- слепая ретрансляция domain event клиенту без client-specific schema и ACL.
