# Шаг 03. Identity & Access: регистрация, вход и browser session

Статус: `Completed` — 2026-08-15; M03-01…M03-13 приняты, открытых P0/P1 нет.
Зависимость: шаг 02  
Результат: пользователь безопасно создаёт аккаунт, подтверждает email, входит,
обновляет browser session, видит и отзывает свои сессии, восстанавливает пароль.

## Затрагиваемые сервисы

- **Identity & Access** — владелец account/credentials/login sessions.
- **Edge Gateway/BFF** — OIDC/browser cookie boundary, CSRF, rate limits.
- **Search & Projections** — только public profile fixture позднее; сейчас consumer
  lifecycle events smoke.
- **Mailpit/email adapter** — infrastructure dependency, не bounded context.

## Разрабатываемые возможности

- registration + email verification;
- password login/logout и anti-enumeration;
- OIDC Authorization Code + PKCE через OpenIddict;
- secure httpOnly cookie/BFF session, access token не в `localStorage`;
- refresh token rotation/reuse detection;
- password reset and security-stamp rotation;
- список login sessions, revoke one/revoke others;
- profile: display name, locale, timezone;
- account lock/rate limiting/security audit;
- deactivation workflow skeleton без финального удаления связанных данных.

## Конкретный план реализации

1. Описать OpenAPI product endpoints, OIDC discovery/clients/scopes и threat model.
2. Реализовать `UserAccount` lifecycle и security events; credentials хранить через
   поддерживаемые Identity primitives, не в domain event payload.
3. Настроить exact redirect URIs, PKCE, signing/encryption keys и rotation policy.
4. Реализовать verification/reset challenges с hash, TTL, one-time consumption.
5. Добавить email outbox/worker и local Mailpit template preview.
6. Реализовать BFF login/callback/logout и secure cookie (`HttpOnly`, `Secure`,
   `SameSite` по flow), CSRF token для state-changing API.
7. Добавить refresh family, rotation, concurrent reuse detection и session revoke.
8. Реализовать `/me`, profile optimistic concurrency и session management UI.
9. Добавить generic auth error UX без раскрытия существования email.
10. Настроить audit/metrics/alerts: failures, locks, reuse, challenge delay.
11. Написать OIDC/component/E2E tests в двух browser contexts.

## Definition of Ready

- [x] решено, self-hosted OpenIddict или managed IdP; public contract одинаков;
- [x] email provider/local Mailpit и sender domain strategy определены;
- [x] password/session/challenge/lockout policies утверждены;
- [x] cookie/CSRF/CORS/redirect URI diagram прошёл threat review;
- [x] consent/privacy text versioned хотя бы fixture-документом;
- [x] account deletion scope явно отложен и не обещан UI;
- [x] test users/domains не могут отправить письмо реальному адресату из CI.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M03-01 | Зарегистрировать новый email и открыть Mailpit | Generic success; одно письмо; token не виден в логах |
| M03-02 | Повторить регистрацию существующего email | Внешний ответ не позволяет определить наличие account |
| M03-03 | Открыть verification link дважды и после TTL | Первый активирует account; повторы безопасно отклонены |
| M03-04 | Войти с valid/invalid password и невалидным redirect URI | Valid flow успешен; invalid generic/rate-limited; redirect rejected |
| M03-05 | Проверить browser storage/devtools | Refresh/access token отсутствует в localStorage/sessionStorage; cookie защищена |
| M03-06 | Выполнить state-changing command без/с неверным CSRF | `403 csrf_failed`, состояние не изменено |
| M03-07 | Обновить session в двух конкурентных вкладках | Rotation корректна; benign race обрабатывается, reuse theft revokes family |
| M03-08 | Открыть список sessions и отозвать одну | Только выбранная session теряет доступ, audit создан |
| M03-09 | `revoke others`, затем использовать старые cookies | Текущая остаётся, остальные rejected в пределах freshness SLO |
| M03-10 | Запросить/завершить password reset, повторить token | Password изменён один раз; старые sessions revoked; token replay rejected |
| M03-11 | Изменить display name/locale двумя stale tabs | Первый save успешен, второй получает conflict и current version |
| M03-12 | Провести enumeration/rate-limit smoke по login/reset | Одинаковые публичные ответы/timing class; `Retry-After`; оператор видит alert |
| M03-13 | Logout и back/refresh protected route | Cookie invalidated, приватные данные не берутся из stale browser cache |

## Definition of Done

- [x] OIDC/BFF/password/email/session flows работают end-to-end;
- [x] tokens/challenges hashed/rotated, signing keys не в repo;
- [x] anti-enumeration, CSRF, redirect allowlist и rate limits tested;
- [x] User lifecycle integration events минимальны и не содержат PII;
- [x] UI имеет accessible errors/loading и session management;
- [x] audit/metrics/redaction и runbook compromised session готовы;
- [x] unit/integration/OIDC/E2E/security tests зелёные;
- [x] M03-01…M03-13 пройдены, P0/P1 defects отсутствуют.

## Критический check перед завершением

- Может ли злоумышленник определить существующий email по ответу/timing?
- Можно ли украсть bearer token через XSS/localStorage?
- Что происходит при refresh reuse и смене signing key во время rolling deploy?
- Revocation реально применяется сервисами или только исчезает из UI?
- Нет ли email/IP/user-agent в domain bus/metric labels?
- Защищены ли callback/returnUrl от open redirect?

`NO-GO`: token в browser storage/log, replay reset token, CSRF bypass, open redirect,
session не отзывается, enumeration очевидна, credential material попал в event.

Evidence: OIDC/security test report, cookie/storage screenshot, reuse trace, key
rotation drill, Mailpit/redaction check.

## Вне scope

Passkeys, TOTP и external social providers переходят в шаг 16; avatar upload — шаг
09/16; platform admin UI и окончательное distributed account erasure — шаг 16.
