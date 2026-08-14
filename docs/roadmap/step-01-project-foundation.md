# Шаг 01. Подготовка: структура проекта и local environment

Статус: `Manual acceptance` — реализация завершена 2026-08-14; независимый clean-machine walkthrough и M01-12 ожидают выполнения  
Зависимости: нет  
Результат: новый разработчик из чистого clone одной инструкцией поднимает
инфраструктуру, собирает все .NET-заглушки и React-приложение, запускает smoke tests.

## Затрагиваемые сервисы

Все будущие deployables получают только skeleton:

- Edge Gateway/BFF;
- Identity & Access;
- Campaign;
- Ruleset;
- Compendium;
- Character;
- Media;
- Scene;
- Session & Realtime;
- Gameplay & Encounter;
- Chat & Dice;
- Search & Projections.

На этом шаге бизнес-функции не реализуются. Цель — правильные физические границы,
единые инженерные правила и воспроизводимый local environment.

## Разрабатываемые возможности

- понятная monorepo-структура;
- заглушки .NET 10 проектов и dependency rules;
- React 19 + TypeScript strict application shell;
- общий package/build management без shared domain model;
- `docker-compose` для всех локальных инфраструктурных зависимостей;
- health endpoints и smoke page;
- базовые команды bootstrap/build/test/format;
- документация onboarding и схема локальных портов.

## Целевая структура

```text
Vtt/
  src/
    backend/
      BuildingBlocks/          # только технические primitives
      Services/
        Edge/
        Identity/
        Campaign/
        Ruleset/
        Compendium/
        Character/
        Media/
        Scene/
        Session/
        Gameplay/
        ChatDice/
        Search/
    frontend/
      apps/web/
      packages/api-client/
      packages/ui/
      packages/testing/
  contracts/
    openapi/
    asyncapi/
    schemas/
  tests/
    architecture/
    integration/
    e2e/
    performance/
  deploy/
    compose/
    observability/
    kubernetes/                 # пустой placeholder не нужен до этапа 16
  eng/
  docs/
```

Для каждого domain service создаются проекты:

```text
Vtt.<Context>.Api
Vtt.<Context>.Application
Vtt.<Context>.Domain
Vtt.<Context>.Infrastructure
Vtt.<Context>.Contracts
Vtt.<Context>.UnitTests
Vtt.<Context>.IntegrationTests
```

Допускается генерировать их внутренним template, но project references обязаны
соблюдать направление `Api/Infrastructure → Application → Domain`, а `Contracts`
не ссылается на Domain. Edge и Search могут иметь упрощённый набор без fake domain.

## Конкретный план реализации

### 1. Repository baseline

1. Зафиксировать `global.json` с .NET 10 SDK и policy roll-forward.
2. Добавить `Directory.Build.props`, `Directory.Build.targets`,
   `Directory.Packages.props`, `.editorconfig`, analyzers и nullable/warnings policy.
3. Настроить central package management и запрет плавающих версий.
4. Создать `Vtt.slnx`, solution folders и проверить, что каждый проект включён.
5. Добавить `.gitattributes`, line endings, `.dockerignore`, корректный `.gitignore`.
6. Создать `eng` scripts/commands для restore, build, test, format и compose; scripts
   должны быть cross-platform либо иметь PowerShell + documented equivalent.

### 2. Backend stubs

1. Создать сервисные проекты по структуре выше.
2. В каждом API добавить `/health/live`, `/health/ready`, OpenAPI placeholder,
   structured logging и graceful shutdown.
3. Добавить composition root, но не ссылаться на infrastructure из Domain.
4. Добавить architecture tests, запрещающие cross-service project references,
   shared domain types и обратные layer dependencies.
5. Добавить один smoke unit test и один API factory smoke test на template.
6. Проверить публикацию каждого API как Linux container image non-root.

### 3. Frontend stub

1. Настроить pnpm workspace, React 19, TypeScript strict, Vite и lockfile.
2. Создать application shell: routing, error boundary, loading/degraded page,
   runtime config и `/system` diagnostic screen без секретов.
3. Добавить Vitest, React Testing Library, ESLint, formatter и Playwright skeleton.
4. Настроить generated API-client package placeholder; вручную написанные дубли
   server DTO в feature folders запретить правилом/ревью.
5. Добавить accessibility smoke для shell и bundle-size baseline.

### 4. Docker Compose

Создать основной Compose-файл и profiles:

- `postgres`: один local instance, отдельные databases/users создаются init script;
- `nats`: JetStream enabled, persisted volume, monitoring endpoint;
- `redis`: persistence/limits only for development diagnostics;
- `minio`: private buckets + init container, без public anonymous bucket;
- `otel-collector`, `prometheus`, `grafana`, `loki`, `tempo` profile `observability`;
- `mailpit` profile `dev-tools` для email flows;
- `clamav`/media scanner profile добавляется сейчас или на шаге 09, но port/image
  decision documented;
- API stubs и web могут запускаться profile `apps`, hot reload остаётся optional.

Для каждого контейнера: pinned image version, healthcheck, named volume, resource
limit appropriate for laptop, explicit network, no production password reuse.
Добавить `.env.example`, но реальные secrets/credentials не коммитить.

