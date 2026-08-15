# Runbook: event platform

Область: CQRS/Event Store/outbox/inbox/projections Engineering Fixture и будущих
сервисов, использующих ADR-0002. Команды ниже локальные; production требует
аутентифицированного operator API и change ticket.

## Быстрый запуск и health

```powershell
.\eng\vtt.cmd platform-up
Invoke-RestMethod http://127.0.0.1:55120/health/live
Invoke-RestMethod http://127.0.0.1:55120/health/ready
```

OpenAPI доступен по `http://127.0.0.1:55120/openapi/v1.json`. Остановить без
удаления volumes: `.\eng\vtt.cmd down`.

## Диагностика по correlation id

1. Получить `X-Correlation-ID` из ответа/ProblemDetails.
2. В Grafana Explore выбрать Tempo и искать trace id из `traceId` либо Loki и
   structured field `correlation_id`.
3. Проверить цепочку `HTTP server → command → PostgreSQL → outbox publish → NATS
   consumer → projection`.
4. В логах допустимы error code, subject и технические event/correlation ids.
   Request/event body, SQL parameters, token/email/invite code недопустимы.

## Outbox lag или NATS outage

Симптомы: рост `vtt.outbox.message.age`, result `retry/dead_letter`, projection
staleness, readiness 503 при недоступном NATS.

1. Проверить NATS health/JetStream и DB availability. Не перезапускать DB ради
   broker-инцидента.
2. После восстановления убедиться, что pending age уменьшается. Relay продолжит
   доставку автоматически; duplicate после crash window штатен.
3. Для dead-letter получить message id из защищённого operator storage и вызвать
   `POST /platform/outbox/{messageId}/retry`.
4. Если lag не уменьшается, остановить массовый retry, проверить subject/contract
   и consumer capacity. Не увеличивать бесконечно concurrency: сначала исключить
   poison/hot partition.

## Poison event quarantine

1. `GET /platform/quarantine` — взять `quarantineId`, `eventId`, `reasonCode` и
   subject. API намеренно не возвращает payload.
2. Исправить consumer/schema/config и проверить совместимость на fixture.
3. `POST /platform/quarantine/{quarantineId}/retry`.
4. Убедиться, что `retryCount`/`retriedAt` изменились и появился inbox outcome
   `Applied`, `IgnoredOld` или `ResyncedGap`. Повторный quarantine означает, что
   причина не устранена; прекратить цикл retry.

Malformed event terminates только это delivery; остальные aggregates продолжают
обрабатываться. Нельзя удалять quarantine payload до истечения incident/retention
policy.

## Projection gap и rebuild

Gap warning означает, что consumer увидел version больше `current + 1`. Он делает
адресный resync aggregate из canonical stream. Если повреждение массовое:

1. Зафиксировать checksum и representative queries до rebuild.
2. `POST /platform/projections/probes/rebuild`; сохранить `Location`.
3. Poll `GET /platform/operations/{operationId}` до `completed/failed/cancelled`.
4. Сравнить checksum/query results и убедиться, что outbox count не вырос.
5. Cancellation: `DELETE /platform/operations/{operationId}`. Это cooperative
   request; уже закоммиченный rebuild не откатывается.

Rebuild читает canonical streams, не запускает external effect handlers и не
публикует integration events. При несоответствии checksum не переключать traffic,
сохранить старую projection/backup и открыть P1 incident.

## NO-GO / escalation

- committed event отсутствует в outbox;
- duplicate повторяет бизнес-эффект;
- stale command принят без conflict;
- SQL/body/PII появились в logs/traces/metric labels;
- rebuild не детерминирован или вызвал внешний effect;
- breaking contract прошёл CI.

Любой пункт блокирует rollout. Сохранить correlation/operation/event ids и
временной диапазон, но не копировать private payload в issue/chat.
