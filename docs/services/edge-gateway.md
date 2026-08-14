# Edge Gateway / BFF

## Bounded responsibility

Технический edge context без собственной бизнес-модели. Владеет публичной
маршрутизацией, browser session boundary, response composition, cache policy,
request shaping, rate limiting и protocol negotiation. Не владеет campaign,
character, scene или gameplay state и не принимает доменные решения.

Deployables:

- `http-gateway`: ASP.NET Core/YARP-based reverse proxy + React BFF endpoints;
- `realtime-edge`: SignalR endpoints, connection admission и routing в Session.

Они могут масштабироваться отдельно, но документируются вместе как edge layer.

## Основные модели

Не domain aggregates, а operational records:

- `BrowserSession`: opaque server-side refresh/session reference, CSRF state;
- `IdempotencyRecord`: principal + route + key → response digest/status/TTL;
- `RateLimitLease`: account/IP/campaign/command budget, Redis-backed;
- `CompositionResult`: short-lived ETag/cache metadata;
- `ProtocolCapability`: client/build/protocol compatibility.

Canonical auth session остаётся в Identity; edge record — revocable cache/reference.

## Ответственность

- TLS termination behind CDN/WAF, HSTS/CSP/CORS/CSRF/security headers;
- validate size/content type/basic shape before routing;
- exchange OIDC callback, keep refresh token in secure server-side/cookie pattern;
- compose screen read models in parallel with deadlines and partial degradation;
- route commands to exactly one owning service;
- transform internal ProblemDetails only to stable public codes;
- redact/log metadata, propagate trace/correlation/deadline;
- WebSocket negotiation, authenticate connection, pass join ticket;
- static SPA/asset version compatibility and maintenance response.

Не разрешено:

- cross-service transaction or saga;
- correcting character/gameplay results;
- caching authorization-sensitive response without principal/policy revision key;
- retrying non-idempotent command without Idempotency-Key;
- streaming object-store media through application memory.

## Связи

Sync queries/commands ко всем public domain services. Identity для session exchange,
Campaign для critical authz fallback, Search для composite discovery, Session для
join tickets. Использует Redis для rate/idempotency caches; cache loss only reduces
performance and can cause safe client retries.

## События

Edge не публикует бизнес integration events. Operational/security events:

- `EdgeRateLimitExceeded` → security telemetry, sampled;
- `ClientProtocolRejected` → compatibility metric;
- `SuspiciousRequestBlocked` → security audit pipeline;
- `BrowserSessionBound/Released` → security audit, not domain bus.

## Public API

| Method/path | Параметры | Назначение |
|---|---|---|
| `GET /health/live` | none | process liveness; без dependency graph |
| `GET /health/ready` | none | готовность принимать трафик |
| `GET /api/v1/bootstrap` | optional `campaignId` | user summary, feature flags, protocol/config, recent campaigns |
| `GET /api/v1/campaigns/{campaignId}/workspace` | `include=scene,characters,encounter,chatSummary` | параллельная композиция начального workspace; per-part status |
| `GET /api/v1/characters/{characterId}/sheet` | `view=play|edit`, optional active encounter | composite static profile + gameplay runtime + ACL |
| `GET /api/v1/scenes/{sceneId}/bootstrap` | `quality`, `lastKnownVersion` | scene metadata, signed asset URLs, runtime/session cursor |
| `POST /api/v1/realtime/join-tickets` | `campaignId`, optional `sessionId`, `characterId`, capabilities | получить short-lived one-use ticket |
| `GET /realtime/v1` | SignalR negotiate, `protocolVersion` | WebSocket endpoint; JSON fallback only diagnostics, MessagePack preferred |
| `GET /api/v1/operations/{operationId}` | owner-scoped id | unified long-operation view |

Все остальные `/api/v1/*` маршруты проксируются к owning service и описаны там.

### Workspace partial response

`workspace` может вернуть core parts и `degradedParts[]` для search/recommendation/
thumbnail. Он не маскирует failure character authorization или active gameplay
state: core part failure превращает весь request в safe error.

## Rate limits initial

- auth endpoints: per IP + account exponential limits;
- general reads: 120/min/user burst 60, уточняется load tests;
- domain commands: 60/min/user, gameplay/realtime отдельные token buckets;
- upload reservations: quota + 10/min/user;
- WS frames: max 64 KiB command frame; connection/room message budget;
- dice/formulas: cost-weighted, не только request count.

Возврат `429` содержит `Retry-After` и stable limit code. GM actions не получают
безлимитность.

## SLI and tests

- route latency excluding/including upstream, composition timeout rate;
- auth/session exchange errors, rate-limit rejects, open WS connections;
- tests: header/CSP/CSRF, cache isolation, retry rules, partial response, forbidden
  resource disclosure, protocol current/previous, slow downstream cancellation;
- load: 1.3× target HTTP/WS, reconnect storm and large-room backpressure.