### 5. Developer experience

1. `README` с prerequisites, port map, start/stop/reset и troubleshooting.
2. Команда bootstrap проверяет версии SDK/Node/pnpm/Docker и выдаёт actionable error.
3. Seed только технический: databases/buckets/streams, без copyrighted content.
4. Добавить pre-commit или CI-equivalent checks без обязательного медленного E2E.
5. Зафиксировать ADR о monorepo, project layering и local dependencies.

## Definition of Ready

- [x] утверждены названия bounded contexts и service folder names;
- [x] выбраны поддерживаемые версии .NET, Node/pnpm и container images;
- [x] согласованы host ports, чтобы не конфликтовать с типичной машиной;
- [x] решено, какие сервисы стартуют по умолчанию, а какие через profiles;
- [x] утверждены технические building blocks, которые разрешено разделять;
- [x] описан minimum hardware target для local development;
- [x] существующие пользовательские файлы/настройки репозитория проверены и не
  будут перезаписаны scaffold-генерацией.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M01-01 | На чистой машине/директории выполнить documented bootstrap | Проверены версии; отсутствующая зависимость названа и дана команда установки |
| M01-02 | Выполнить restore и build solution | Все проекты собираются без warning, нет неожиданных downloads после restore |
| M01-03 | Запустить все unit/architecture smoke tests | Все тесты зелёные; deliberate forbidden reference ломает architecture test |
| M01-04 | Выполнить frontend install, lint, typecheck, unit test и build | Lockfile не меняется, strict typecheck зелёный, production bundle создаётся |
| M01-05 | Выполнить `docker compose config`, затем поднять core profile | Compose валиден; PostgreSQL, NATS, Redis, MinIO healthy; данные не публикуются анонимно |
| M01-06 | Поднять profile apps и открыть web shell | Shell загружается, diagnostic page показывает API/commit/config без secrets |
| M01-07 | Проверить `/health/live` и `/health/ready` всех API | Live отвечает процессу; Ready отражает недоступную обязательную локальную зависимость |
| M01-08 | Остановить PostgreSQL/NATS и затем вернуть их | API не падает циклически, readiness становится false и восстанавливается |
| M01-09 | Перезапустить Compose без удаления volumes | Техническое состояние сохраняется; init scripts идемпотентны |
| M01-10 | Выполнить documented reset только в test workspace | Удаляются только явно названные local volumes; повторный bootstrap успешен |
| M01-11 | Собрать и запустить Linux image под non-root user | Health доступен, файловые permission errors отсутствуют |
| M01-12 | Попросить разработчика без контекста пройти README | Он поднимает систему без устных подсказок; пробелы занесены и исправлены |

Результаты M01 сохраняются в test report; скриншоты Grafana/MinIO не должны
содержать реальные credentials.

## Definition of Done

- [x] структура создана и отражена в root/documentation README;
- [ ] все .NET project stubs build/test/publish на Windows и CI Linux;
- [x] React shell lint/typecheck/test/build и Playwright smoke зелёные;
- [x] architecture tests запрещают неправильные layer/cross-service references;
- [x] Compose core стартует одной documented командой и проходит health checks;
- [x] observability profile принимает хотя бы smoke trace/log/metric от одного API;
- [x] контейнеры не используют `latest`, работают non-root где возможно;
- [x] `.env.example` не содержит production secrets, secret scan зелёный;
- [ ] start/stop/reset безопасны и не затрагивают путь вне workspace/именованных
  Compose volumes;
- [ ] ручные M01-01…M01-12 пройдены, P0/P1 defects отсутствуют;
- [x] ADR и onboarding актуальны.

## Критический check перед завершением

### Вопросы

- Не создали ли мы shared `Domain/Common`, в который начнут складывать сущности всех
  сервисов?
- Может ли один сервис сослаться на Infrastructure/Domain другого?
- Реально ли clean build воспроизводим или он зависит от IDE/global packages?
- Не открыты ли PostgreSQL/Redis/MinIO наружу с известными production-like паролями?
- Идемпотентны ли init/reset scripts и ограничены ли они только проектными данными?
- Помещается ли core profile в согласованный laptop memory budget?
- Можно ли независимо собрать image каждого будущего deployable?

### NO-GO условия

- clean clone не собирается;
- Compose требует ручного редактирования secrets/ports без документации;
- Domain зависит от framework/database/другого сервиса;
- reset-команда может удалить данные вне workspace/project volumes;
- health endpoint всегда green при критически неготовом приложении;
- secrets присутствуют в git history/config/diagnostic page.

### Evidence для GO

- CI/build URLs или локальный transcript;
- dependency/architecture test report;
- `docker compose ps` со всеми healthy core dependencies;
- onboarding report второго человека;
- ADR и port/resource table.

## Вне scope

Authentication, domain aggregates, migrations с бизнес-данными, настоящий
ruleset/compendium, canvas и Kubernetes. Заглушка не должна притворяться готовым
сервисом: все business endpoints возвращают только явный `not implemented` либо
вообще отсутствуют.
