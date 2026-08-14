# Шаг 18. Экосистема: вторая система, plugins, LFG, video и регионы

Статус: `Planned / discovery-heavy`  
Зависимость: шаг 17  
Результат: архитектура доказана второй существенно отличающейся игровой системой;
дальнейшие ecosystem capabilities добавляются безопасно и экономически осознанно.

Этот шаг — release train, а не разрешение реализовать все направления сразу.
Каждое направление получает отдельный RFC, threat model, cost model, feature flag
и повторяет глобальные DoR/DoD. Первым обязательным инкрементом является вторая
игровая система; без него system-agnostic claim не подтверждён.

## Затрагиваемые сервисы

- **Ruleset/Compendium/Character/Gameplay/Scene/Chat** — вторая система.
- **Edge/Identity/Campaign** — plugin permissions, LFG/social safety.
- **Session/Media** — WebRTC/SFU/TURN и media cost isolation.
- **Search** — LFG/public ecosystem discovery.
- **All services/infra** — region affinity/read replication.
- Возможные новые bounded contexts после ADR: `Plugin Registry`, `Group Discovery`,
  `Billing/Marketplace`, `Communication`. Они не добавляются внутрь существующих
  сервисов только ради ускорения.

## Направления разработки

### 1. Вторая игровая система — обязательный GO gate

- выбрать систему с отличной механикой: dice pool/percentile/classless;
- реализовать ruleset/content/character/action golden corpus без core hardcode;
- выявить необходимые typed primitives; добавлять только generic semantics;
- migration/version/creator documentation and usability test.

### 2. Permissioned plugin ecosystem

- signed manifest/version compatibility/capabilities;
- client Worker/WASM sandbox with CPU/memory/time/network restrictions;
- no direct DB/event-store/DOM/auth token access;
- review/revoke/kill switch and package provenance;
- server extensions только как separately operated trusted integration, не user DLL.

### 3. LFG/community

- profiles/listings/search/applications/scheduling;
- privacy, block/report/moderation, spam/scam/age/safety controls;
- precise disclosure/data retention and rate limits.

### 4. Voice/video

- WebRTC with explicit consent; SFU/TURN provider or isolated service;
- bandwidth/quality/fallback/cost quotas;
- no recording/transcription by default; core game independent.

### 5. Advanced scenes

- regions/triggers, weather, elevation/roofs/occlusion, teleport/pathfinding;
- deterministic permissioned triggers, performance device tiers;
- no arbitrary scripts from scene packages.

### 6. Regional scaling

- campaign home region, global routing, read-only content replication;
- controlled failover and tested RPO/RTO;
- no active-active mutation of one campaign until ordering/conflict need justified.

### 7. AI-assisted suggestions — optional

- opt-in, explainable and non-authoritative;
- no private content sent to provider without explicit policy/consent;
- deterministic rules validation remains final authority;
- cost/safety/evaluation and non-AI fallback mandatory.

### 8. Marketplace — separate business gate

- rights, moderation, payouts, refunds, taxation, fraud, entitlement portability;
- not started under this technical roadmap without dedicated legal/finance plan.

## Definition of Ready

- [ ] выбран один конкретный ecosystem increment, не весь список;
- [ ] user/business metric and cost ceiling justify it;
- [ ] new/existing bounded context ownership ADR approved;
- [ ] threat/privacy/legal/moderation/data residency review complete;
- [ ] failure isolation from core gameplay demonstrated in design;
- [ ] SLO/capacity/egress/support implications budgeted;
- [ ] rollback/revoke/kill switch and compatibility policy ready;
- [ ] V1 error budget allows rollout.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M18-01 | Создать character и encounter второй системы с отличной механикой | Core services не требуют D&D fields/branches; golden outcomes correct |
| M18-02 | Установить valid plugin с минимальной capability | Доступ только к declared API/data; version/signature visible |
| M18-03 | Запустить malicious/CPU/network/DOM/secret-seeking plugin fixture | Sandbox terminates/denies; core/client secrets/state unaffected |
| M18-04 | Revoke plugin во время active campaigns | Kill switch safe, data/export guidance clear, core remains usable |
| M18-05 | Создать LFG listing/apply/block/report/spam attempts | Privacy/audience/moderation/rate limits work; blocked users cannot contact |
| M18-06 | Start voice/video, force TURN/network loss/provider outage | Quality/fallback/cost telemetry clear; text/gameplay continue |
| M18-07 | Проверить consent/privacy with recording disabled | No unexpected capture/storage; indicators and permissions explicit |
| M18-08 | Run advanced region/weather/elevation fixture on device tiers | Semantics/visibility/performance correct; unsupported devices degrade safely |
| M18-09 | Route campaign to home region, fail region/read replica | Controlled reconnect/failover meets policy; no split-brain writes |
| M18-10 | Enable AI suggestion with/without consent/provider outage | Optional/explainable; no unauthorized private send; deterministic validation/fallback |
| M18-11 | Execute full core regression with plugin/video/LFG outages | Character/scene/chat/gameplay SLO and correctness preserved |
| M18-12 | Compare actual unit cost/support load to approved ceiling | Feature can be limited/rolled back before harming business viability |

## Definition of Done

- [ ] second-system end-to-end corpus proves system-agnostic core;
- [ ] каждый выбранный increment имеет отдельный RFC/security/cost/operations;
- [ ] sandbox/revoke/failure isolation verified where plugins selected;
- [ ] privacy/moderation/consent verified where social/video/AI selected;
- [ ] regional changes pass failover/split-brain and data residency review;
- [ ] core regression/SLO/error budget remains healthy;
- [ ] applicable M18 cases accepted, no P0/P1 defects;
- [ ] marketplace не объявлен готовым без legal/finance gate.

## Критический check перед завершением

- Доказана ли универсальность реальной второй системой, а не ещё одним d20 skin?
- Может ли plugin выйти из sandbox/получить token/private data/сломать upgrade?
- Кто модерирует LFG/public packages и как быстро выполняется takedown/block?
- Может ли TURN/SFU bill или provider outage повредить core product?
- Возникает ли split-brain при regional failover?
- Отправляет ли AI private campaign data и можно ли полностью работать без него?
- Готов ли бизнес к marketplace tax/refund/fraud/support burden?

`NO-GO`: D&D hardcode exposed by second system, sandbox escape, unmoderated social
surface, recording/private AI send without consent, core depends on video/plugin,
unbounded cost, split-brain writes, marketplace without legal/finance approval.

Evidence: second-system gap report, sandbox red-team report, moderation/consent
drills, TURN/unit-cost dashboard, regional failover transcript, core regression.
