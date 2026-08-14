# Нефункциональные требования

Статус: `Proposed`. Числа становятся обязательными после утверждения владельцем
продукта и замеров на agreed reference environment.

## 1. Производительность клиента

Reference desktop: 4 logical cores, 8 GB RAM, integrated GPU, современный Chrome/
Edge/Firefox; сеть 20 Mbps, RTT до региона 80 мс. Reference mobile/tablet задаётся
отдельным device matrix.

| SLI | Target SLO |
|---|---|
| LCP app shell, warm CDN, p75 | ≤2.5 с |
| INP non-canvas UI, p75 | ≤200 мс |
| Route-to-interactive cached sheet, p95 | ≤1.0 с |
| Scene first interactive after metadata, p95 | ≤3.0 с без ожидания full-resolution tiles |
| Local token drag input latency, p95 | ≤50 мс |
| Canvas frame rate during ordinary scene interaction | p95 frame ≤22 мс (~45 FPS), goal 60 FPS |
| Main-thread long tasks | <1% session time over 50 мс |
| JS initial compressed budget | ≤350 KiB app shell; scene/builder lazy chunks separately |

Требования реализации:

- React не re-render-ит canvas objects; Pixi scene graph обновляется адресно;
- spatial index, visibility и formula batches идут в workers;
- texture atlases, mipmaps/tiles, viewport culling и LOD;
- network updates coalesce до 10–20 Hz, rendering остаётся 60 Hz локально;
- immutable rules/content и media кэшируются по content hash;
- quality presets отключают animated lights/shadows на слабых устройствах;
- performance marks: bootstrap, auth, campaign loaded, scene metadata, first tile,
  interactive, reconnect complete.

## 2. Backend latency и availability

| User journey / SLI | SLO normal | SLO degraded |
|---|---:|---:|
| Cached/read-model API p95 / p99 | 250 / 600 мс | 500 / 1 200 мс |
| Domain command accepted p95 / p99 | 300 / 800 мс | 700 / 1 500 мс |
| Server receive → peer delivery p95 / p99, same region | 150 / 300 мс | 300 / 700 мс |
| Dice resolve p95 / p99 | 150 / 400 мс | 300 / 800 мс |
| Reconnect + delta resync p95 | 2 с | 5 с |
| Search p95 / p99 | 400 / 1 000 мс | 800 / 2 000 мс |
| Projection staleness p99 | 2 с | 10 с |
| Core monthly availability | 99.9% | error-budget policy applies |

Core = login for existing session, join active campaign, scene state, character
sheet, dice/chat and authoritative gameplay commands. Public search, thumbnails,
recommendations, audio/video и admin analytics могут деградировать отдельно.

Availability измеряется synthetic user journeys, а не только pod uptime.
Planned maintenance учитывается в SLA только если это явно указано в договоре.

## 3. Надёжность и деградация

- Client сохраняет last acknowledged room sequence и idempotent pending commands.
- При gap клиент запрашивает delta; если retention пропущен — compact snapshot.
- Потеря Search не блокирует игру: fallback к recent/pinned compendium data.
- Потеря media derivatives показывает placeholder, source не удаляется.
- Потеря recommendations не блокирует builder; показывается фильтрованный список.
- Потеря NATS задерживает cross-context projections, но не должна повреждать
  committed aggregate; outbox lag виден и алертится.
- Потеря Redis disconnect-ит часть ephemeral sessions, но clients reconnect и
  durable state восстанавливается; Redis не хранит единственную копию HP/scene.
- Circuit breakers только там, где retry безопасен; exponential backoff + jitter;
  retry budget, deadline propagation и bulkheads.
- Graceful shutdown перестаёт принимать connections, передаёт room leases,
  drains commands и checkpoints sequence.

## 4. Масштабируемость

- Stateless HTTP nodes scale by CPU/RPS/latency.
- Realtime nodes scale by active connections, messages/sec, bytes/sec; sticky
  session допустим как оптимизация, но reconnect работает на другой node.
- Room/campaign — routing key; один sequencer lease на room в момент времени.
- Hot campaign имеет отдельный per-room rate/fan-out budget и не блокирует shard.
- Scene assets идут через CDN/object store, не через application nodes.
- Read projections partition по tenant/campaign и hot keys.
- PostgreSQL connection budget защищён PgBouncer; bounded pools per service.
- Background workers scale by queue lag and job class; media jobs изолированы.

## 5. Security baseline

### Identity/session

- OIDC/OAuth 2.1 Authorization Code + PKCE; short-lived access tokens;
- refresh token rotation/reuse detection, secure httpOnly SameSite cookies для
  browser BFF pattern; не хранить bearer tokens в `localStorage`;
- passkeys/TOTP, breached-password check, login throttling и anomaly alerts;
- CSRF protection для cookie-auth commands; strict CORS/CSP;
- key rotation и JWKS overlap; clock skew bounded.

### Authorization and isolation

