# Локальная разработка

## Требования

- .NET SDK 10.0.302 или совместимый 10.0 patch согласно `global.json`;
- Node.js от 22.12 до 24.x;
- pnpm 11.19+ в пределах major 11;
- Docker Engine 24+ и Docker Compose 2.24+;
- PowerShell 7+ на Windows либо POSIX shell на Linux/macOS.

Рекомендуемая машина: 4 CPU cores, 16 GB RAM, 8 GB свободного места. Core profile
ограничен примерно 1.8 GB RAM. Все API и observability одновременно могут занять
4–6 GB, поэтому обычно запускайте только нужный profile.

## Первый запуск

PowerShell:

```powershell
.\eng\vtt.cmd bootstrap
.\eng\vtt.cmd core-up
.\eng\vtt.cmd app-smoke
```

Linux/macOS:

```sh
sh eng/vtt.sh bootstrap
sh eng/vtt.sh core-up
sh eng/vtt.sh app-smoke
```

Откройте `http://127.0.0.1:55173` и экран `/system`. Core работает с
безопасными local-only defaults. Для изменения портов/локальных паролей скопируйте
`.env.example` в `.env`; этот файл игнорируется Git.

## Частые команды

| Задача | PowerShell | POSIX shell |
|---|---|---|
| Проверить инструменты и Docker daemon | `.\eng\vtt.cmd doctor` | `sh eng/vtt.sh doctor` |
| Restore | `.\eng\vtt.cmd restore` | `sh eng/vtt.sh restore` |
| Build backend + frontend | `.\eng\vtt.cmd build` | `sh eng/vtt.sh build` |
| Полная быстрая проверка | `.\eng\vtt.cmd verify` | `sh eng/vtt.sh verify` |
| Core dependencies | `.\eng\vtt.cmd core-up` | `sh eng/vtt.sh core-up` |
| Engineering platform fixture | `.\eng\vtt.cmd platform-up` | `sh eng/vtt.sh platform-up` |
| Platform integration/contracts | `.\eng\vtt.cmd platform-test` | `sh eng/vtt.sh platform-test` |
| Edge + web smoke | `.\eng\vtt.cmd app-smoke` | `sh eng/vtt.sh app-smoke` |
| Все API + web | `.\eng\vtt.cmd apps-up` | `sh eng/vtt.sh apps-up` |
| Edge + observability | `.\eng\vtt.cmd observability-up` | `sh eng/vtt.sh observability-up` |
| Mailpit | `.\eng\vtt.cmd dev-tools-up` | `sh eng/vtt.sh dev-tools-up` |
| Остановить | `.\eng\vtt.cmd down` | `sh eng/vtt.sh down` |

После `platform-up` fixture доступен на `http://127.0.0.1:55120`, а его OpenAPI —
на `/openapi/v1.json`. Append/replay baseline: `pnpm platform:benchmark`.

## Безопасный reset

Reset удаляет только контейнеры и именованные volumes из
`deploy/compose/compose.yaml`. Он требует явного подтверждения.

PowerShell:

```powershell
.\eng\vtt.cmd reset -ConfirmReset
```

POSIX:

```sh
VTT_CONFIRM_RESET=vtt sh eng/vtt.sh reset
```

Не заменяйте эту команду глобальной очисткой Docker.

## Режим без Docker

Backend API можно запустить напрямую:

```powershell
dotnet run --project src/backend/Services/Edge/Vtt.Edge.Api
```

Frontend dev server проксирует `/api` на `http://127.0.0.1:5100`:

```powershell
pnpm dev
```

Без `VTT_READINESS_TCP_ENDPOINTS` readiness проверяет только обязательные
зависимости, перечисленные в конфигурации. Compose задаёт PostgreSQL и NATS, поэтому
остановка любого из них делает `/health/ready` нездоровым, не меняя liveness.

## Troubleshooting

- `global.json SDK not found`: установите .NET 10 SDK; runtime недостаточен.
- `ERR_PNPM_IGNORED_BUILDS`: не включайте все scripts. В workspace разрешён
  только `esbuild` через `allowBuilds`.
- занят порт: измените соответствующий `VTT_*_PORT` в local `.env`.
- Docker daemon недоступен: запустите Docker Desktop/Engine и повторите `doctor`.
- readiness красный: проверьте `docker compose ps` и доступность PostgreSQL/NATS.
- MinIO buckets отсутствуют: проверьте завершение `minio-init` без ошибки.
- NuGet user config ломает restore: используйте documented `--configfile NuGet.config`.
