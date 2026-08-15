# Workflow миграций PostgreSQL/Marten

## Ownership и права

Каждый bounded context владеет database/schema и отдельным principal. Fixture
шага 02 использует database `vtt_engineering`, schema `engineering`, principal
`vtt_engineering`. Сервис не читает таблицы другого контекста и не создаёт
межсервисные foreign keys.

Production runtime запускается с `VTT_APPLY_SCHEMA=false` и без DDL-процесса в
API startup. One-shot process использует тот же versioned application image:

```powershell
dotnet Vtt.EngineeringFixture.Api.dll --migrate-only
```

Он вызывает `ApplyAllConfiguredChangesToDatabaseAsync(CreateOrUpdate)` и
завершается до старта API. В Compose это сервис `engineering-migrate`; основной
контейнер зависит от его успешного завершения.

## Изменение схемы

1. Изменить Marten mapping рядом с owning Infrastructure project.
   Каждый persisted event type явно регистрируется через
   `options.Events.AddEventType<T>()`, чтобы one-shot migrator видел event-store
   storage до первой команды.
2. Проверить additive/backward-compatible rolling window. Код N должен работать
   со схемой N и, когда требуется rolling deploy, с переходной схемой N+1.
3. Обновить migration fixture. Для первой версии platform schema предыдущим
   состоянием `v0` считается пустая service-owned database.
4. Выполнить integration tests на disposable PostgreSQL.
5. Выполнить `--migrate-only` дважды: первый apply и идемпотентный повтор.
6. Для destructive/backfill изменения использовать expand → backfill → switch →
   contract в отдельных deploy. Не удалять колонку в том же rollout, где код
   перестал её читать.
7. Перед merge проверить backup/rollback plan и обновить runbook.

Локальная проверка полного one-shot пути:

```powershell
.\eng\vtt.cmd platform-up
docker compose -f deploy/compose/compose.yaml --profile platform-tests run --rm engineering-migrate
```

POSIX:

```sh
sh eng/vtt.sh platform-up
docker compose -f deploy/compose/compose.yaml --profile platform-tests run --rm engineering-migrate
```

## Правила безопасности

- `AutoCreate.All/CreateOrUpdate` в long-running production API запрещён.
- Runtime и migration principals разделяются перед production; локально они могут
  совпадать только как явно документированное упрощение.
- Connection strings и DDL не попадают в application logs/artifacts.
- Drop/rewrite event history требует отдельного ADR, backup и rehearsal.
- Не считать JetStream резервной копией event store.
