# Шаг 07. Character Builder: draft, характеристики, происхождение и класс

Статус: `Planned`  
Зависимости: шаги 05 и 06  
Результат: пользователь создаёт level-1 character через сохраняемый wizard,
выбирает характеристики несколькими способами, происхождение/background/class и
получает детерминированный профиль с объяснением каждого значения.

## Затрагиваемые сервисы

- **Character** — Character/BuildDraft/Profile owner.
- **Ruleset** — build graph/evaluation/provenance artifacts.
- **Compendium** — exact species/background/class/feature refs.
- **Campaign** — optional campaign link, ruleset/content/access validation.
- **Chat & Dice** — минимальный immutable roll kernel только для ability rolls;
  полный chat UX реализуется на шаге 11.
- **Edge/React** — builder UI, autosave, preview/diff/conflict UX.

## Разрабатываемые возможности

- character вне кампании или внутри compatible campaign;
- build draft/wizard, autosave/resume/back/forward;
- manual, fixed array, point buy, server random throws;
- class-based explainable ability recommendation;
- species/race/subrace/origin bonuses, включая свободные `+2/+1` при ruleset;
- racial/species features, background features, languages/proficiencies choices;
- class level 1, starting features/resources definitions;
- structured homebrew/manual feature baseline с audited override;
- dependency invalidation, warnings/errors, preview hash;
- complete/publish immutable static profile и provenance inspector.

## Конкретный план реализации

1. Реализовать Character aggregate, BuildDraft state и immutable Profile document.
2. Получать wizard nodes из compiled ruleset, `choicePath` opaque/stable.
3. Реализовать choice commands с expected version и dependency invalidations.
4. Добавить ability methods:
   - manual bounds/policy;
   - fixed array use-once assignment;
   - exact point-buy budget/cost;
   - ChatDice server roll, immutable roll id, visible reroll history.
5. Реализовать ruleset-defined recommendation с explanation, не auto-commit.
6. Подключить exact compendium refs and mechanics hashes.
7. Реализовать origin/background/class choice sets and granted features/modifiers.
8. Сделать preview/profile provenance tree и suppressed modifiers.
9. UI: stepper, incomplete/error states, autosave indicator, invalidation dialog,
   keyboard/mobile-width baseline and resume route.
10. Complete проверяет preview hash, accepted warnings и full validation atomically.
11. Property/golden/E2E tests для разных ability/origin/class combinations.

## Definition of Ready

- [ ] D&D package содержит необходимые level-1 definitions и golden characters;
- [ ] ability method rules/limits/costs/reroll policy согласованы;
- [ ] UI flow/wireframe показывает invalidated choices и provenance;
- [ ] warning vs blocking error vs GM override semantics определены;
- [ ] Character/Gameplay ownership текущих значений явно разделено;
- [ ] external character campaign-link policy утверждена;
- [ ] минимальный Dice API достаточен и не создаёт временный RNG в Character.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M07-01 | Создать character вне campaign, закрыть browser и продолжить | Draft сохраняется, owner-only access, текущий step/version восстановлен |
| M07-02 | Создать character в incompatible campaign | До записи выбора понятный compatibility error, чужой ruleset не смешан |
| M07-03 | Назначить fixed array drag/drop с duplicate/missing value | Duplicate/missing rejected; каждое значение используется ровно один раз |
| M07-04 | Исчерпать point-buy точно и превысить budget | Exact budget valid; excess/invalid bounds подсвечены с cost breakdown |
| M07-05 | Выполнить random throws и reroll | Каждый server roll имеет immutable id/history/visibility; старый не исчезает |
| M07-06 | Применить class recommendation | Показано объяснение; пользователь подтверждает; recommendation не скрывает choice |
| M07-07 | Выбрать species/origin +2/+1 и features | Derived fields/features обновлены, source/version видны в provenance |
| M07-08 | Выбрать background/language/proficiency с cardinality conflicts | Wizard не завершает шаг до valid set, доступные варианты ruleset-bound |
| M07-09 | Изменить class после dependent choices | Только зависимые nodes invalidated; dialog перечисляет потерянные choices |
| M07-10 | Открыть draft в двух tabs и редактировать | Stale tab получает conflict/current diff, не перетирает новое |
| M07-11 | Добавить manual homebrew feature без automation и structured override | Режимы различимы; structured invalid formula rejected; reason/audit обязательны |
| M07-12 | Complete со stale preview hash, затем с current | Stale rejected; current публикует immutable profile один раз |
| M07-13 | Открыть provenance для ability/save/movement | Base, active/suppressed modifiers, choice/source/ruleset version понятны человеку |
| M07-14 | Повторить complete с тем же idempotency key | Новый profile/event не создаётся, возвращён исходный result |

## Definition of Done

- [ ] Character/BuildDraft/Profile model реализована без gameplay counters;
- [ ] все четыре ability methods и recommendation работают;
- [ ] origin/background/class/features/proficiencies покрыты level-1 package;
- [ ] autosave/resume/invalidation/conflict/preview complete UX доступны;
- [ ] immutable server rolls и profile refs воспроизводимы;
- [ ] provenance отвечает «почему это значение?»;
- [ ] domain/golden/property/integration/E2E/accessibility tests зелёные;
- [ ] M07-01…M07-14 пройдены без P0/P1 defects.

## Критический check перед завершением

- Есть ли D&D поля в универсальном Character aggregate вместо ruleset paths?
- Может ли смена выбора оставить stale granted feature/modifier?
- Можно ли подменить client roll/result/preview hash?
- Воспроизводится ли profile после cache clear/rebuild на точной версии artifacts?
- Видит ли GM/другой игрок внешний owner-only draft без ACL?
- Понятно ли пользователю, что автоматическое, ручное и GM-overridden?

`NO-GO`: потерянный draft, client-authoritative roll, profile nondeterminism,
необъяснимые values, stale feature после смены класса, cross-user access.

Evidence: golden profile hashes, ability method property report, two-tab conflict
video/report, provenance screenshots, replay checksum.

## Вне scope

Level >1, multiclass, HP progression, spells, inventory/equipment, runtime HP и
combat actions. Они добавляются в шагах 08 и 13–14.
