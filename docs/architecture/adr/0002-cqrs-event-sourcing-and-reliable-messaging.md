# ADR-0002. CQRS, Event Sourcing и надёжная доставка событий

Статус: `Accepted`  
Дата: 2026-08-15

## Контекст

Сервисы VTT должны независимо принимать конкурентные команды, хранить полную
историю бизнес-решений и обновлять локальные read models. PostgreSQL commit и
публикация в message broker не образуют распределённую транзакцию. NATS даёт
at-least-once delivery, поэтому повторная доставка и отсутствие глобального
порядка являются штатными условиями, а не исключениями.

Платформенный шаблон не должен переносить доменную модель одного bounded context
в другой. Для проверки гарантий нужен нейтральный aggregate без будущей
продуктовой семантики.

## Решение

### CQRS и конкурентность

- Application layer использует узкие `ICommandHandler` и `IQueryHandler`.
- Command metadata содержит command/principal/tenant/correlation/causation,
  deadline, idempotency key и expected aggregate version.
- Каждая изменяющая HTTP-команда требует `Idempotency-Key`; команда к aggregate
  также требует strong `If-Match: "{version}"`.
- Marten `FetchForWriting` фиксирует фактическую stream version, а application
  сравнивает её с expected version клиента. Stale write возвращает стабильный
  `409 aggregate_conflict`, а не перетирает состояние.
- Idempotent command handler допускает один bounded retry при uncertain commit
  outcome. Повтор использует тот же command/key/version; второй сбой не скрывается.
- Query читает только локальную projection и сообщает `projectionVersion`,
  `sourceAggregateVersion`, `asOf` и checksum.

### Event store и транзакция

Каждый сервис владеет своей PostgreSQL database/schema и principal. Одна
`IDocumentSession.SaveChangesAsync` атомарно сохраняет:

1. domain events в aggregate stream;
2. snapshot, если он нужен измеренной стоимостью replay;
3. integration envelope в outbox;
4. idempotency result команды.

Snapshot — оптимизация. Stream остаётся canonical source. Схема в обычном runtime
работает с `AutoCreate.None`; изменение схемы выполняет отдельный one-shot process.

### Outbox, JetStream и inbox

- Relay читает pending outbox batches и публикует в JetStream.
- Subject имеет вид
  `vtt.{producer}.{aggregate}.{event}.v{major}`.
- Retry использует exponential backoff до 30 секунд и максимум 20 попыток, затем
  переводит запись в dead-letter. Operator может вернуть её в pending.
- Между broker publish и отметкой outbox как published есть неизбежное crash
  window. Оно может дать duplicate, но не потерю committed event.
- Consumer записывает inbox result и projection/checkpoint в одной DB transaction.
  Дубликат не повторяет логический эффект.
- Порядок существует только внутри aggregate stream. Old version игнорируется;
  gap вызывает warning и resync из canonical stream.
- Malformed/unsupported event помещается в quarantine и не останавливает
  consumer. Operator retry снимает соответствующий inbox dedupe и публикует
  сообщение с новым transport id, сохраняя canonical event id в envelope.

### Контракты и наблюдаемость

- HTTP описывается OpenAPI 3.1, каналы — AsyncAPI 3.0, payload — JSON Schema
  2020-12. Generated TypeScript является производным артефактом.
- `traceparent` сохраняется в outbox. Relay восстанавливает parent context,
  создаёт producer span и передаёт его transport header в NATS; consumer
  продолжает producer span. Correlation id возвращается клиенту и находится в
  structured log scope.
- Metric labels содержат только bounded result/error/aggregate type. Raw user,
  tenant, event и correlation ids в labels запрещены.
- SQL, параметры, document JSON и event body не логируются. Marten использует
  redacted logger; ошибка сохраняет только тип в metric/trace event.

## Эталонная реализация

`PlatformFixtures/Engineering` содержит нейтральный `ProbeAggregate`. Он проверяет
event append, outbox/inbox, projection, operator recovery и long-running rebuild,
но не является bounded context и не поставляется как продуктовый API.

Технические building blocks разрешено переиспользовать. Fixture Domain,
Application, Contracts и endpoint-ы копировать в сервисы запрещено; сервис создаёт
собственные модели и публичные контракты.

## Последствия

- Сервис продолжает принимать локальные команды при недоступном NATS, пока DB и
  outbox budget доступны; cross-context state становится stale и наблюдаем.
- Exactly-once transport не обещается. Гарантия — at-least-once delivery и
  effectively-once business effect.
- Relay и consumers требуют операционных dashboards/runbooks и контроля lag.
- Event schema эволюционирует additively внутри major; breaking change получает
  новый major и rolling compatibility window.
- Replay projection не вызывает outbox, email, webhook или иной внешний effect.

## Отклонённые варианты

- Dual write PostgreSQL + NATS без outbox: имеет окно потери события.
- Общий event store или shared domain assembly: нарушает ownership bounded context.
- Глобальный ordering: дорого, создаёт hotspot и не гарантируется JetStream.
- Автоматический schema update при каждом production startup: несколько replicas
  могут конкурировать и runtime principal получает лишние DDL-права.
- Wolverine/MassTransit как обязательная abstraction на этом этапе: скрывает
  ключевые гарантии до их проверки и добавляет широкий runtime surface. Решение
  можно пересмотреть отдельным ADR после измерений.
