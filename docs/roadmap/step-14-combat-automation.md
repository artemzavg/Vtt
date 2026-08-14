# Шаг 14. Combat automation: attacks, saves, damage, resources и effects

Статус: `Planned`  
Зависимости: шаги 12 и 13  
Результат: действие из character/NPC profile проходит authoritative pipeline от
target/range и roll до damage/status/resource/ammo; GM видит explanation и может
компенсировать результат без переписывания истории.

## Затрагиваемые сервисы

- **Gameplay & Encounter** — action resolution and runtime mutations.
- **Chat & Dice** — immutable attack/save/damage rolls and action cards.
- **Ruleset** — deterministic action/effect/rest semantics.
- **Character** — static action/profile/equipment source.
- **Compendium** — exact spell/item/status mechanics.
- **Scene** — target positions, range, LOS/cover/area projection.
- **Session & Realtime** — prompts/results/pending/corrections.
- **Campaign/Edge/React** — automation policy, action UI and audit.

## Разрабатываемые возможности

- action preview: availability, target, range/LOS/cover, cost and formula;
- attack hit/miss/critical with advantage/disadvantage/situational query;
- spell/feature saving throws, auto or prompt, success/failure/half/no effect;
- typed damage/healing components, resistance/immunity/vulnerability/temp HP;
- resource/spell slot/ammo/charge/consumable spending exactly once;
- status/effect apply/duration/expiration and basic concentration;
- short/long rest recovery;
- automation levels manual/assist/automatic;
- multi-target/AoE process with per-target idempotency/partial recovery;
- action cards and explanation/provenance;
- cancel before commit and audited compensation after commit;
- critical correctness reconciliation.

## Конкретный план реализации

1. Зафиксировать ActionIntent/Resolution/Prompt/Outcome contracts and state machine.
2. Materialize immutable profile/rules/content/scene combat projections before use.
3. Реализовать preview context hash and final stale-version revalidation.
4. Реализовать Dice request ids and deterministic outcome engine.
5. Single-target transaction applies outcome/cost with idempotency.
6. Multi-target process manager stores per-target state/retry/timeout/compensation.
7. Реализовать save prompts/auto consent and situational modifier selection.
8. Реализовать damage pipeline and effect/status/concentration subset.
9. Реализовать rest/recovery and conflict with active encounter policy.
10. Chat action cards use opaque resolution commands, client amounts untrusted.
11. React: target selection/template, formula preview, pending prompts, apply/
    compensate confirmation, combat log.
12. Golden corpus + mutation/property/load/retry/chaos tests.

## Definition of Ready

- [ ] P0 action types/classes/spells/effects scope fixed;
- [ ] exact order attack/save/damage/resistance/temp HP/critical documented;
- [ ] scene range/LOS/cover projection freshness policy agreed;
- [ ] automation inheritance and auto-save consent rules approved;
- [ ] multi-target partial failure/timeout/compensation state machine reviewed;
- [ ] reaction/concentration subset and intentionally deferred cases clear;
- [ ] golden combat scenarios include adverse/concurrent/retry cases.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M14-01 | Preview melee/ranged/spell action with valid/invalid target/range | Formula/cost/LOS warnings correct; invalid commit rejected authoritative |
| M14-02 | Resolve normal/advantage/disadvantage hit/miss/critical | Dice/results/modifiers/outcome match golden cases, roll immutable |
| M14-03 | Toggle allowed situational modifier before roll | Preview/explanation changes; unauthorized/client-injected modifier rejected |
| M14-04 | Cast save spell in auto and prompt modes | Correct DC/target rolls/success-half-none; prompt timeout policy explicit |
| M14-05 | Apply mixed damage types to resistance/immunity/vulnerability/temp HP | Ordered breakdown and final HP match golden expected values |
| M14-06 | Spend spell slot/ammo/charge and retry/reconnect same command | Cost applied once; failed/cancelled-before-commit action policy correct |
| M14-07 | Execute AoE on multiple targets, fail one target processing temporarily | Applied targets dedupe; missing target retries; resolution shows partial/pending then converges |
| M14-08 | Apply status/concentration, damage caster, advance duration | Status/source/check/expiration follow selected ruleset subset |
| M14-09 | Short/long rest with resources/status restrictions | Recovery exact, removed/retained effects explained, duplicate rest no double gain |
| M14-10 | Change token position/profile after preview before commit | Stale context rejected/re-previewed, old assumptions not applied |
| M14-11 | GM compensate committed damage/resource/status | New inverse events restore intended state; original roll/action remains audit-visible |
| M14-12 | Player attempts forged action card/apply amount/hidden target | Opaque server reference required; unauthorized input rejected/no leak |
| M14-13 | Kill service after roll and at each commit window | Retry returns same roll/resolution and exactly-once logical mutations |
| M14-14 | Run full fight: initiative→weapon→spell save→status→rest | All clients/sheet/token/tracker/chat converge without manual derived edits |

## Definition of Done

- [ ] selected attack/save/damage/effect/rest corpus passes server/browser display;
- [ ] all resource/ammo/HP mutations idempotent under crash/retry;
- [ ] range/LOS/stale projection safe behavior implemented;
- [ ] manual/assist/automatic policy and prompts work;
- [ ] multi-target process recovers/compensates deterministically;
- [ ] combat log/action cards/provenance explain all modifiers/outcomes;
- [ ] replay/reconciliation finds zero duplicate/lost transitions;
- [ ] mutation score critical core meets target;
- [ ] M14-01…M14-14 пройдены без P0/P1 defects.

## Критический check перед завершением

- Может ли client подменить roll total, modifiers, targets, damage или resource cost?
- Есть ли crash window между roll, cost и outcome с двойным/потерянным эффектом?
- Что видят другие клиенты во время partial multi-target resolution?
- Может ли stale scene/profile/runtime применить удар по неверному состоянию?
- Компенсация создаёт события или переписывает history?
- Покрывает ли автоматика только доказанный subset, а не молча угадывает unknown rule?

`NO-GO`: lost/duplicate HP/resource, client-authoritative outcome, hidden target
leak, nondeterministic replay, history rewrite, silent fallback for unsupported rule.

Evidence: golden/mutation report, crash-window matrix, multi-target recovery trace,
full-fight E2E video/report, replay/reconciliation checksum.

## Вне scope

Advanced reactions/interrupt stack, auras, legendary/lair/mythic actions,
pathfinding/movement budget and every published spell/item. Unsupported mechanics
явно маркируются manual/assist и попадают в шаг 17.
