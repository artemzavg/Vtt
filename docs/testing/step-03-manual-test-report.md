# Step 03 manual acceptance report

Дата прогона: 2026-08-15  
Окружение: Docker Engine 29.0.1, реальный PostgreSQL/NATS, Mailpit,
`docker compose --profile apps`.  
Точки входа: web `http://127.0.0.1:55173`, Edge `http://127.0.0.1:5100`,
Identity `http://127.0.0.1:5101`, Mailpit `http://127.0.0.1:58025`.

Итог: **GO**. M03-01…M03-13 пройдены, открытых P0/P1 нет. `ID-ACC-001`
закрыт повторным M03-04: полный Authorization Code + PKCE flow через Edge BFF
завершается возвратом на React `/account`.

## Результаты ручных сценариев

- [x] **M03-01** — регистрация вернула `202`; Mailpit получил ровно одно письмо;
  ссылка указывает на актуальный web port; raw email/challenge/session token в
  Identity logs не найден.
- [x] **M03-02** — повторная регистрация вернула такой же `202` и публичное тело;
  второе verification-письмо не создано.
- [x] **M03-03** — первая verification дала `204`, replay — `400`; отдельный
  challenge с принудительно истёкшим `ExpiresAt` отклонён с `400`.
- [x] **M03-04** — valid password login `200`, invalid password `401`, lockout после
  пяти ошибок подтверждён; незарегистрированный redirect URI отклонён `400`.
  Edge initiation создал защищённые `state`/`nonce`, correlation cookies и PKCE
  `S256`; Identity выдал code; Edge обменял code, проверил BFF subject binding и
  вернул `302` на `http://127.0.0.1:55173/account`, страница ответила `200`.
  Replay callback отклонён без повторного обмена, внешний `returnUrl`
  нормализован в `/account`, anonymous initiation получил `401`.
- [x] **M03-05** — login response не содержит token; `vtt.session` имеет
  `HttpOnly; SameSite=Strict`; E2E подтверждает пустые `localStorage` и
  `sessionStorage`.
- [x] **M03-06** — отсутствующий и неверный CSRF дают `403`, профиль не меняется;
  валидный double-submit запрос даёт `200`.
- [x] **M03-07** — refresh: rotation `200`, старый token в race window `409`, reuse
  после пяти секунд `401`; rotated token той же family также становится
  недействительным.
- [x] **M03-08** — точечный revoke вернул `204`: выбранная session получила `401`,
  текущая сохранила `200`; audit `session_revoked` создан.
- [x] **M03-09** — revoke others вернул `204`: прежняя session получила `401`,
  текущая сохранила `200`; audit `other_sessions_revoked` создан.
- [x] **M03-10** — существующий и отсутствующий email получили одинаковые `202` и
  тела (217/209 ms); reset `204`, replay `400`, старая session и старый password
  `401`, новый password `200`.
- [x] **M03-11** — browser profile save успешен; первый writer `200`, stale writer
  `409`, победившее значение и новая версия сохранены.
- [x] **M03-12** — burst дал ровно 20 ответов `401`, затем `429` с
  `Retry-After: 60`; после пяти неверных паролей правильный пароль также получил
  `401`; security audit содержит `authentication_failed` и
  `refresh_reuse_detected`.
- [x] **M03-13** — logout `204`, прежняя session после logout `401`; UI содержит
  logout и переводит неавторизованный `/account` на `/auth`, приватный профиль не
  хранится в browser storage.

## Найдено и исправлено во время приемки

1. Web proxy снимал `/api`, но Edge ожидал `/api/v1`: внешний API отвечал `404`.
2. Non-root Identity не мог писать Data Protection volume; readiness при этом был
   ложноположительным. Добавлены volume init/chown и функциональный
   `identity-data-protection` readiness check.
3. Runtime Alpine не содержал `libgssapi_krb5.so.2`; добавлен `krb5-libs`.
4. Email links использовали `localhost:5173`, а compose web — `55173`; отсутствовали
   UI routes verification/reset.
5. Missing profile fields приводили к `NullReferenceException`/`500` и могли
   частично изменить tracked aggregate. Валидация теперь атомарна и возвращает
   `400`; добавлен unit test.
6. Неизвестное JSON-поле в Edge приводило к `500`; теперь возвращается безопасный
   `400 edge.malformed_request`, добавлен integration test.
7. Account UI не имел profile form/logout и показывал revoked sessions; добавлены
   profile/session/logout UX и обработка `401`.
8. Generated client отправлял лишний `userId` в строгий `PUT /me`, из-за чего
   browser save получал `400`; payload приведён к контракту.
9. `429` не содержал `Retry-After`; Edge теперь отдаёт metadata fixed-window
   limiter.
10. Verification effect нарушал React lint и мог повторно использовать одноразовый
    token в Strict Mode; добавлен one-shot guard.
11. Edge не имел OIDC initiation/callback; добавлен Authorization Code handler с
    PKCE, protected state, nonce/correlation cookies, code exchange и ID token
    validation. BFF не запрашивает `offline_access`; токены не сохраняются в
    authentication ticket и не выдаются браузеру.
12. Discovery загружался по Docker DNS и первоначально отдавал browser redirect на
    `identity:8080`; Edge backchannel adapter теперь оставляет внутренний transport,
    но нормализует discovery URLs к публичному issuer.
13. Edge OIDC correlation key ring сделан persistent и добавлен в readiness.

## Автоматические evidence после исправлений

- `dotnet test Vtt.slnx -c Release --no-restore`: успешно; все unit,
  architecture, contract и integration projects зелёные, включая реальные
  PostgreSQL/NATS Testcontainers.
- Identity unit: 10/10; Identity integration: 2/2; Edge integration: 9/9;
  engineering fixture integration: 9/9.
- `pnpm format:check`, `lint`, contract/boundary validation, `typecheck`, build,
  bundle budget и high-confidence secret scan: успешно.
- Vitest: 4/4; Playwright: 2/2.
- Compose: postgres, nats, identity, edge, web и Mailpit healthy; Identity readiness
  отдельно подтверждает доступность dependencies и Data Protection key ring.
- Identity log redaction scan: 0 test-email, `challengeToken`, `sessionToken` и
  verification/reset URL token occurrences.
- Повторный live M03-04: `state`, `nonce`, `code_challenge` присутствуют,
  `code_challenge_method=S256`; callback/replay/invalid redirect/anonymous start
  соответствуют ожидаемым статусам. OpenIddict пишет значения token response как
  `[redacted]`; callback query и acceptance email в логах отсутствуют.

## Открытые замечания

- **Закрыт ID-ACC-001:** Edge OIDC initiation/callback и негативные callback tests
  реализованы; M03-04 повторно пройден.
- **P2 ID-ACC-002:** провести отдельный production-like drill: зашифрованное
  Data Protection key ring, persistent OpenIddict certificates и rolling key
  rotation. Development volume намеренно не является production evidence.
- **P2 ID-ACC-003:** minimal `apps` profile создаёт security audit и метрики, но
  фактическая доставка paging alert должна быть подтверждена с observability
  profile на закрытой альфе.
