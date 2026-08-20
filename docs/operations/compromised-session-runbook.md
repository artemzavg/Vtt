# Runbook: compromised Identity session

1. Найти PII-free audit records по `userId`/`sessionId`; не добавлять email/IP в metric labels.
2. Для одного устройства вызвать revoke session; при refresh reuse или неизвестном
   масштабе — revoke all sessions и увеличить security revision сменой пароля.
3. Проверить `refresh_reuse_detected`, `session_revoked`, `password_reset_completed` и
   доставку outbox. Credential values никогда не копировать в ticket/chat.
4. При компрометации signing key удалить key из выдачи только после overlap window,
   развернуть новый certificate, затем принудительно инвалидировать активные sessions.
5. Закрыть incident только после проверки, что старые cookies получают 401 и новые
   Authorization Code + PKCE flows подписываются новым ключом.

Alert policy для первого запуска: page on-call при любом
`vtt.identity.refresh.reuse > 0`; warning при доле `authentication.attempts{outcome="locked"}`
выше 5% за 10 минут или росте `challenge.failures` в 5 раз относительно часового
baseline. Labels ограничены `outcome`/`purpose` и не содержат user/email/IP/device.
