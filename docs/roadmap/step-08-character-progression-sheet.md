# Шаг 08. Character progression, inventory, spells, effects и sheet

Статус: `Planned`  
Зависимость: шаг 07  
Результат: персонаж создаётся сразу на уровне N или повышает уровень; получает
HP/proficiency/features/resources, starting equipment/spells/actions и удобный
static play sheet с provenance.

## Затрагиваемые сервисы

- **Character** — progression choices, inventory ownership/loadout, profile.
- **Ruleset** — level grants, HP/resource/effect/action evaluation.
- **Compendium** — items, spells, feats, classes/subclasses exact refs.
- **Chat & Dice** — level-up HP rolls.
- **Campaign** — content allowlist/character ACL baseline.
- **Edge/React** — level-up wizard, inventory/spells/recommendations/sheet.
- **Search & Projections** — character summaries within authorized campaign.

## Разрабатываемые возможности

- target level N с воспроизведением choices по уровням;
- proficiency bonus, class/subclass features/resources;
- HP: first-level max, fixed average/manual/server roll per ruleset;
- Constitution/derived HP recalculation with explanation;
- multiclass baseline and prerequisites;
- starting equipment choices или starting currency purchase workflow;
- inventory ownership/quantity/container baseline;
- equip/unequip, attunement, stacking/suppressed effects;
- spell known/prepared/granted limits, filter and explainable recommendations;
- derived actions/attacks from weapons/spells/features;
- responsive sheet: Summary/Actions/Spells/Inventory/Features/Audit;
- object ACL owner/editor/controller/viewer;
- level-up/respec draft с preview/diff, published profile immutable.

## Конкретный план реализации

1. Расширить progression graph уровневой последовательностью и subclass/multiclass.
2. Реализовать HP policies and immutable roll linkage; правила retroactive CON
   берутся из package, не global code.
3. Создать level-up/respec/migration draft from published profile.
4. Реализовать inventory ownership and starting gear/currency choices.
5. Реализовать equipment slots, attunement и modifier stacking/provenance.
6. Реализовать spell catalog filters/known/prepared/granted constraints.
7. Recommendation engine rules-based: role/preferences/gaps + explanation; failure
   не блокирует полный список.
8. Генерировать action definitions with hit/save/damage/range/cost metadata; не
   выполнять action до Gameplay.
9. Сделать screen-oriented static sheet projection + optimistic safe edits.
10. Добавить character ACL, campaign link/unlink and exact policy revisions.
11. Подготовить `CharacterProfilePublished` consumer contract для Gameplay.

## Definition of Ready

- [ ] package покрывает выбранные классы/уровни/HP/equipment/spell fixtures;
- [ ] target max level MVP и multiclass subset зафиксированы;
- [ ] static inventory ownership vs runtime ammo/charges разделены;
- [ ] effect stacking/attunement/recalculation examples готовы;
- [ ] spell recommendation criteria прозрачны и не AI-only;
- [ ] sheet wireframes desktop/tablet/mobile и ACL UX согласованы;
- [ ] rebase policy для будущего Gameplay описана contract fixture.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M08-01 | Создать выбранный class сразу level N | Wizard проходит choices каждого уровня; proficiency/features/resources верны |
| M08-02 | Сравнить fixed и rolled HP, сделать visible reroll | Формулы/roll ids/history/provenance верны, first level policy соблюдена |
| M08-03 | Изменить Constitution до publish | Max HP diff объясняет каждый уровень/правило, unrelated choices не сброшены |
| M08-04 | Выполнить valid/invalid multiclass | Prerequisites enforced; grants/slots/resources соответствуют golden fixture |
| M08-05 | Выбрать starting equipment, затем альтернативный starting money purchase | Взаимоисключение соблюдено, budget/quantity/source видимы |
| M08-06 | Equip/unequip/attune item с conflicting modifiers | Active/suppressed effects и derived AC/ability/action обновляются объяснимо |
| M08-07 | Превысить attunement/slot limit | Command rejected до profile publish, UI предлагает разрешение конфликта |
| M08-08 | Выбрать known/prepared/granted spells на/сверх limit | Allowed set exact; превышение rejected; granted source видим |
| M08-09 | Открыть recommendations, отключить recommendation service | Suggestions объяснимы; при деградации полный spell picker работает |
| M08-10 | Проверить weapon/spell/feature actions | Hit/save/damage/range/cost формулы видны, но кнопка execution ясно disabled до Gameplay |
| M08-11 | Level-up published character, cancel и затем complete | Cancel не меняет old profile; complete создаёт new immutable profile/diff |
| M08-12 | Дать viewer/controller/editor разным users | Viewer читает, controller play view, editor build changes; forbidden actions rejected server-side |
| M08-13 | Открыть sheet на desktop/tablet/mobile widths и клавиатурой | Core values/actions доступны, фокус/labels/scroll стабильны |
| M08-14 | Rebuild profile projection | Static sheet/profile hash совпадает до и после rebuild |

## Definition of Done

- [ ] level N/level-up/HP/multiclass selected scope golden tests зелёные;
- [ ] inventory/start money/equipment/attunement/effects реализованы;
- [ ] spell selection/recommendation/fallback и derived actions готовы;
- [ ] static sheet responsive/accessibility baseline пройден;
- [ ] profile version/diff/provenance/replay deterministic;
- [ ] character ACL enforced in API/cache/search;
- [ ] Gameplay profile/rebase contract published;
- [ ] M08-01…M08-14 пройдены без P0/P1 defects.

## Критический check перед завершением

- Не смешаны ли current HP/ammo/charges с static profile/inventory ownership?
- Корректен ли HP после retroactive modifier и multiclass во всех golden cases?
- Может ли removed/unequipped item оставить modifier/action?
- Recommendation не блокирует/манипулирует выбор и объяснима?
- Profile old version остаётся воспроизводимой после compendium update?
- ACL проверена в sheet, recommendation, search и audit, а не только route?

`NO-GO`: двойной владелец runtime, stale equipment modifier, lost old profile,
неверный HP/resource golden case, ACL leak, unusable core sheet on reference device.

Evidence: level-N golden matrix, effect provenance demo, ACL matrix, responsive/
accessibility report, projection checksum.

## Вне scope

Current gameplay counters, rests applying recovery, real attack execution, ammo
spending and statuses — steps 13–14. Full printable PDF/export — step 17.
