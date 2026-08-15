# Протокол тестирования: шаг 02

Дата: 2026-08-15  
Среда: Windows, .NET SDK 10.0.302, Node.js 24.19.0, pnpm 11.19.0,
Docker Desktop 29.0.1, PostgreSQL 17.6, NATS 2.11.3.

## Автоматизированные проверки

| Проверка | Статус | Evidence |
|---|---|---|
| Repository quality gate | Passed | `eng/vtt.cmd verify`: restore, format, Release build, backend/frontend tests, contracts, security и Compose config |
| Backend tests | Passed | 53/53: unit/property, service integration smoke, architecture, contracts и 9 platform integration scenarios |
| Engineering unit/property tests | Passed | 11/11: aggregate Given/When/Then, FsCheck, CQRS validation/deadline, subject/envelope и bounded retry |
| Engineering integration tests | Passed | 9/9 на disposable PostgreSQL 17.6 и NATS 2.11.3 через Testcontainers |
| Contract tests | Passed | 4/4; OpenAPI 3.1.1, AsyncAPI 3.0.0, JSON Schema 2020-12, stable ProblemDetails и redaction rules |
| Generated TypeScript | Passed | Генерация воспроизводима; lint/typecheck/build зелёные, handwritten DTO не добавлены |
| Frontend regression | Passed | ESLint без warnings, 4/4 Vitest, strict typecheck всех packages, production build 201651/350000 bytes |
| NuGet/security | Passed | Restore/audit без vulnerability warnings; high-confidence secret scan не нашёл утечек |
| Production-like startup | Passed | PostgreSQL/NATS → init jobs → one-shot Marten migration → API с `AutoCreate.None`; fixture healthy |
| Clean migration | Passed | Уникальная disposable database: apply + идемпотентный повтор; создано 14 таблиц schema `engineering`, database затем удалена |
| Previous-schema migration | Passed | One-shot migrator обновил сохранённую раннюю schema до event store + platform documents без очистки volume |
| Runtime append/replay baseline | Passed | 100 sequential appends: 52.59 RPS, p50 15.48 ms, p95 62.98 ms, max 70.54 ms; replay 429.85 ms |
| Observability | Passed | Tempo trace `f120188773b9421586c9da1780277cb1`: 12 spans; producer span равен parent consumer span |
| Metrics | Passed | Prometheus получил command result/duration, outbox result/age, inbox result и projection lag series |
| Logs/redaction | Passed | Loki получил structured logs; regression запрещает SQL, parameters, event body и common secret/PII fields |
| CI supply chain | Configured | Coverage artifacts, clean migration, CycloneDX SBOM и Trivy HIGH/CRITICAL image gate добавлены; remote CI run ожидает push |

Benchmark выполнен на локальном последовательном клиенте и является baseline, а
не доказательством production SLO или предела пропускной способности.

## Ручные сценарии M02

Сценарии и ожидаемые результаты определены в
[roadmap шага 02](../roadmap/step-02-engineering-platform.md).

| ID | Статус | Evidence |
|---|---|---|
| M02-01 | Passed | Два последовательных append увеличили stream/snapshot до версии 2; response содержит version/correlation |
| M02-02 | Passed | Тот же key/body возвращает исходный event id; stream содержит одно событие |
| M02-03 | Passed | Тот же key с другим body возвращает `409 idempotency_payload_mismatch` |
| M02-04 | Passed | Из двух concurrent version-0 команд ровно одна успешна, вторая получает `409 aggregate_conflict` |
| M02-05 | Passed | При остановленном NATS command коммитится и outbox остаётся pending; после старта projection догоняет |
| M02-06 | Passed | Повторная доставка даёт inbox outcome `Duplicate`, checksum projection не меняется |
| M02-07 | Passed | Old event игнорируется; gap выполняет resync из canonical stream |
| M02-08 | Passed | Rebuild возвращает прежний checksum и не создаёт новый outbox |
| M02-09 | Passed | Malformed, oversized и unknown-field requests получают безопасный стабильный 4xx ProblemDetails |
| M02-10 | Passed | Tempo показывает HTTP → command/DB → outbox publish → NATS consumer; Prometheus/Loki получают telemetry |
| M02-11 | Passed | Poison message quarantined, operator retry учтён, последующие сообщения продолжают обрабатываться |
| M02-12 | Passed | Breaking contract fixture отклоняется compatibility verifier; baseline и generated client проходят |

## Критический GO/NO-GO review

- Events, snapshot, outbox и idempotency result сохраняются одной Marten
  transaction. Отдельного PostgreSQL→NATS dual-write нет.
- Crash после publish до отметки outbox может дать duplicate, но inbox в одной
  transaction с projection/checkpoint не повторяет логический эффект.
- Ordering используется только по версии aggregate; глобального ordering
  JetStream реализация не предполагает. Old/gap пути протестированы.
- Rebuild читает canonical streams и не вызывает relay или внешние effects;
  checksum и неизменность outbox подтверждены.
- Shared building blocks содержат только технические abstractions. Нейтральная
  модель `ProbeAggregate` остаётся в `PlatformFixtures`, а не в shared Domain.
- Producer/consumer совместимость закреплена versioned subject, envelope major,
  JSON Schema и additive compatibility baseline.
- Redaction-тесты и runtime telemetry не выявили body, SQL parameters, token,
  email или unbounded tenant labels.

P0/P1 дефектов после исправлений не осталось. NO-GO условия roadmap не
воспроизводятся. Remote CI/SBOM evidence появится после публикации ветки; это не
скрывается как локально выполненная проверка.

После проверки стенд останавливается через `eng/vtt.cmd down`; named volumes
сохраняются.
