# Шаг 06. Compendium и лицензированный SRD pipeline

Статус: `Planned`  
Зависимости: шаги 04 и 05  
Результат: версия открытого контента импортируется, проверяется, публикуется и
доступна по exact refs; приватные packs не утекут в public/campaign search.

## Затрагиваемые сервисы

- **Compendium** — packs/entries/license/version/moderation owner.
- **Ruleset** — content contract validation.
- **Campaign** — allowed packs/content-set revision.
- **Search & Projections** — FTS/facets/catalog.
- **Media** — только asset contract fixture до шага 09.
- **Edge/React** — browse/search/entry view/internal import report.

## Разрабатываемые возможности

- CompendiumPack/PackDraft/Entry version model;
- types: class, species, background, feature, item, spell, creature, condition;
- exact refs, dependencies and localized content;
- license/provenance/attribution manifest;
- validate/publish/deprecate/quarantine;
- internal import pipeline для выбранного открытого SRD fixture;
- PostgreSQL FTS, type/source/level/tag facets and autocomplete baseline;
- private/campaign/unlisted/public visibility;
- campaign allowlist и content-set revision;
- batch-get/export format skeleton.

## Конкретный план реализации

1. Провести legal/content manifest review: какой SRD/version/language используется,
   какая attribution обязательна, что намеренно исключено.
2. Определить normalized entry schema + localized display AST + mechanics payload.
3. Реализовать pack/draft/version aggregates и immutable entry document storage.
4. Реализовать exact dependency refs, reverse index и publish validation.
5. Написать repeatable importer из законного source artifact в intermediate format;
   raw protected books/неразрешённые переводы не коммитить.
6. Ruleset валидирует mechanics; failed rows получают actionable import report.
7. Реализовать search/read/batch-get с audience/locale-aware cache keys.
8. Добавить Campaign allowed-pack commands и content-set revision events.
9. Реализовать deprecate/quarantine/takedown propagation priority lane.
10. Добавить pack export с license/attribution и content hashes.
11. Создать internal moderation stub без public community submission.

## Definition of Ready

- [ ] source content и license reviewed; attribution text approved;
- [ ] точная SRD версия выбрана, 5.1 и 5.2.1 не смешиваются неявно;
- [ ] entry types/mechanics contracts согласованы с Ruleset;
- [ ] locale fallback и sanitized rich-text format определены;
- [ ] visibility/access/cache matrix утверждена;
- [ ] import fixture можно хранить/распространять в repository/CI;
- [ ] search relevance golden queries подготовлены.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M06-01 | Импортировать approved SRD fixture дважды | Результат/hash детерминирован, duplicate entries не создаются |
| M06-02 | Импортировать entry с invalid mechanics/source/license | Row rejected с path/code; valid rows policy явна, publish до исправления запрещён |
| M06-03 | Опубликовать pack и получить exact entry | Manifest/entry immutable, exact version/hash/source/attribution видимы |
| M06-04 | Изменить published entry | Отклонено; новая редакция только через draft/new pack version |
| M06-05 | Поиск spell/item/creature по имени и facets | Релевантные результаты, стабильный cursor, source/version видимы |
| M06-06 | Проверить RU/EN fallback на отсутствующей локали | Fallback явно помечен, mechanics не меняются скрыто |
| M06-07 | Создать private и campaign pack, искать другим user/campaign | Ни hit, ни suggestion, ни count/snippet/cache leakage |
| M06-08 | Разрешить/удалить pack в campaign | Content set revision меняется; доступ/search обновляется в SLO |
| M06-09 | Quarantine опубликованный test entry | Он быстро исчезает из выдачи/distribution; audit/source history сохраняется |
| M06-10 | Batch-get mix allowed/forbidden/missing exact refs | Per-item safe result без раскрытия forbidden existence, порядок сохранён |
| M06-11 | Экспортировать pack | Export содержит manifest, hashes, license и required attribution |
| M06-12 | Подать HTML/SVG/script payload в localized description | Отображается sanitized text/AST, script/event handlers не выполняются |

## Definition of Done

- [ ] legal manifest/attribution и approved source задокументированы;
- [ ] deterministic importer + validation report работают;
- [ ] packs/entries exact immutable versions опубликованы;
- [ ] search/facets/autocomplete и batch-get meet baseline;
- [ ] private/campaign/public access/cache isolation tested;
- [ ] quarantine/deprecate/access propagation работает;
- [ ] export всегда включает attribution/license;
- [ ] XSS/import/fuzz/contract tests зелёные;
- [ ] M06-01…M06-12 пройдены без P0/P1 defects.

## Критический check перед завершением

- Имеем ли мы право распространять каждый source text/translation/artifact?
- Может ли `latest` изменить уже собранного character?
- Может ли private entry попасть в public autocomplete/cache/log?
- Совпадают ли mechanics и localized text versions/provenance?
- Удаляет ли quarantine выдачу быстро и без переписывания history?
- Не стал ли Compendium вычислять character/gameplay rules?

`NO-GO`: неясная лицензия, отсутствующая attribution, protected content в repo,
floating refs, private search leakage, mutable published entry, raw executable HTML.

Evidence: content/license manifest sign-off, deterministic import hashes, access
matrix report, search golden report, quarantine trace, export sample.

## Вне scope

Public community publishing, marketplace, full moderation team workflow, arbitrary
homebrew editor и licensed commercial books. Они рассматриваются в шагах 17–18.
