# Нагрузочная модель, SLI/SLO/SLA и стоимость

Статус: `Initial capacity hypothesis`, цены/допущения на 2026-08-14.

## 1. Термины

- `CCU`: одновременно подключённые пользователи.
- `Room`: активная игровая сессия, baseline 1 GM + 5 players.
- `HTTP RPS`: запросы/сек к API, без CDN assets.
- `WS in`: client messages/sec на realtime edge.
- `WS deliveries`: исходящие logical messages после room fan-out; это важнее
  одного RPS при оценке real-time.
- `Durable commands`: значимые изменения, сохраняемые в event store.

## 2. Базовый профиль одной комнаты

Предположения для capacity test, не прогноз поведения каждого пользователя:

- 6 connected users, 2 активно взаимодействуют в типичный момент;
- HTTP average 0.3 RPS/user, peak 1.5 RPS/user при смене сцены/открытии листов;
- WS inbound average 2 msg/s/user, burst 10 msg/s/user;
- fan-out average 5 recipients;
- cursor/drag delta coalesced до 10–20 Hz только во время действия;
- durable gameplay/chat commands average 0.15/s/user, peak 1/s/user;
- одна кампания: 0.5–2 GB uploaded originals/derivatives как планировочный quota;
- initial scene metadata 100–500 KB; крупные карты грузятся tiled через CDN.

Практическое следствие: нельзя умножать каждый mousemove на event store write и
room fan-out без coalescing. Load test должен моделировать burst и reconnect storm,
а не только равномерные HTTP requests.

## 3. Capacity tiers

| Tier | CCU / rooms | HTTP avg / peak RPS | WS in avg / burst msg/s | WS deliveries avg / burst msg/s | Durable writes avg / peak |
|---|---:|---:|---:|---:|---:|
| Dev | 12 / 2 | 4 / 20 | 24 / 120 | 120 / 600 | 2 / 12 |
| Alpha | 100 / 15–20 | 30 / 150 | 200 / 1 000 | 1 000 / 5 000 | 15 / 100 |
| Launch | 1 000 / 150–200 | 300 / 1 500 | 2 000 / 10 000 | 10 000 / 50 000 | 150 / 1 000 |
| Growth | 10 000 / 1 500–2 000 | 3 000 / 15 000 | 20 000 / 100 000 | 100 000 / 500 000 | 1 500 / 10 000 |
| Large | 50 000 / 7 500–10 000 | 15 000 / 75 000 | 100 000 / 500 000 | 500 000 / 2 500 000 | 7 500 / 50 000 |

Числа `Large` требуют region/shard architecture и не обещаются одним cluster.
В production capacity держится минимум 30% headroom; failover capacity учитывает
потерю одной AZ/node.

## 4. Данные и трафик

Пример Launch за месяц:

- 5 000 registered MAU, 1 000 peak CCU;
- 2 000 active campaigns × 1 GB originals+derivatives = 2 TB object storage;
- 20 session-hours/campaign/month × 0.5 GB CDN delivery = 20 TB egress/cache
  delivery до оптимизации;
- 150 durable writes/s в busy hour, средний event 1 KB → около 13 GB/day raw at
  sustained peak; реальный daily average ниже, но indexes/replicas/backups дают
  3–6× amplification;
- chat/roll retention и projection indexes планируются отдельно;
- observability sampling и log redaction критичны: необузданные debug logs могут
  стоить дороже domain database.

