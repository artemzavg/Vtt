# VTT Platform

Веб-платформа Virtual Tabletop и компендиума на .NET 10 и React 19. Архитектура
строится вокруг bounded contexts, DDD, CQRS и Event Sourcing; первая реализация
правил планируется на открытом SRD-контенте D&D, а доменное ядро не привязано к
одной игровой системе.

Шаг 01 реализует только foundation: 12 изолированных service skeletons, React shell,
тестовые границы и воспроизводимый local Compose. Бизнес-endpoints намеренно
отсутствуют.

## Быстрый старт

Требуются .NET SDK 10.0.x, Node.js 22.12–24.x, pnpm 11 и Docker Compose.

```powershell
.\eng\vtt.cmd bootstrap
.\eng\vtt.cmd core-up
.\eng\vtt.cmd app-smoke
```

После запуска web доступен на `http://127.0.0.1:55173`, диагностика — на
`http://127.0.0.1:55173/system`, Edge health — на
`http://127.0.0.1:5100/health/ready`.

Linux/macOS: используйте те же команды через `sh eng/vtt.sh <command>`.
Подробности, profiles, reset и troubleshooting описаны в
[onboarding](docs/development/onboarding.md).

## Структура

```text
src/backend/BuildingBlocks       технические primitives без shared domain
src/backend/Services             12 bounded contexts, по 7 проектов
src/frontend/apps/web            React application shell
src/frontend/packages            api-client, ui и testing packages
contracts                        OpenAPI, AsyncAPI и schemas placeholders
tests                            architecture, E2E и performance tests
deploy/compose                   core/apps/observability/dev-tools profiles
eng                              bootstrap, verify и безопасный reset
docs                             продукт, архитектура, сервисы и roadmap
```

## Проверка

```powershell
.\eng\vtt.cmd verify
```

Команда выполняет restore, форматирование, Release build, .NET tests, frontend
lint/typecheck/unit/build, bundle baseline, secret scan и Compose config check.

## Документация

- [Оглавление](docs/README.md)
- [Roadmap разработки](docs/roadmap/README.md)
- [Шаг 01](docs/roadmap/step-01-project-foundation.md)
- [Верхнеуровневая архитектура](docs/architecture/overview.md)
- [ADR: monorepo и service boundaries](docs/architecture/adr/0001-monorepo-and-service-boundaries.md)
- [Локальные порты](docs/development/ports.md)
- [Test report шага 01](docs/testing/step-01-test-report.md)

Локальные пароли из `.env.example` предназначены только для loopback-bound
development environment и никогда не должны использоваться в shared/production.
