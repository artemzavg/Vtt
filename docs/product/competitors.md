# Анализ конкурентов и продуктовые выводы

Дата исследования: 2026-08-14. Анализ основан преимущественно на официальных
публичных страницах. Маркетинговые заявления конкурентов считаются заявленными,
а не независимо измеренными. Закрытые/платные сценарии не reverse-engineered.

## Источники и ограничения

- [Roll20 homepage](https://roll20.net/) — основные возможности и масштаб
  поддерживаемых систем.
- [Roll20 Crash Course](https://help.roll20.net/hc/en-us/articles/360039223834-Roll20-Crash-Course) — листы, handouts, folders и dynamic lighting.
- [Roll20 D&D 2024 change log](https://help.roll20.net/hc/en-us/articles/34086640235927-2024-Change-Log) — builder, level-up, equipment, spells, effects и automation.
- [Roll20 Characters](https://help.roll20.net/hc/en-us/articles/360037258594) — персонажи вне кампаний и ограничения синхронизации.
- [Foundry FAQ](https://foundryvtt.com/article/faq/) и
  [Canvas Layers](https://foundryvtt.com/article/canvas-layers/) — canvas, lighting,
  fog, audio/video, layers и модульность.
- [Foundry Lighting](https://foundryvtt.com/article/lighting/),
  [Scenes](https://foundryvtt.com/article/scenes/) и
  [Tokens](https://foundryvtt.com/article/tokens/) — стены, per-user fog,
  darkvision/detection modes и параметры сцены.
- [Foundry Tutorial](https://foundryvtt.com/article/tutorial/) — chat, combat tracker,
  actors, items, journals, roll tables, playlists и compendium packs.
- [Foundry API v14](https://foundryvtt.com/api/modules/foundry.html) — публичная
  extensibility model и типы документов.
- [Virtual TTG Club](https://new.ttg.club/vttg) — официальный перечень функций.
- [Dungeon Book](https://dungeon-book.ru/) — публичная beta-страница SPA,
  прочитанная в интерактивном браузере; сам VTT на дату среза помечен «в разработке».
- [Ролебаза](https://rolebaza.ru/) — официальный лендинг; закрытая alpha/летняя beta
  2026 означает, что часть возможностей является обещанной.

## Сводная матрица

Обозначения: `✓` — официально заявлено/доступно; `△` — ограничено, зависит от
системы, тарифа/модуля или находится в разработке; `—` — не подтверждено
исследованными публичными источниками.

| Возможность | Roll20 | Foundry VTT | VTTG | Dungeon Book | Ролебаза |
|---|---:|---:|---:|---:|---:|
| Браузерный VTT без self-hosting | ✓ | △ self-host/hoster | △ download/demo | ✓ ecosystem | ✓ |
| Несколько игровых систем | ✓ 1 200+ | ✓ systems/modules | — D&D 5e | ✓ builder | ✓ заявлено |
| Листы персонажей | ✓ | ✓ | ✓ | ✓ | ✓ |
| Guided character builder | ✓ для части систем | △ system/module | △ интеграция | △ generic formulas | — |
| Полный автоматический level-up | △ D&D builder | △ system/module | — | — | — |
| Compendium/drag-and-drop | ✓ | ✓ packs | ✓ ttg.club | △ lists/rules | — |
| Карты, tokens, grid/layers | ✓ | ✓ | ✓ | △ VTT в разработке | ✓ |
| Dynamic lighting + walls | ✓ tariff | ✓ | ✓ | △ в разработке | — |
| Per-user fog/vision | ✓ | ✓ | ✓ | △ в разработке | △ fog |
| Initiative/combat tracker | ✓ | ✓ | ✓ | △ в разработке | ✓ |
| Chat + dice formulas | ✓ | ✓ | ✓ | △ в разработке | ✓ |
| 3D dice | △ | △ module | ✓ | — | ✓ заявлено |
| Audio/playlists | ✓ | ✓ | ✓ | △ в разработке | — |
| Video/voice | ✓ | ✓ WebRTC | — | — | ✓ |
| Macros/extensions | ✓ API/macros | ✓ modules/API | △ formulas | ✓ formulas | ✓ macros |
| World wiki/journal/graph | ✓ journal | ✓ journal | — | ✓ сильная сторона | — |
| LFG | ✓ | — core | — | PWA/LFG план | ✓ |
| Вход игрока по ссылке без регистрации | — | △ host user | — | — | ✓ |
| RU-first UX | △ community | △ community | ✓ | ✓ | ✓ |

## Наблюдения по продуктам

### Roll20

Сильные стороны:

- низкий порог входа: browser-first, invites/LFG, готовые adventures;
- широкий охват систем, character sheets, compendium и marketplace;
- D&D builder уже автоматизирует starting equipment, level-up, spells,
  modifiers, resource recovery и часть effect application;
- зрелая связка chat rolls, macros, tokens, turn tracker и dynamic lighting.

Ограничения/возможность дифференциации:

- глубина автоматизации неоднородна между системами;
- часть lighting/API-функций привязана к подписке;
- отдельные персонажи вне VTT, согласно справке, не имеют real-time sync с
  игровым листом;
- старые и новые варианты D&D создают заметную сложность совместимости.

Вывод: нельзя конкурировать только наличием карты и бросков. Нужен более цельный
builder → sheet → token → action → combat state pipeline и прозрачная версия правил.

### Foundry VTT

Сильные стороны:

- наиболее богатый scene canvas: layers, walls, light/darkness, fog, detection,
  audio, templates, weather/regions и подробные token settings;
- game systems/modules и публичный client API создают сильную экосистему;
- единая document model для Actors, Items, Scenes, Combat, Chat, Journals,
  Compendia и Active Effects;
- self-hosting даёт владельцу контроль над данными и модификациями.

Ограничения/возможность дифференциации:

- установка/hosting и совместимость core/system/module увеличивают сложность;
- качество character automation зависит от конкретного system/module stack;
- свободный код модулей повышает возможности, но усложняет безопасность,
  воспроизводимость и поддержку SaaS.

Вывод: взять уровень scene tooling и расширяемость, но реализовать расширения через
versioned schemas, декларативные правила и permissioned sandbox, а не общий доступ
к internals платформы.

### Virtual TTG Club (VTTG)

Официально заявляет maps/layers/fog/markers, walls/doors, directional lighting,
tokens с HP/statuses, инициативу, 3D dice со сложными формулами, animated/video
scenes, effects, playlists и прямую интеграцию заклинаний/существ/предметов ttg.club.

Главное преимущество — русскоязычная D&D-вертикаль и единая база контента.
Уязвимость для универсального продукта — тесная ориентация на D&D 5e. Наш ответ:
сохранить столь же удобную интеграцию D&D, но сделать content/rules packages
первичными и версионируемыми.

### Dungeon Book

На дату анализа уже доступны visual system builder, девять типов полей,
формулы/conditions/dice notation, list entities, character/NPC sheets с live
recalculation, миры с wiki, graph, timeline, maps и node-level access. VTT с fog,
tokens, combat mode, chat, audio, права и undo/redo явно отмечен как находящийся
в активной разработке.

Главная сильная сторона — user-created systems и worldbuilding. Это подтверждает
спрос на generic schema/formula engine. Наше отличие должно быть в строгом
versioning/publishing lifecycle, глубокой автоматизации официального первого
ruleset и низколатентном authoritative gameplay.

### Ролебаза

Лендинг заявляет RU-first browser UX, карты/scenes, tokens/layers, generic sheets,
initiative, fog, dice/macros, HP/statuses, LFG, встроенное видео, адаптивную
web-версию и join по ссылке без регистрации. На дату среза продукт сообщает о
закрытой alpha с апреля 2026 и открытой beta летом 2026, поэтому сравнение
производительности и глубины функций преждевременно.

Продуктовый урок: мгновенный guest join, русский интерфейс и близкий регион —
важные acquisition/latency features, а не косметические дополнения.

## Незаполненные рынком сценарии

### 1. Объяснимая автоматизация персонажа

Каждый calculated field и action должен отвечать на «почему?». В sheet inspector
показываются base value, активные modifiers, подавленные conflicts, source/version
и situational toggles. Это существенно снижает недоверие к автоматике.

### 2. Безопасная универсальность

Универсальный ruleset builder не должен исполнять произвольный JS. Нужны typed
definitions, expression AST, dependency graph, cycle detection, resource budgets,
publish validation, signed package и deterministic evaluation.

### 3. Версии правил как часть состояния

Персонаж и кампания закреплены за точной версией. Обновление создаёт migration
preview с broken choices и diff. Это лучше скрытого изменения compendium entries.

### 4. Устойчивый real-time UX

Optimistic token drag, reconnect по sequence, low-bandwidth mode, local scene
cache и server correction дают быстрый стол даже при нестабильной сети.

### 5. Уровни автоматизации

Группы различаются. `Manual` только предлагает формулу, `Assist` предлагает
применить результат, `Automatic` исполняет безопасные шаги. GM может менять
политику по action/encounter без отказа от всей автоматики.

### 6. Переносимость и доверие

Экспорт campaign/character/homebrew, roll audit, backup/restore и понятные права
на контент снижают platform lock-in и являются конкурентной функцией.

## Рекомендации по приоритету

1. Доказать вертикальный slice: D&D character builder → sheet → token → attack →
   save/damage/status → chat log.
2. Затем довести базовый scene tool до стен, fog, light/vision и GM preview.
3. После этого открыть homebrew content authoring; generic system builder без
   зрелого validator/versioning слишком рано создаст дорогую совместимость.
4. Journals, audio, LFG и video важны, но не должны задерживать уникальную
   автоматизацию. Video особенно следует выделять в отдельный cost domain.
5. Plugin SDK выпускать после стабилизации contracts; до этого расширение через
   versioned rules/content packages и webhooks.