Object storage пример можно считать по текущей публичной цене
[Cloudflare R2](https://developers.cloudflare.com/r2/pricing/): standard storage
$0.015/GB-month, Class A $4.50/million, Class B $0.36/million, заявленный direct
egress $0. Это только иллюстрация; data residency, доступность сервиса из целевого
рынка и условия CDN надо проверить перед выбором.

Managed real-time может заменить self-hosted fan-out. Например, официальный
[Azure SignalR pricing page](https://azure.microsoft.com/en-us/pricing/details/signalr-service/)
указывает 1 000 concurrent connections на Standard/Premium unit и SLA 99.9%/
99.95%; конкретная региональная цена динамична. Build-vs-buy пересматривается по
стоимости messages, egress, операционной нагрузке и доступности региона.

## 5. SLI, SLO и SLA

### SLI definitions

- Availability: доля valid synthetic journey attempts, завершившихся успешно;
  4xx от клиента не считаются ошибкой, 429 анализируется отдельно.
- API latency: server receive → response sent, по endpoint class, без client RTT.
- Realtime latency: server accepted authoritative/ephemeral room message → peer
  node enqueued it; end-to-end client ack измеряется отдельным RUM SLI.
- Correctness: доля committed commands без duplicate/lost/contradictory effect;
  проверяется invariants и reconciliation jobs.
- Freshness: `now - projection source event occurredAt` при доступной bus health.
- Durability: успешно восстановленные committed events в restore drills.

### Рекомендуемые SLO

| Journey | Availability | Latency/Freshness |
|---|---:|---:|
| Existing user joins active game | 99.9% | p95 ≤2 с до interactive metadata |
| Character sheet read | 99.9% | p95 ≤250 мс API |
| Character/gameplay command | 99.9% | p95 ≤300 мс accept/result class |
| Realtime room delivery | 99.9% | p95 ≤150 мс intra-region |
| Dice resolution/record | 99.95% correctness | p95 ≤150 мс |
| Cross-service projections | 99.5% within bound | p99 ≤2 с |
| Public search | 99.5% | p95 ≤400 мс |
| Media upload processing | 99.0% within 5 min | async |

Дополнительно: correctness SLO для gameplay — не более 1 подтверждённого
duplicate/lost state transition на 1 000 000 команд; цель должна измеряться
reconciliation, а не только HTTP 5xx.

### SLA recommendation

- Alpha: SLA отсутствует, публикуется status/known limitations.
- Launch free tier: target SLO 99.9%, договорной SLA не обещать до 3 месяцев
  стабильной статистики и on-call процесса.
- Paid V1: 99.5% или 99.9% monthly core SLA с узким определением journeys;
  99.95% оставлять для зрелой multi-AZ/managed realtime конфигурации.
- Third-party identity/CDN/object/video exclusions и scheduled maintenance должны
  быть ясны, но не превращать SLA в бессмысленную метрику.

## 6. Оценка стоимости

Все значения — инфраструктура в USD/месяц, без НДС, зарплат, payment fees,
коммерческих книг/артов, pentest и поддержки. Диапазон широк из-за региона,
managed/self-hosted выбора и egress. Для российского размещения нужен отдельный
расчёт локальных провайдеров и юридическая проверка.

### Local development

| Компонент | Конфигурация | Оценка |
|---|---|---:|
| Docker Compose dependencies | local workstation, 16–32 GB RAM желательно | $0 |
| MinIO/PostgreSQL/NATS/Redis/OTel | local containers | $0 |
| Optional shared dev/staging VPS | 4–8 vCPU, 16 GB | $30–100 |
| Domain/email/error tracking | optional/free tiers | $0–30 |
| Итого | без стоимости рабочего компьютера | **$0–130** |

### Closed alpha, до 100 CCU

Frugal, без договорного SLA:

- 2 app VMs (4 vCPU/8 GB), 1 data/worker VM или managed small DB;
- PostgreSQL with backups, single NATS/Redis with restore plan;
- 100–500 GB object storage + CDN;
- self-hosted observability with short retention.

Оценка: **$100–400/month**. Нижняя граница имеет single-failure-domain risks;
честная 99.9% конфигурация стоит ближе к следующему tier.

### Production launch, 1 000 CCU

| Статья | Типичная конфигурация | Диапазон |
|---|---|---:|
| App/realtime/workers | 6–12 nodes equivalent, autoscaling | $300–900 |
| PostgreSQL | HA primary/standby + backups, 16–32 vCPU total | $400–1 200 |
| Redis + NATS | HA/managed or 3-node clusters | $150–500 |
| Object storage/CDN | 2 TB stored, 10–30 TB delivery | $50–600 |
| Observability/security/backups | sampled logs/traces, WAF, PITR | $200–800 |
| Итого | single region, multi-AZ | **$1 100–4 000** |

### Growth, 10 000 CCU

Dedicated realtime pool, split PostgreSQL workloads, clustered NATS/Redis,
OpenSearch if justified, 10–30 TB media and 100+ TB delivery. Оценка:
**$7 000–25 000/month**. Managed SignalR, log retention и media egress могут
переместить итог за пределы диапазона.

### Large, 50 000 CCU

Несколько regional/shard cells, failover headroom, dedicated databases, 24×7
operations. Оценка: **$30 000–100 000+/month**. До реальных RUM/usage данных
число не годится для бизнес-плана.

### Video/voice исключено

TURN traffic может стать крупнейшей переменной. Пример: 1 000 одновременных
пользователей × 0.5 Mbps relayed × 2 часа/день ≈ 13.5 TB/month только media
traffic (без SFU overhead). WebRTC/SFU/TURN получает отдельный budget, quotas и
SLO; V1 core не должен зависеть от его доступности.

## 7. Формула ежемесячного бюджета

```text
Total = app_compute
      + database_primary_replicas_backups
      + realtime_units_or_nodes
      + cache_and_bus
      + object_GB_month * storage_rate
      + delivered_GB * egress_rate
      + object_operations
      + logs_metrics_traces
      + WAF_DNS_secrets
      + backup_cross_region
      + support_plan
      + 20..30% headroom
```

Cost telemetry tags: service, environment, region, workload class. Не добавлять
raw campaign/user id как billing/metric label; per-tenant usage агрегируется в
отдельной low-cardinality pipeline.

## 8. Load test plan

### Сценарии

1. `Join storm`: 30% CCU переподключаются за 60 секунд после node restart.
2. `Scene switch`: GM активирует карту, clients запрашивают metadata/tiles.
3. `Token drag`: 20% rooms одновременно двигают 2 tokens, 10 Hz coalesced.
4. `Combat burst`: initiative + 3 attacks/saves/status updates per room.
5. `Chat roll`: сложные, но bounded formulas и hidden rolls.
6. `Builder publish`: concurrent level-N calculations + profile fan-out.
7. `Projection lag`: NATS consumer delayed, затем catch-up без duplicate effects.
8. `Hot room`: 100 observers + GM, проверка isolation/fan-out budget.
9. `Media`: multipart upload + tile worker saturation без влияния core API.
10. `Soak`: 8–24 часа с churn, memory/connection leak и snapshot growth.

### Pass criteria

- SLO соблюдён при target +30% headroom;
- correctness reconciliation = 0 lost/duplicate committed commands;
- DB CPU <70%, connections <70%, queue lag recovers within 5 min;
- node loss не теряет committed state, reconnect p95 в degraded target;
- cost per 1 000 session-hours рассчитан и сравним между releases.

## 9. Scaling triggers

| Сигнал | Действие |
|---|---|
| Realtime node >70% connection/message budget | horizontal scale/rebalance rooms |
| PostgreSQL write p95 >20 мс или CPU >70% sustained | tune/index, split hot service DB |
| Projection lag p99 >2 с | scale consumer, partition stream, inspect poison event |
| Redis memory >65% or evictions | reduce payload/TTL, scale shard; never lose canonical data |
| CDN hit ratio <85% for immutable map tiles | fix cache keys/variants/prewarm |
| Media queue oldest job >2 мин | scale isolated workers/limit upload burst |
| Observability >15% infra bill | sampling/retention/cardinality review |
| One tenant >10% shard throughput | hot-tenant isolation and quota |
