# Публичные HTTP и event contracts

Статус: `Accepted baseline`  
Дата: 2026-08-15

## Источники истины

| Контракт | Каталог | Версия стандарта |
|---|---|---|
| HTTP API | `contracts/openapi` | OpenAPI 3.1 |
| Channels/operations | `contracts/asyncapi` | AsyncAPI 3.0 |
| Event envelope/payload | `contracts/events` | JSON Schema 2020-12 |
| Compatibility snapshot | `contracts/baseline` | repository-owned manifest |
| TypeScript client | `src/frontend/packages/api-client/src/generated` | generated, не редактируется вручную |

`pnpm contracts:generate` воспроизводимо строит клиент. `pnpm contracts:check`
проверяет версии, subject naming, запрещённые публичные поля, baseline
compatibility, актуальность generated-файла и собственный breaking fixture.

## HTTP conventions

- JSON использует `camelCase`, timestamps — RFC 3339 UTC, ids — UUID.
- Изменяющая retryable-команда требует безопасный `Idempotency-Key` длиной до
  128 символов. Store key: hash от `principal + route template + key`.
- Повтор key с тем же canonical request hash возвращает исходный result; другой
  payload — `409 idempotency_payload_mismatch`.
- Внутренний automatic retry ограничен одной попыткой и разрешён только при
  наличии key, стабильного command id и expected version.
- Aggregate command требует strong `If-Match: "{non-negative version}"`.
- Command response возвращает `ETag`, authoritative aggregate version,
  `X-Projection-Pending` и correlation id.
- Deadline передаётся `X-Command-Deadline`; command/causation —
  `X-Command-ID`/`X-Causation-ID`.
- Long operation: `POST` → `202 Location`, затем `GET operation`; `DELETE`
  запрашивает cancellation и не обещает мгновенную остановку уже выполненного work.
- Ограничение fixture body — 64 KiB. Каждый продуктовый endpoint определяет свой
  меньший или равный threat-model budget.

Ошибки используют `application/problem+json`. `type` имеет вид
`urn:vtt:problem:{stable_code}`; body включает `code`, `correlationId` и `traceId`,
но не stack trace, SQL, token, email или request/event body.

## Integration events

Canonical envelope: `contracts/events/event-envelope.v1.schema.json`.

- `eventId` идентифицирует факт и используется inbox dedupe.
- `eventType` — past-tense PascalCase с major suffix, например
  `CharacterProfilePublished.v1`.
- `aggregate.version` определяет old/next/gap; timestamp не используется для
  разрешения конфликтов.
- `correlationId`, `causationId` и `traceParent` обеспечивают диагностику, но не
  являются authorization context.
- `data` минимален для consumers. Access token, email, IP, raw user-agent и
  произвольный private text запрещены.
- `metadata` имеет bounded set коротких строк; пользовательские ключи и значения
  не становятся metric labels.

Subject: `vtt.{producer}.{aggregate-type}.{event-name}.v{major}`. Все tokens —
lower-kebab-case. Consumer durable name включает projection и major contract.

## Совместимость

Без нового major допустимы additive optional fields и новые response codes, если
клиент способен их обработать. Удаление/переименование поля или operation,
изменение типа/семантики, превращение optional в required и смена subject —
breaking changes.

Producer сначала публикует контракт, который понимают текущий и предыдущий
consumer. Consumer обновляется до producer. После rolling/deprecation window
старый major удаляется отдельным изменением. Stored domain events не переписываются:
для них применяется side-effect-free upcaster и shadow replay.

Baseline обновляется только вместе с review обоснования совместимости. Подгонять
baseline для обхода красного CI запрещено.
