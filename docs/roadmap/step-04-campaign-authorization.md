# Шаг 04. Campaign, membership, invitations и authorization

Статус: `Planned`  
Зависимость: шаг 03  
Результат: GM создаёт кампанию, приглашает игроков, назначает роли и управляет
доступом; каждый сервис получает воспроизводимую policy projection.

## Затрагиваемые сервисы

- **Campaign** — единственный владелец campaign lifecycle/membership/invite/policy.
- **Identity & Access** — lifecycle/display projection, authentication.
- **Edge Gateway/BFF** — route prefilter, dashboard composition, invite landing.
- **Search & Projections** — user dashboard/campaign summary.
- Все будущие tenant services — consume policy contract fixtures.

## Разрабатываемые возможности

- campaign draft/activate/archive/restore;
- роли Owner/CoGM/Player/Observer;
- relation/capability policy baseline;
- invitation token с TTL/max uses/revoke;
- member list, role change, suspend/remove/leave;
- ownership transfer с acceptance;
- campaign settings и exact ruleset placeholder reference;
- authorization check + signed/versioned policy snapshot;
- dashboard «мои кампании»;
- audit и немедленная invalidation будущих join tickets.

## Конкретный план реализации

1. Утвердить role→capability matrix и object relation vocabulary.
2. Реализовать Campaign/Membership/Invitation aggregates и event streams.
3. Хранить invite token только hash; raw token показывать один раз.
4. Реализовать atomic accept/max-uses и generic invalid invite behavior.
5. Добавить local effective-capability projection с `policyRevision`.
6. Реализовать internal batch authorization check и policy snapshot contract.
7. Edge проверяет coarse policy, Campaign/owner service остаётся финальной защитой.
8. Реализовать campaign/member/invite UI и role impact warning.
9. Search строит dashboard из events; outage не блокирует direct campaign access.
10. Добавить audit trail и test consumer, который invalidates stale policy cache.

## Definition of Ready

- [ ] capability matrix содержит каждое planned action, deny-by-default;
- [ ] различены platform role, campaign role и object relation;
- [ ] определена 403/404 disclosure policy;
- [ ] invite TTL/max-use/guest policy для этого этапа согласованы;
- [ ] owner transfer/last-owner/leave rules имеют examples;
- [ ] ruleset placeholder допускает только опубликованный fixture version;
- [ ] policy freshness SLO и critical fallback определены.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M04-01 | Создать campaign и открыть dashboard | Creator Owner; campaign Draft; projection появляется в пределах SLO |
| M04-02 | Попытаться активировать без valid ruleset fixture | Stable validation error, draft сохранён |
| M04-03 | Создать invite Player с TTL/maxUses=1, принять вторым user | Membership Active; повтор/второй user rejected atomic |
| M04-04 | Отозвать неиспользованный invite и открыть raw URL | Generic invalid/expired response, token не в logs/list API |
| M04-05 | Изменить Player→CoGM и проверить capability explanation | Новые права видны с policy revision/provenance |
| M04-06 | В двух вкладках одновременно изменить одну membership version | Один success, второй conflict; last write не молчаливый |
| M04-07 | Player пытается изменить campaign/role другого user | 403/404 по policy, audit/metrics без утечки деталей |
| M04-08 | Owner пытается удалить/понизить последнего Owner | Invariant rejects; campaign не остаётся без владельца |
| M04-09 | Провести ownership transfer без/с acceptance | До acceptance owner прежний; после — ровно один новый owner |
| M04-10 | Suspend member с открытой вкладкой | Следующая command rejected; policy cache/join ticket invalidated |
| M04-11 | Archive/restore campaign | Новые gameplay writes заблокированы в archive; authorized reads доступны |
| M04-12 | Остановить Search и открыть direct campaign URL | Core Campaign работает; dashboard показывает degraded, не ложный empty state |
| M04-13 | Подставить campaignId другого tenant в route/body/cursor | Никаких данных/count/cache leakage, command не проходит |

## Definition of Done

- [ ] campaign/member/invite/ownership aggregates и projections реализованы;
- [ ] capability matrix automated как policy tests;
- [ ] invite token hashing, atomic use, revoke/expiry verified;
- [ ] policy revision events доходят до test consumers p99 target;
- [ ] dashboard и accessible membership UI готовы;
- [ ] archive/restore и ownership transfer имеют audit;
- [ ] cross-tenant adversarial integration/E2E tests зелёные;
- [ ] M04-01…M04-13 пройдены без P0/P1 defects.

## Критический check перед завершением

- Есть ли действие, которое разрешено UI, но отсутствует в policy matrix?
- Можно ли использовать stale invite/policy после revoke?
- Не доверяет ли service клиентскому `campaignId`/role claim без проверки revision?
- Раскрывает ли dashboard/cache число/название чужих кампаний?
- Что происходит при duplicate/out-of-order membership events?
- Не пытается ли Campaign владеть character/scene-specific ACL?

`NO-GO`: cross-tenant read/write, invite replay, last owner removal, права действуют
после revoke дольше SLO без fail-closed, role logic дублируется только во frontend.

Evidence: permission matrix report, concurrent invite test, policy propagation trace,
tenant isolation test, archive/ownership audit.

## Вне scope

Guest without account, subscriptions/quotas, scheduled sessions, character/token
ACL и ruleset migration; они добавляются в шагах 07, 10, 16–17.
