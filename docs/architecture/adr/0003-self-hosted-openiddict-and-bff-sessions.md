# ADR 0003: Self-hosted OpenIddict and BFF browser sessions

Статус: Accepted, 2026-08-15.

## Контекст

Шаг 03 требует OIDC Authorization Code + PKCE, независимого Identity bounded context
и отсутствия bearer/refresh token в доступном JavaScript browser storage. На первом
запуске managed IdP создаёт лишнюю стоимость и vendor lock-in.

## Решение

- Identity является self-hosted OIDC provider на OpenIddict 7.6 и PostgreSQL.
- Разрешён только Authorization Code + PKCE; redirect URI регистрируется как точное
  абсолютное значение. Неизвестные clients/redirect URI отклоняет OpenIddict.
- Edge реализует BFF. Opaque session credential передаётся Identity только по
  service-to-service HTTP и хранится браузером в `HttpOnly; SameSite=Strict` cookie.
- Edge OIDC handler защищает `state` через persistent Data Protection key ring,
  выпускает HttpOnly nonce/correlation cookies, использует PKCE `S256`, обменивает
  code по backchannel и проверяет issuer, audience, nonce и совпадение `sub` с
  активной BFF session. BFF client не имеет refresh grant и не запрашивает
  `offline_access`; OIDC access/id tokens не сохраняются и не передаются браузеру.
- Browser authorization endpoint и issuer публичные; discovery/JWKS/token transport
  внутри compose идёт по Docker DNS. Edge нормализует discovery URLs на публичный
  authority, сохраняя backchannel rewrite только для server-to-server запросов.
- State-changing browser requests используют double-submit CSRF cookie/header, а
  Identity дополнительно сверяет CSRF hash с server-side session.
- Session и challenge secrets сохраняются только как SHA-256 hashes. Password hash
  создаёт ASP.NET Core `PasswordHasher`.
- Signing/encryption certificate обязателен вне Development. Development certificate
  нельзя использовать в staging/production.
- Managed IdP остаётся допустимой будущей заменой за текущими Edge contracts.

## Последствия

Команда владеет patching, key rotation и OIDC conformance. Взамен локальная разработка
не требует облачной учётной записи, credential/session data остаются в bounded context,
а фронтенд не получает bearer tokens.
