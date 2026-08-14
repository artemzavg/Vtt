# Протокол тестирования: шаг 01

Дата: 2026-08-14  
Среда: Windows, .NET SDK 10.0.302, Node.js 22.12.0, pnpm 11.19.0, Docker Desktop.

## Автоматизированные проверки

| Проверка | Статус | Evidence |
|---|---|---|
| NuGet restore и audit | Passed | OpenTelemetry 1.12.0 отклонён из-за NU1902; зависимости обновлены до 1.17.0, restore завершён без предупреждений |
| Release build | Passed | 86 .NET-проектов, 0 warnings, 0 errors |
| Backend tests | Passed | 27/27: 12 unit, 12 integration health smoke, 3 architecture tests |
| `dotnet format` | Passed | `--verify-no-changes --no-restore` |
| Frontend lint и boundaries | Passed | ESLint и запрет handwritten `*Dto`/импортов backend source |
| Frontend typecheck | Passed | 4/4 workspace packages, TypeScript strict |
| Frontend unit и a11y | Passed | 4/4 Vitest/RTL/axe tests |
| Frontend E2E | Passed | Playwright Chromium: app shell, diagnostics, клавиатура, отсутствие критических axe violations |
| Frontend production build | Passed | JS 201642 bytes при baseline 350000 bytes |
| Secret scan | Passed | Проверка исходников и конфигурации не нашла credential-like значений вне разрешённого `.env.example` |
| Developer tooling | Passed | `vtt.cmd doctor` работает при системной политике `AllSigned`; ошибки дочернего процесса возвращаются вызывающей стороне |
| Compose config | Passed | Конфигурация всех profiles валидна; floating tags отсутствуют |
| Core containers | Passed | PostgreSQL, NATS JetStream, Redis и MinIO healthy; init jobs завершились с exit code 0 |
| API containers | Passed | Все 12 API images собраны; `/health/live` и `/health/ready` ответили успешно на портах 5100–5111 |
| Web container | Passed | `/system` вернул 200; контейнер работает от UID 101 |
| Non-root API | Passed | Edge API работает от UID 1654 |
| Readiness degradation | Passed | При остановке NATS: liveness 200, readiness 503 с причиной; после запуска NATS readiness восстановился до 200 |
| Observability smoke | Passed | Prometheus получил HTTP/.NET metrics, Loki — structured logs, Tempo — traces, Grafana health — OK |
| Safe stop | Passed | `vtt.cmd down` удалил контейнеры и сеть всех profiles; восемь project-owned named volumes сохранены |

## Ручные сценарии M01

Сценарии и подробные инструкции определены в
[roadmap шага 01](../roadmap/step-01-project-foundation.md).

| ID | Статус | Комментарий |
|---|---|---|
| M01-01 | Partial | `doctor`/`bootstrap` реализованы; необходим повтор на чистой машине другим разработчиком |
| M01-02 | Passed | restore и Release build выполнены |
| M01-03 | Passed | backend tests и architecture tests выполнены |
| M01-04 | Passed | lint, typecheck, unit/a11y, production build и Playwright выполнены |
| M01-05 | Passed | все profiles проходят `docker compose config`; core поднят и проверен |
| M01-06 | Passed | app shell и `/system` проверены Playwright и через контейнерный HTTP endpoint |
| M01-07 | Passed | liveness/readiness всех 12 сервисов проверены |
| M01-08 | Passed | отказ и восстановление обязательной зависимости проверены на NATS |
| M01-09 | Passed | init jobs повторно выполняются идемпотентно без удаления volumes |
| M01-10 | Partial | destructive reset защищён явным подтверждением; сам reset намеренно не запускался, чтобы сохранить тестовые данные |
| M01-11 | Passed | API и web работают non-root; host-порты привязаны к loopback |
| M01-12 | Pending | нужен разработчик без контекста реализации для независимого walkthrough |

## Критический review

- P0/P1 дефекты в выполненных проверках не обнаружены.
- Бизнес-функциональность в заглушки не добавлена; корневой endpoint явно сообщает `foundation-only`.
- Направление зависимостей слоёв и изоляция bounded contexts закреплены architecture tests.
- В shared building blocks нет общей Domain-модели; Contracts принадлежат сервисам.
- Внешние DTO пока отсутствуют; правило генерации API client и запрет handwritten DTO уже действуют.
- Локальные пароли находятся только в `.env.example` и помечены как непригодные для shared/production окружений.
- Формальный перевод шага в `Done` возможен после M01-01 на чистой машине, M01-10 в disposable environment и независимого M01-12.

После проверки контейнеры можно остановить командой `.\eng\vtt.cmd down`; named volumes сохраняются.
