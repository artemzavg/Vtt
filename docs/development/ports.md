# Локальные порты и profiles

Все host ports привязаны к `127.0.0.1`. Внутри сети `vtt_local` сервисы
используют стандартные container ports.

| Компонент | Host port | Profile | Назначение |
|---|---:|---|---|
| Web | 55173 | apps | React shell |
| Edge | 5100 | apps | BFF/API smoke |
| Identity…Search | 5101–5111 | apps | API skeletons в порядке roadmap |
| PostgreSQL | 55432 | core/default | service databases |
| NATS client | 54222 | core/default | JetStream |
| NATS monitor | 58222 | core/default | health/diagnostics |
| Redis | 56379 | core/default | transient local cache |
| MinIO API | 59000 | core/default | private object storage |
| MinIO Console | 59001 | core/default | local administration |
| OTLP gRPC/HTTP | 14317/14318 | observability | telemetry ingestion |
| Grafana | 53000 | observability | dashboards |
| Loki | 53100 | observability | logs API |
| Tempo | 53200 | observability | traces API |
| Prometheus | 59090 | observability | metrics API |
| Mailpit SMTP/UI | 51025/58025 | dev-tools | future email flows |

`Identity…Search` mapping: Identity 5101, Campaign 5102, Ruleset 5103,
Compendium 5104, Character 5105, Media 5106, Scene 5107, Session 5108,
Gameplay 5109, ChatDice 5110, Search 5111.

ClamAV не запускается на шаге 01: scanner вводится вместе с isolated media worker на
шаге 09. Зарезервировано имя profile `media-scan` и только внутренний TCP 3310;
host port публиковаться не будет.

