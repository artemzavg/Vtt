# Шаг 15. Закрытая alpha: интеграция и hardening vertical slice

Статус: `Planned`  
Зависимости: шаги 01–14  
Результат: приглашённая группа проходит полный путь registration → campaign →
character → scene → session → encounter без помощи разработчика и без нарушения
correctness/security; 100 CCU выдерживаются с наблюдаемым запасом.

## Затрагиваемые сервисы

Все сервисы: Edge, Identity, Campaign, Ruleset, Compendium, Character, Media,
Scene, Session, Gameplay, Chat & Dice, Search & Projections, React client и вся
инфраструктура. Новых больших bounded contexts на этом шаге не создаётся.

## Разрабатываемые возможности

- единый onboarding/demo campaign flow;
- coherent loading/error/degraded/retry/conflict UX;
- feature flags и automation kill switches;
- cross-service operation/projection pending UX;
- audit/support diagnostics без приватного impersonation;
- backup/PITR/object restore and projection rebuild runbooks;
- staging/alpha deployment with TLS, WAF/basic rate limits and secret manager;
- metrics dashboards/alerts/status components;
- correctness reconciliation jobs;
- closed-alpha feedback/report flow;
- critical accessibility/responsive fixes;
- 100 CCU/15–20 rooms load, reconnect storm and soak hardening.

## Конкретный план реализации

1. Зафиксировать alpha scope/known unsupported mechanics; UI не обещает больше.
2. Создать seed/demo campaign только из approved open content.
3. Пройти все cross-service sagas and eliminate synchronous call chains/hard
   dependency where degraded operation is possible.
4. Добавить global error codes/support correlation id and user-safe recovery hints.
5. Собрать golden-path Playwright multi-user suite и nightly full matrix.
6. Настроить staging/alpha IaC, migrations expand-contract, canary/rollback.
7. Настроить SLO dashboards, alert ownership and short runbooks.
8. Выполнить security/threat review, dependency/container scan and focused DAST.
9. Выполнить backup restore in isolated environment, projection checksum compare.
10. Запустить 60-minute target+30% load, 8-hour soak and reconnect storm.
11. Провести минимум две реальные игровые сессии с наблюдением/feedback.
12. Исправить P0/P1 defects; P2 known limitations visible and owner/date assigned.

## Definition of Ready

- [ ] все DoD шагов 01–14 и critical `GO` приложены;
- [ ] alpha feature matrix/unsupported list/flags frozen на цикл;
- [ ] staging максимально близок topology alpha и имеет synthetic data only;
- [ ] приглашённые testers/consent/privacy/feedback channel готовы;
- [ ] SLO/load profile и defect severity policy утверждены;
- [ ] on-call owner и rollback authority назначены;
- [ ] license manifest/attribution reviewed для всей demo content.

## Подробный план ручного тестирования

### A. Полный пользовательский путь

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M15-01 | Новый GM регистрируется, создаёт/активирует campaign и invite | Flow без админской помощи, ошибки/почта/role корректны |
| M15-02 | 5 игроков принимают invite и создают разных level-1/N characters | Builder/profile/provenance/ACL correct, progress survives refresh |
| M15-03 | GM загружает карту, grid/tokens/walls/light/fog | Processing/scene/visibility работает, игроки не видят GM data |
| M15-04 | Все входят в session, двигают tokens, chat/roll | Presence/realtime/reconnect/history converge |
| M15-05 | Провести encounter с initiative, weapon, spell save, damage, status, rest | HP/resources/ammo/action cards/provenance correct без ручного derived edit |
| M15-06 | Завершить session, reload на следующий день | Scene positions, runtime, chat, character and encounter history сохранены |

### B. Degradation/recovery/security

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M15-07 | Остановить Search/Media worker/NATS consumer по очереди | Core session остаётся или честно degraded; outbox catches up, no corruption |
| M15-08 | Перезапустить realtime/gameplay nodes во время игры | Clients reconnect; committed action/move не теряется/не дублируется |
| M15-09 | Использовать stale tabs, duplicate clicks и network retries | Conflict/idempotency UX понятен, state converges |
| M15-10 | Выполнить cross-tenant/GM-hidden adversarial checklist | Никаких payload/cache/search/log leaks |
| M15-11 | Restore DB/object backups в isolated environment | RPO/RTO alpha target; projections rebuild; checksums match |
| M15-12 | Откатить canary version после migration-compatible deploy | Old binary работает с expanded schema, pending operations safe |

### C. Usability/performance

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M15-13 | Новый tester проходит onboarding без подсказок | Critical path completion; непонятные места recorded/fixed |
| M15-14 | Играть на reference desktop/tablet и throttled network | Sheet/canvas/chat usable, low-quality/reconnect states accessible |
| M15-15 | Выполнить 100 CCU load +30% and reconnect storm | Stage SLO/correctness/headroom criteria met |
| M15-16 | Выполнить 8-hour soak | Нет connection/memory/event/projection growth leaks |
| M15-17 | Проверить alert → runbook → diagnosis по injected failure | Alert отражает user impact, trace/correlation приводит к причине |

## Definition of Done

- [ ] full multi-user E2E и две реальные alpha sessions успешны;
- [ ] 100 CCU/15–20 rooms +30%, reconnect storm, 8-hour soak passed;
- [ ] zero known P0/P1 security/correctness/data-loss defects;
- [ ] backup/restore/projection rebuild/rollback drills meet alpha RPO/RTO;
- [ ] dashboards/alerts/runbooks/status components ready;
- [ ] feature flags/kill switches tested, unsupported mechanics explicit;
- [ ] license/privacy/security reviews signed off;
- [ ] M15-01…M15-17 evidence and acceptance `GO` recorded.

## Критический check перед завершением

- Может ли реальная группа играть без developer/admin intervention?
- Где система деградирует каскадно из-за sync dependencies?
- Есть ли correctness discrepancy между sheet, token, chat and runtime?
- Можно ли диагностировать incident без чтения private chat/content?
- Реально ли restore, а не просто наличие backup checkbox?
- Не выдаём ли alpha за SLA-ready production?

`NO-GO`: любой P0/P1, lost/duplicate gameplay state, hidden data leak, restore не
проверен, alert не ведёт к diagnosis, target load без headroom, legal uncertainty.

Evidence: alpha session reports, full E2E video/log, load/soak charts, DR/rollback
transcript, security/license sign-offs, residual-risk register.

## Вне scope

Публичная регистрация без allowlist, коммерческий SLA, 1 000 CCU, billing,
community publishing, guest join, external auth/MFA and full WCAG certification.
