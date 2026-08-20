# Identity & Access Service

## Bounded context

Владеет идентичностью пользователя, credentials, authentication factors,
platform login sessions, external identity links, consent и account lifecycle.
Не владеет ролями кампании, character ACL или join room permissions — это Campaign/
owning contexts.

Реализация шага 03: ASP.NET Core `PasswordHasher`, OpenIddict/OIDC provider и EF Core
в .NET service. Self-hosted решение и BFF boundary зафиксированы ADR 0003; managed
IdP остаётся возможной заменой за теми же Edge contracts.

## Aggregates, entities, value objects

### `UserAccount` aggregate

- `UserId`, status (`Pending`, `Active`, `Locked`, `DeactivationPending`, `Deleted`);
- verified email references, display profile reference;
- password credential metadata (hash хранит Identity framework);
- external identities `(issuer, subject)`;
- MFA factors/passkeys metadata;
- consent versions and locale/timezone;
- security stamp/account version.

Invariants:

- normalized verified email unique в рамках выбранной policy;
- external `(issuer,subject)` связан максимум с одним account;
- последний recovery/auth method нельзя удалить без подтверждённой замены;
- deleted subject не переиспользуется; PII erasure separated from audit identity;
- privileged role change требует step-up + audited operator authorization.

### `LoginSession` aggregate/record

- session id, user id, refresh family, created/last used/expires;
- device label, token hashes, rotation/reuse state, revoked reason;
- IP/user-agent доступны только security store with short retention.

### Value objects

`NormalizedEmail`, `PasswordPolicyVersion`, `ConsentReceipt`, `PasskeyCredentialId`,
`ExternalSubject`, `SessionFamily`, `RecoveryChallenge`.

## Domain events

- `UserRegistered`, `EmailVerificationRequested`, `EmailVerified`;
- `PasswordChanged`, `AuthenticationFailed`, `AccountLocked/Unlocked`;
- `PasskeyRegistered/Removed`, `TotpEnabled/Disabled`;
- `ExternalIdentityLinked/Unlinked`;
- `LoginSessionIssued/Rotated/Revoked`, `RefreshTokenReuseDetected`;
- `ConsentGranted/Withdrawn`;
- `AccountDeactivationRequested/Cancelled`, `UserDeactivated`, `UserPiiErased`.

## Integration events

- `UserActivated.v1 { userId, displayNameRevision }`;
- `UserProfileChanged.v1 { userId, publicProfileRevision }`;
- `UserDeactivated.v1 { userId, effectiveAt }`;
- `UserSecurityStampChanged.v1 { userId, stampRevision }` для cache/session revoke.

Не публиковать email, factor details, IP или credential data.

## Связи

- Campaign consumes user lifecycle и хранит local member display projection.
- Edge performs OIDC flow/session binding and caches JWKS/security stamp briefly.
- Media stores avatar asset by user-owned reference, но Identity задаёт current ref.
- Email provider вызывается через async notification port, link содержит one-time
  bounded token; provider failure не теряет challenge state.

## Public OIDC/auth API

Стандартные OpenID Connect endpoints следуют discovery metadata:

| Method/path | Параметры | Назначение |
|---|---|---|
| `GET /.well-known/openid-configuration` | none | discovery |
| `GET /connect/authorize` | OIDC `client_id`, `redirect_uri`, `scope`, `state`, PKCE | authorization code flow |
| `POST /connect/token` | code/refresh grant, PKCE verifier | token exchange/rotation; BFF preferred |
| `POST /connect/revocation` | token/client auth | revoke |
| `GET /connect/userinfo` | access token | allowed standard claims |

Product endpoints:

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/auth/registrations` | `email`, `password`, `locale`, consent versions | создать pending account; always generic anti-enumeration response |
| `POST /api/v1/auth/email-verifications` | `challengeToken` | подтвердить email |
| `POST /api/v1/auth/password-resets` | `email` | generic response, rate limited |
| `POST /api/v1/auth/password-resets/complete` | token, new password | rotate security stamp/revoke sessions |
| `GET /api/v1/auth/oidc/start` (Edge BFF) | HttpOnly session cookie, safe relative `returnUrl` | создать protected state/nonce/correlation и начать Authorization Code + PKCE |
| `GET /signin-oidc` (Edge BFF callback) | authorization `code`, protected `state` | одноразовый code exchange, issuer/audience/nonce и BFF subject binding; redirect на web |
| `GET /api/v1/me` (Edge BFF) | HttpOnly session cookie | private profile summary |
| `PUT /api/v1/me` (Edge BFF) | `displayName`, `locale`, `timeZone`, `version`; CSRF | optimistic profile update |
| `GET /api/v1/me/sessions` (Edge BFF) | cookie | список sessions without token values |
| `DELETE /api/v1/me/sessions/{sessionId}` (Edge BFF) | cookie + CSRF | revoke one |
| `DELETE /api/v1/me/sessions` (Edge BFF) | cookie + CSRF | revoke all except current |
| `POST /api/v1/me/passkeys/options` | ceremony type | WebAuthn options |
| `POST /api/v1/me/passkeys` | attestation/response, label | register passkey |
| `DELETE /api/v1/me/passkeys/{passkeyId}` | step-up; `If-Match` | remove factor |
| `POST /api/v1/me/totp:begin` | step-up | create enrollment challenge |
| `POST /api/v1/me/totp:confirm` | challenge id + code | enable TOTP |
| `POST /api/v1/me/export` | scope | async data export operation |
| `POST /api/v1/me/deactivation` | step-up, confirmation | cooling-off workflow |

Admin endpoints находятся на отдельном audience/host, не документируются как
обычные public routes и всегда требуют reason + audited authorization.

## Storage and projections

- PostgreSQL encrypted; credential tables separate from public profile projection;
- token values never persisted plaintext, only strong hashes where needed;
- security audit append-only retention; account PII crypto-shreddable;
- JWKS keys in KMS/HSM-backed secret system; overlapping rotation.

## Key tests/SLI

- OIDC conformance, PKCE/redirect URI exact match, refresh reuse race;
- account/email enumeration, password reset replay, MFA recovery, CSRF;
- external identity collision/link hijack;
- deactivation propagation and stale token rejection;
- SLI: auth success excluding user error, token p95, suspicious failure rate,
  refresh reuse, email challenge latency; core availability target 99.9%.
