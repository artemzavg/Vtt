# Шаг 13. Gameplay Runtime, statuses, Encounter и initiative

Статус: `Planned`  
Зависимости: шаги 08, 10 и 11  
Результат: published character/NPC получает canonical runtime с current HP/
resources/ammo/statuses; GM создаёт encounter, роллит initiative и ведёт turns.

## Затрагиваемые сервисы

- **Gameplay & Encounter** — ActorRuntime/Encounter owner.
- **Character** — immutable static profile/rebase source.
- **Compendium/Ruleset** — exact NPC/status/resource/turn semantics.
- **Scene** — token↔actor mapping and spatial version projection.
- **Chat & Dice** — initiative rolls/system messages.
- **Session & Realtime** — commands/results/turn notifications.
- **Campaign/Edge/React** — policy, composite sheet and combat tracker.

## Разрабатываемые возможности

- ActorRuntime creation from Character profile or NPC snapshot;
- current/max/temp HP, current resources/ammo/charges;
- manual audited damage/heal/set/resource adjustments;
- status instances with source/duration/stacking baseline;
- profile rebase plan preserving/clamping current values explicitly;
- encounter create/add/remove/start/pause/complete;
- individual/group/manual initiative and ties;
- round/turn next/previous with audited GM override;
- status expiration hooks at turn boundaries baseline;
- composite sheet static + runtime and token bars;
- real-time combat tracker and runtime history.

## Конкретный план реализации

1. Реализовать ActorRuntime aggregate and profile materializer/inbox idempotency.
2. Определить source ActorId for Character/NPC and token mapping.
3. Реализовать manual typed adjustments with expected version/reason/visibility.
4. Реализовать RuntimeEffect/status instance and basic duration/stacking.
5. Реализовать profile rebase preview/apply policy and active encounter serialization.
6. Реализовать Encounter aggregate/combatants/initiative/turn cursor.
7. ChatDice initiative batch requests with stable ids/visibility.
8. Session broadcasts recipient-safe runtime/turn deltas.
9. Edge composes sheet; React adds HP/resources/status controls and tracker.
10. Reconciliation проверяет Character profile ref, token mapping and runtime version.

## Definition of Ready

- [ ] Character vs Gameplay ownership table окончательно утверждена;
- [ ] runtime initial current values and profile rebase policies specified;
- [ ] ActorId/token/Character/NPC mapping semantics готовы;
- [ ] status stacking/duration boundary subset selected;
- [ ] initiative/tie/group/manual rules and visibility examples ready;
- [ ] GM/player adjustment capabilities and audit visibility agreed;
- [ ] composite sheet conflict/pending UX wireframed.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M13-01 | Publish Character profile и дождаться materialization | Один ActorRuntime создан с exact profile version, initial HP/resources correct |
| M13-02 | Повторно доставить profile event | Второй runtime/ресурсы не создаются, source version idempotent |
| M13-03 | Добавить NPC snapshot token/actor | Independent runtime created; later compendium update не меняет его скрыто |
| M13-04 | Manual damage/heal/temp HP/resource/ammo adjustment | Clamp/order/reason/audit/visibility correct; composite sheet/token bar update |
| M13-05 | Concurrent adjustments с stale version | Один applies; второй conflict/retry preview, no lost damage |
| M13-06 | Apply same/stacking/refresh status and advance boundaries | Policy enforced, duration/source visible, expiration event once |
| M13-07 | Publish profile with changed max HP/resources and preview rebase | Preserve/clamp/reset consequences explicit; apply idempotent, no silent reset |
| M13-08 | Create encounter/add PC+NPC/start with initiative | Roll refs immutable, tie policy deterministic, tracker ordered |
| M13-09 | Next turn/round, pause/resume/complete | Cursor/round/status hooks correct; runtime retained after completion |
| M13-10 | Player attempts GM override/other actor adjustment | Rejected server-side; no optimistic persisted effect |
| M13-11 | Disconnect/reconnect during HP/turn change | Snapshot/delta converge, no duplicate adjustment/turn advance |
| M13-12 | Unlink/delete source Character/token during active encounter | Explicit blocked/orphan policy; runtime/encounter history not corrupted |
| M13-13 | Rebuild runtime/encounter projections from events | HP/resources/status/tracker checksum identical |

## Definition of Done

- [ ] ActorRuntime/materialization/rebase/manual adjustments implemented;
- [ ] statuses baseline and turn-boundary expiration deterministic;
- [ ] Encounter/initiative/round/turn lifecycle works;
- [ ] composite sheet/token bars/tracker update realtime;
- [ ] expected version/idempotency/reconnect correctness tested;
- [ ] Character/Gameplay ownership remains single and documented;
- [ ] event replay/rebuild checksum verified;
- [ ] M13-01…M13-13 пройдены без P0/P1 defects.

## Критический check перед завершением

- Где canonical current HP и не осталось ли второй writable копии в Character/Scene?
- Может ли duplicate profile event сбросить повреждённого персонажа к full HP?
- Сохраняется ли runtime после encounter и crash/replay?
- Что делает profile rebase с current ratios/removed resources в active combat?
- Может ли stale turn/adjustment примениться дважды после reconnect?
- Не раскрываются ли GM-only HP/status/NPC данные всем?

`NO-GO`: dual HP ownership, silent full-heal on rebase, duplicate damage/turn,
runtime loss, nondeterministic initiative/status expiration, unauthorized adjustment.

Evidence: ownership audit, rebase scenario matrix, concurrency/reconnect report,
encounter replay checksum, permission payload diff.

## Вне scope

Attack/save/damage formula automation, resource costs from actions, concentration,
reactions and automatic rests. Пока изменения typed/manual; автоматизация — шаг 14.
