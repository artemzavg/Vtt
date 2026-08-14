# Шаг 02. Engineering platform: CQRS, Event Sourcing, contracts и observability

Статус: `Planned`  
Зависимость: шаг 01  
Результат: любой сервис может безопасно принять команду, записать event stream и
outbox, построить projection, повторно обработать integration event и быть
диагностируемым одинаковым способом.

## Затрагиваемые сервисы

Все .NET-сервисы через технические building blocks; пилотная реализация — Campaign
или отдельный test fixture service. Edge получает error/idempotency conventions,
Search — projection consumer template. Domain logic следующих шагов ещё не входит.

## Разрабатываемые возможности

- command/query pipeline, validation и ProblemDetails;
- Marten/PostgreSQL event streams, expected version, snapshots;
- transactional outbox и idempotent inbox;
- NATS JetStream subjects/consumers и event envelope;
- service-local projections, checkpoint/rebuild;
- HTTP idempotency и concurrency headers;
- OpenAPI/AsyncAPI/JSON Schema source of truth + TypeScript generation;
- OpenTelemetry traces/metrics/log correlation;
- CI quality gates, Testcontainers и миграционный workflow.

## Конкретный план реализации

### 1. Contracts and errors

1. Зафиксировать event envelope и naming/version rules из architecture docs.
2. Создать `ProblemDetails` stable codes, correlation/trace middleware.
3. Добавить command metadata: `CommandId`, principal, tenant, correlation,
   causation, deadline, idempotency key, expected version.
4. Настроить OpenAPI 3.1/AsyncAPI validation и generated TypeScript client.
5. Добавить compatibility check current vs main branch contracts.

### 2. CQRS/Event Store

1. Реализовать узкие `ICommandHandler`/`IQueryHandler`, не framework service locator.
2. Добавить aggregate repository поверх Marten с optimistic concurrency.
3. Создать exemplar aggregate с Given/When/Then tests, snapshot and replay.
4. Настроить service-owned migrations/schema/database credentials.
5. Реализовать projection version/asOf и read-your-writes response metadata.

### 3. Reliable messaging

1. В одной PostgreSQL transaction сохранять aggregate events + outbox message.
2. Relay публикует в JetStream с retry/backoff/dead-letter/metrics.
3. Consumer inbox дедуплицирует event id вместе с projection update.
4. Проверять aggregate version: duplicate/old/gap/resync.
5. Добавить poison-event quarantine и operator retry без блокировки всех tenants.

### 4. Idempotency and operations

1. Edge/service idempotency store связывает principal + route + key + request hash.
2. Повтор с тем же key/different payload возвращает conflict.
3. Добавить стандарт long-running operation resource и cancellation semantics.
4. Небезопасные automatic HTTP retries запрещены без key.

### 5. Observability and CI

1. Автоматическая trace propagation HTTP→DB→outbox→consumer.
2. Metrics: command duration/result, conflicts, outbox/inbox lag, projection lag,
   DB pool, NATS redelivery; labels low-cardinality.
3. Structured log redaction и no-body-by-default policy.
4. CI: build, analyzers, unit, integration, contracts, frontend generated client,
   migration apply, container scan/SBOM.
5. Runbook: rebuild projection, retry poison event, inspect correlation id.

## Definition of Ready

- [ ] ADR подтверждает Marten/PostgreSQL и NATS JetStream;
- [ ] event envelope и public API conventions согласованы;
- [ ] chosen test aggregate не несёт будущую бизнес-семантику;
- [ ] database-per-service local strategy и migration naming утверждены;
- [ ] retry/dead-letter/retention limits определены;
- [ ] PII/log redaction policy и metric cardinality rules готовы;
- [ ] CI имеет доступ к container runtime для Testcontainers.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M02-01 | Отправить command с valid expected version | Событие записано, aggregate version увеличена, response содержит correlation/version |
| M02-02 | Повторить command с тем же Idempotency-Key | Возвращён исходный result, второе domain event отсутствует |
| M02-03 | Повторить тот же key с другим body | `409 idempotency_payload_mismatch`, state не изменён |
| M02-04 | Отправить два concurrent commands с одной expected version | Один commit, второй `409 aggregate_conflict` с current version |
| M02-05 | Остановить NATS после commit | Command успешна локально, outbox pending; после старта NATS событие доставлено |
| M02-06 | Принудительно доставить integration event дважды | Projection изменена логически один раз, inbox содержит dedupe record |
| M02-07 | Доставить старую версию и gap | Старая игнорируется; gap алертится/resync, нет повреждения projection |
| M02-08 | Удалить test projection и запустить rebuild | Query checksum совпадает с исходным, внешние side effects не повторяются |
| M02-09 | Отправить malformed/oversized request | Stable ProblemDetails без stack/PII, корректный 4xx |
| M02-10 | Проследить command через Grafana/Tempo | Один correlation виден API→DB→relay→consumer, labels не содержат user text/id raw |
| M02-11 | Подложить poison event fixture | Он quarantined, другие partitions продолжают работать, alert содержит event id |
| M02-12 | Сравнить breaking contract fixture | CI блокирует несовместимое удаление/переименование поля |

## Definition of Done

- [ ] exemplar aggregate покрыт unit/property/integration tests;
- [ ] expected-version и HTTP idempotency semantics стабильны;
- [ ] outbox/inbox выдержали crash-window и duplicate delivery tests;
- [ ] projection rebuild/checksum и poison recovery работают;
- [ ] OpenAPI/AsyncAPI/schemas опубликованы и client generated reproducibly;
- [ ] migration применяется к clean и previous schema fixture;
- [ ] traces/metrics/logs видны; PII/secret redaction test зелёный;
- [ ] architecture docs/runbooks/template обновлены;
- [ ] M02-01…M02-12 пройдены без P0/P1 defects.

## Критический check перед завершением

### Вопросы

- Действительно ли DB transaction включает events и outbox, или есть окно потери?
- Может ли consumer повторить business effect после crash до ack?
- Есть ли глобальный ordering assumption, которого NATS не гарантирует?
- Может ли projection rebuild отправить email/webhook или повторно списать ресурс?
- Не стал ли shared building block носителем доменной модели?
- Можно ли обновлять producer и consumer независимо хотя бы в rolling window?
- Сколько времени/места занимает replay representative long stream?

### NO-GO условия

- duplicate event даёт двойной effect;
- commit может исчезнуть между DB и bus;
- stale command молча перетирает state;
- breaking schema проходит CI;
- traces/logs содержат token, email, body или unbounded tenant labels;
- projection невозможно пересоздать детерминированно.

### Evidence для GO

- crash-window integration report;
- contract compatibility report;
- projection checksum до/после rebuild;
- trace screenshot/link and redaction test;
- benchmark event append/replay baseline.

## Вне scope

Конкретные user/campaign/ruleset aggregates, production Kubernetes, multi-region,
полная platform authorization и business dashboards.