- deny by default; policy проверяется в каждом owning service;
- `tenantId/campaignId` обязателен в repository filters и cache keys;
- authorization projection carries revision; critical commands optionally query
  Campaign on stale revision, но не создают постоянный synchronous mesh;
- join ticket одноразовый/short-lived и связан с campaign, user/guest, role,
  connection nonce и policy revision;
- batch endpoint проверяет каждый target, не только parent collection;
- support impersonation только just-in-time, approved, time-limited и audited.

### Untrusted content

- direct-to-object upload через presigned URL, quota reserved before upload;
- allowlist types; проверка magic bytes, dimensions, decoded pixel count, archive
  depth/ratio; filename не используется как object key;
- scan + isolated decode; SVG/HTML по умолчанию не принимаются как scene images;
- markdown sanitized, external embeds proxied/allowlisted;
- formulas parsed into bounded AST: max length/depth/dice/count/evaluation cost;
- ruleset/package JSON Schema validation, dependency and cycle limits;
- CSP без `unsafe-eval`; third-party plugin permissions explicit.

### Infrastructure

- TLS 1.2+, HSTS, encryption at rest, KMS/secret manager;
- private DB/cache/bus networks, mTLS/service identity where platform supports;
- least-privilege DB/bucket/NATS credentials per service;
- dependency/image/SBOM scan, signed deployments, protected environments;
- immutable audit storage для privileged and security events;
- DDoS/WAF/rate limits, separate limits per IP, account, campaign and command.

### Threat-model hotspots

- horizontal privilege escalation across campaigns/character ACL;
- replay/idempotency abuse that duplicates damage or resources;
- formula/resource exhaustion and regex/JSON bombs;
- zip/image decompression bombs and malicious metadata;
- WebSocket connection exhaustion and oversized frames;
- XSS through chat, journals, names, compendium HTML and SVG;
- dice result forgery/client substitution;
- SSRF in remote import/thumbnail URL;
- public homebrew copyright abuse and moderation bypass;
- event poisoning/upcaster vulnerability.

## 6. Privacy and compliance

- data inventory/classification: public, account PII, campaign private, secret;
- minimum required PII; age/parental consent decision before launch;
- data residency is a product/deployment decision, not marketing assumption;
- configurable chat/media retention and clear deletion semantics;
- self-service export and account deletion workflow;
- audit access to private campaign data; no content in metrics labels;
- logs redact tokens, invite codes, email, chat text and uploaded URLs;
- backups inherit retention/erasure policy with documented delayed deletion;
- DPA/subprocessor list, incident notification workflow, privacy policy;
- for Russian users/hosting, legal review of 152-FZ/data localization is mandatory;
  architecture supports region affinity but does not itself establish compliance.

## 7. Accessibility and internationalization

- target WCAG 2.2 AA for application UI;
- full keyboard operation for non-spatial tasks; canvas actions have list/property
  alternatives and shortcuts;
- visible focus, screen-reader announcements for turn/damage/status/chat;
- color is not the only status signal; high contrast and reduced motion;
- dice animation skippable; 3D dice never hides numeric result;
- localization keys, ICU pluralization, locale date/number/unit formatting;
- content locale separated from UI locale, fallback visible to user;
- RTL not required for first release, but layout primitives should not hardcode
  left/right where start/end works.

## 8. Operability

- structured logs with trace/correlation/tenant hash, no high-cardinality raw IDs
  in metric labels;
- golden signals per service: traffic, latency, errors, saturation;
- domain metrics: command conflicts, rejected actions, projection lag, reconnect,
  room fan-out lag, outbox/inbox age, formula evaluation budget, media quarantine;
- distributed trace sampling: errors/slow traces 100%, normal adaptive sample;
- runbooks link from alert; every paging alert has user impact and owner;
- deploy markers on dashboards; canary + automated rollback;
- health endpoints distinguish liveness/readiness/startup; readiness checks local
  required dependencies without cascading entire graph;
- status page components map to user journeys.

## 9. RPO/RTO

| Tier | RPO | RTO | Notes |
|---|---:|---:|---|
| Local/dev | none promised | best effort | disposable data |
| Alpha/pilot | ≤15 мин | ≤4 ч | daily restore verification recommended |
| Production core | ≤5 мин | ≤60 мин | PITR, multi-AZ, practiced runbook |
| Mature critical gameplay | ≤1 мин | ≤30 мин | only after business justification |

In-flight ephemeral cursor/drag deltas may be lost on failure; committed actions,
rolls, HP and final token positions follow RPO. Client must distinguish pending
from committed state.

## 10. Error budgets

For 99.9% monthly SLO budget is approximately 43.8 minutes/30-day month. Policy:

- burn >2% per hour for 1 hour: investigate;
- fast burn projected to exhaust budget in <2 days: page and pause risky rollout;
- >50% monthly budget spent: only reliability/security/critical correctness work
  ships to affected journey until trend recovers;
- SLA credits, if introduced, are calculated separately by legal/billing rules.
