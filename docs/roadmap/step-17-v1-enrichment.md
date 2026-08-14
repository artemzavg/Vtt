# Шаг 17. Полноценная V1: подготовка кампаний, контент и расширенная игра

Статус: `Planned`  
Зависимость: шаг 16  
Результат: платформа закрывает регулярную подготовку и проведение кампаний, а не
только бой: journals/handouts/audio, расширенная automation, homebrew publishing,
переносимость данных, расписание и управляемые тарифы.

## Затрагиваемые сервисы

- **Campaign** — scheduling/templates/settings/quotas references.
- **Compendium/Ruleset** — homebrew authoring/publish/migrations/creator contracts.
- **Scene/Media** — animated maps, playlists, ambient sounds/emitters.
- **Chat & Dice** — roll tables, decks, richer macros/action cards.
- **Gameplay** — reactions/auras/advanced conditions/actions.
- **Character** — export/print/migration and richer recommendations.
- **Search** — journals/content discovery with ACL.
- **Identity/Edge/React** — billing/quota UX, localization and creator tools.
- Новый **Journal** bounded context допускается только после ADR; до этого нельзя
  молча присвоить long-form world data Campaign или Compendium.

## Разрабатываемые возможности

V1 выполняется подрелизами, каждый проходит общие gates:

1. **Campaign preparation:** journals/wiki, handouts, folders, linked map notes,
   granular audience and campaign templates.
2. **Media immersion:** animated maps/tiles, playlists, soundboard, ambient emitters.
3. **Gameplay depth:** reactions, auras, concentration coverage, advanced duration,
   legendary/lair actions in selected ruleset scope.
4. **Random tools:** roll tables, decks/piles/hands and macro hotbar improvements.
5. **Creator content:** private→unlisted→public homebrew publishing, moderation,
   immutable versions, migration preview and creator SDK/contracts.
6. **Portability:** character/campaign/homebrew export, printable accessible sheet,
   controlled import with validation/quarantine.
7. **Organization:** session calendar/RSVP/reminders, RU/EN content variants.
8. **Business:** subscription/quota/billing integration only after separate legal/
   finance ADR; gameplay remains usable when billing provider degraded.

## Конкретный план реализации

1. Product metrics/public feedback выбирают порядок подрелизов; не начинать все
   восемь параллельно.
2. Для Journal провести context-mapping/ownership ADR and privacy/search design.
3. Для audio/video assets определить codecs/quotas/CDN/rights and autoplay policy.
4. Расширять rules/actions только golden scenarios + manual fallback.
5. Public content получает author rights declaration, moderation/report/takedown.
6. Import/export формат versioned, license manifest included, hostile input sandboxed.
7. Calendar/reminders use timezone/idempotent notification workflows.
8. Billing provider isolated via future Billing context/adapter; entitlement
   projection never silently deletes user data.
9. После каждого подрелиза повторить MVP regression/load/privacy/accessibility.

## Definition of Ready

- [ ] выбран конкретный V1 подрелиз и metrics justify priority;
- [ ] bounded context owner/ADR для новых данных утверждён;
- [ ] licensing/moderation/privacy/billing implications reviewed;
- [ ] UX/manual fallback и migration path designed;
- [ ] quotas/storage/egress/notification cost estimated;
- [ ] contract/feature flag/rollout/rollback prepared;
- [ ] MVP error budget не исчерпан.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M17-01 | Создать journal tree/handout с mixed audiences и linked map note | Только разрешённые users/search snippets; links survive rename/move |
| M17-02 | Одновременно редактировать note/handout | Defined conflict/collaboration behavior, no silent overwrite |
| M17-03 | Проиграть playlist/ambient sound, reconnect/background browser | Sync/personal volume/autoplay policy correct; core session unaffected by audio failure |
| M17-04 | Запустить reaction/aura/advanced duration golden encounters | Selected rules correct; unsupported cases clearly manual, no guessed automation |
| M17-05 | Использовать roll table/deck/macro через retry/reconnect | Draw/hand/roll immutable/idempotent, secret audience protected |
| M17-06 | Опубликовать homebrew, update version, migrate one campaign | Moderation/license/version immutable; preview handles incompatible choices |
| M17-07 | Report/quarantine public homebrew in active campaign | Distribution stops safely; existing state/history and GM guidance explicit |
| M17-08 | Export/import character/campaign/homebrew round-trip | Owned data/provenance/license/ids preserved or mapped; no executable payload |
| M17-09 | Generate printable sheet and use screen reader/print widths | Essential data complete/readable/accessibility maintained |
| M17-10 | Schedule cross-timezone session, RSVP/reminder duplicate delivery | Times correct, reminders idempotent, revoked member not notified |
| M17-11 | Upgrade/downgrade subscription and simulate provider outage | Entitlements/quotas clear; data not deleted; active game core remains available |
| M17-12 | Run full MVP regression and target load after each subrelease | No SLO/security/correctness/accessibility regression beyond accepted budget |

## Definition of Done

- [ ] выбранные V1 subreleases прошли собственные DoR/DoD and flags rollout;
- [ ] journals/audio/gameplay/content/import/calendar/billing implemented only в
  утверждённом scope, ownership documented;
- [ ] public content moderation/license/takedown operational;
- [ ] export/import compatibility and hostile-input tests pass;
- [ ] all new user flows accessible/localized and observable;
- [ ] MVP regression, load/cost and DR tests remain within SLO/headroom;
- [ ] M17-01…M17-12 applicable cases accepted без P0/P1 defects.

## Критический check перед завершением

- Не превратился ли V1 в параллельную реализацию восьми огромных эпиков?
- Кто владеет journals, entitlements, schedules и deck state?
- Имеем ли права на public user/audio/imported content?
- Может ли billing/audio/moderation outage остановить core gameplay?
- Поддерживается ли старый character/campaign после package update?
- Не расширили ли automation без golden/manual fallback?

`NO-GO`: неизвестный owner, public content without rights/moderation, destructive
downgrade, core depends on billing/audio, unsafe import, implicit rules migration,
MVP SLO regression with exhausted error budget.

Evidence: subrelease ADRs, moderation/takedown drill, round-trip export report,
advanced rules golden report, cost/SLO regression dashboard, business/legal sign-off.

## Вне scope

Untrusted executable plugins, marketplace payouts/tax, second official ruleset,
LFG, built-in video and multi-region write. Это шаг 18 после отдельной валидации.
