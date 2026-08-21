# Identity & Access threat model

Дата review: 2026-08-15. Scope: registration, verification/reset challenges,
password login, OIDC provider, BFF cookies, profile и login sessions.

| Угроза | Контроль | Автоматическая проверка |
|---|---|---|
| Account enumeration | generic register/reset response; единый rate-limit bucket | application/API tests |
| Credential stuffing | password policy, fixed-window limit, 5 failures/15 minute lock | unit/integration tests |
| Token theft через XSS | opaque credential только в HttpOnly cookie; `no-store` | BFF integration test |
| CSRF | SameSite Strict + double-submit + server-side CSRF hash | BFF integration test |
| Reset/verify replay | hashed challenge, TTL, atomic one-time consumption | domain tests |
| Refresh replay | rotation, 5-second benign race, reuse revokes user sessions | domain tests |
| Open redirect/code interception | exact redirect URI; safe relative return URL; Authorization Code PKCE S256 | Edge OIDC integration + live M03-04 |
| Callback forgery/replay | protected state; HttpOnly nonce/correlation cookies; single-use correlation; subject bound to BFF session | tampered state/missing cookie/wrong nonce/replay tests |
| Stale profile overwrite | `If-Match`/expected aggregate version | domain/API tests |
| PII leak in event bus | integration events contain only user id and revisions | architecture review |
| DB/email compromise | hashes at rest; protected email-outbox payload | persistence tests/review |

Residual risks before public launch: distributed rate limiting must move to Redis,
production keys must be KMS-managed with overlap rotation, CSP and full OIDC conformance
suite must be enabled at the deployment edge.

`NO-GO`: session token in response/localStorage/logs, missing CSRF check, plaintext
challenge, wildcard redirect URI, development signing certificate outside Development.
