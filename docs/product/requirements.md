# Требования и каталог функций

Статус: `Proposed`  
Приоритеты: `P0` — обязательное ядро, `P1` — первая полноценная версия,
`P2` — развитие, `P3` — исследование.

## 1. Видение и принципы продукта

Платформа объединяет подготовку кампании, компендиум, создание персонажа и
синхронную игру на виртуальном столе. Главная ценность — не просто цифровой лист,
а объяснимая автоматизация: пользователь видит, из какого ruleset, предмета,
особенности или эффекта получено каждое значение и почему действие доступно.

Продуктовые принципы:

- **System-agnostic core.** D&D — первая система, но ни API, ни базовые агрегаты
  не содержат полей вроде `strength` или `spellSlot` как универсальных понятий.
- **Automation with escape hatches.** Автоматика предлагает и проверяет, но GM
  может разрешить override/homebrew; override хранит причину и автора.
- **Server authoritative, client responsive.** Клиент применяет обратимые
  optimistic updates, сервер подтверждает итог и версию состояния.
- **Explainability.** Производное значение содержит provenance: правило, версия,
  входы, модификаторы и порядок применения.
- **Reversibility.** Настройка сцены и персонажа поддерживает preview/diff,
  autosave draft, undo там, где это безопасно, и журнал изменений.
- **Portable content.** Пользователь может экспортировать принадлежащие ему
  персонажи, кампании и homebrew в документированный формат.
- **Accessible by default.** Клавиатурная навигация, контраст, уменьшение анимации,
  screen-reader labels и альтернативный текст для важных сценовых объектов.

## 2. Пользователи, роли и права

### 2.1 Платформенные роли

- `User`: создаёт/участвует в кампаниях, управляет своим контентом.
- `Moderator`: рассматривает жалобы на публичный контент и пользователей.
- `Support`: диагностирует проблемы только через ограниченный audited access.
- `PlatformAdmin`: управляет платформой; не получает неограниченный доступ к
  приватным кампаниям по умолчанию.

### 2.2 Роли кампании

- `Owner/GM`: владелец, биллинг, удаление кампании, полный доступ.
- `CoGM`: сцены, NPC, встречи, журнал и контент; без биллинга/удаления владельца.
- `Player`: участие в сессиях и разрешённые персонажи/токены.
- `Observer`: просмотр разрешённого состояния без управляющих команд.
- `Guest`: временный join по ссылке с ограниченным сроком и набором прав.

Роль — стартовый набор разрешений, но окончательное решение строится на relation-
based ACL. Для персонажа отдельно задаются `owner`, `editor`, `controller`,
`viewer`; для токена — кто видит, управляет и открывает связанный лист; для
сцены/журнала — GM-only, selected players или вся группа. Смена прав применяется
к новым командам немедленно и инвалидирует join tickets.

### 2.3 Базовые account flows — P0

- регистрация и подтверждение email;
- вход/выход, refresh token rotation, завершение отдельных/всех сессий;
- восстановление пароля, passkey/WebAuthn и TOTP как P1;
- OAuth/OIDC вход через внешних провайдеров как P1;
- профиль, локаль, часовой пояс, display name, avatar;
- согласия, экспорт и удаление аккаунта с retention/legal-hold политикой;
- rate limiting, защита от credential stuffing, журнал security-событий.

## 3. Кампании и совместная игра

### P0

- создать, архивировать и восстановить кампанию;
- выбрать `rulesetId + immutable version` и разрешённые homebrew packages;
- приглашение ссылкой/кодом, отзыв приглашения, лимит использований и TTL;
- одновременное подключение GM и игроков, presence, reconnect/resume;
- lobby, активная сцена, список участников и их connection state;
- несколько персонажей пользователя, но явный выбор активного персонажа;
- права на листы, токены, сцены, handouts и журнал;
- серверный audit trail критических команд GM и изменений прав;
- campaign settings: язык, единицы, сетка, dice visibility, automation level.

### P1/P2

- расписание сессий, RSVP и напоминания (P1);
- шаблоны кампаний и безопасное клонирование (P1);
- поиск группы/LFG, анкеты и moderation/report flow (P2);
- campaign branches/sandbox для подготовки GM без публикации игрокам (P2);
- импорт/экспорт из открытого формата, затем точечные импортёры конкурентов при
  наличии законных и стабильных API/форматов (P2).

## 4. Killer feature: автоматизированное создание персонажа

### 4.1 Wizard и жизненный цикл — P0

- создать draft независимо от кампании либо сразу в кампании;
- выбрать систему и версию правил: как минимум D&D SRD 5.1 и SRD 5.2.1 как
  разные пакеты, без неявного смешивания;
- пошаговый wizard с autosave, back/forward, resume и review before commit;
- граф зависимостей шагов: изменение класса инвалидирует только зависимые выборы;
- preview/diff перед применением level-up, respec или смены ruleset version;
- предупреждения не блокируют разрешённый GM homebrew; жёсткие инварианты
  блокируют публикацию, если GM не включил соответствующее house rule;
- provenance для каждого итогового значения и action: `base + modifiers`, источник,
  версия правила, выбор пользователя, GM override;
- детерминированный пересчёт и воспроизводимый результат на одной версии ruleset.

### 4.2 Характеристики — P0

- ручной ввод и фиксированный массив;
- point buy с ruleset-specific стоимостью, минимумами и максимумами;
- случайные броски с задаваемой ruleset формулой (например `4d6kh3`), публичной,
  приватной GM или локальной видимостью;
- журнал серверных бросков, исключающий незаметный reroll;
- рекомендация распределения по классу/подклассу и объяснение приоритета;
- drag-and-drop распределение, проверка дублей и остатка point budget;
- автоматические racial/species/background bonuses либо свободные `+2/+1` и
  альтернативные варианты, если они разрешены выбранной версией правил.

### 4.3 Происхождение и особенности — P0

- species/race, subrace/lineage, background/origin, языки, размеры, senses;
- автоматическое добавление traits, proficiencies, movement и granted actions;
- choice sets с prerequisites и взаимоисключающими вариантами;
- custom/homebrew сущность с теми же структурированными modifiers/effects;
- ручная особенность с описанием без автоматики и структурированная особенность
  с безопасной формулой; UI явно различает эти режимы;
- происхождение каждого effect и возможность временно отключить разрешённый
  situational modifier при броске.

### 4.4 Класс, уровень и здоровье — P0

- класс, подкласс, multiclass и prerequisites;
- создание сразу на уровне N с воспроизведением всех выборов по уровням;
- автоматический proficiency bonus, hit dice, class resources, features,
  resource recovery и количество prepared/known spells;
- HP: максимум первого уровня; далее fixed average, ручной выбор или серверные
  throws; учёт Constitution modifier по правилам конкретной версии;
- повторный пересчёт HP при изменении Constitution с прозрачной разбивкой;
- level-up wizard с preview/diff и возможностью отката незавершённого draft;
- short/long rest, recharge и альтернативные recovery periods из ruleset.

### 4.5 Инвентарь, закуп и артефакты — P0/P1

- starting equipment choices либо starting currency (P0);
- магазин с фильтрами, валютами, стоимостью, количеством и encumbrance (P0);
- контейнеры, weight, currency conversion и custom items (P1);
- equip/unequip, attunement limits, charges и recovery (P0);
- временные и постоянные modifiers от экипированных предметов: minimum/maximum,
  additive, multiplicative, advantage/disadvantage, resistance/immunity (P0);
- конфликтующие эффекты разрешаются ruleset-defined stacking policy; UI показывает
  применённые и подавленные эффекты (P0);
- ammo/consumable counters, автоматическое списание только после подтверждённого
  действия, возврат при отменённой транзакции (P0).

### 4.6 Заклинания, действия и рекомендации — P0/P1

- фильтр доступных spells по классу, уровню, источнику, school, components,
  ritual/concentration, attack/save и подготовке (P0);
- cantrips, known/prepared spell limits, granted spells, spellbook (P0);
- recommendations на основе класса, роли, текущего набора и типов задач; каждая
  рекомендация объяснима и не подменяет выбор пользователя (P1);
- предупреждения о похожих spells, нехватке utility/defense и недоступных choices
  без навязывания «оптимального» билда (P1);
- автоматическое построение actions/attacks из weapons, spells и features (P0);
- attack bonus, save DC, damage/healing formula, damage type, range, targets,
  resource/ammo cost, critical rules и upcast/scaling (P0);
- situational query перед броском и отображение формулы до подтверждения (P0).

### 4.7 Лист персонажа — P0

- быстрый desktop/tablet UI и ограниченный, но функциональный mobile UI;
- вкладки summary/actions/spells/inventory/features/biography/notes/audit;
- derived values обновляются без полного refetch;
- pin/favorite actions и hotbar;
- режим редактирования и режим игры, защита от случайной перезаписи;
- локальные optimistic changes с server correction и conflict UI;
- экспорт printable PDF — P1, переносимый JSON — P1;
- private GM notes и поля, видимые выбранным участникам — P1.

## 5. Виртуальный стол: карты и сцены

### P0

- multipart upload PNG/JPEG/WebP и безопасная обработка; большие карты тайлятся;
- сцены, folders, thumbnails, порядок и активация для выбранных игроков;
- square/hex/gridless, размер клетки, масштаб, origin и snapping;
- слои: background, tiles, drawings, grid, walls/doors, lights, tokens,
  templates, notes, GM overlay;
- pan/zoom, selection, multi-select, copy/paste, lock, z-order, keyboard shortcuts;
- токены PC/NPC, bars, nameplate, tint, size, rotation, statuses, vision;
- привязка токена к character или compendium NPC snapshot;
- ruler, area templates, ping, arrows, text и freehand drawing;
- manual fog reveal/hide и per-user explored fog;
- стены, двери, windows/terrain walls, line of sight и collision;
- ambient/global light, dim/bright light, darkness, directional light;
- darkvision и расширяемые vision/detection modes;
- optimistic dragging: 60 FPS локально, сетевые deltas coalesced, durable event
  только на завершение значимого перемещения;
- GM preview «что видит этот игрок/токен»;
- undo/redo для безопасных scene-edit операций и autosave.

### P1/P2

- animated maps/video backgrounds и tiles (P1);
- ambient audio emitters, playlists и soundboard (P1);
- doors controlled by players, secret doors, traps/regions/triggers (P1);
- weather, roof/occlusion, elevations и teleport regions (P2);
- встроенный простой map drawing/import из Dungeon Scrawl-like формата (P2);
- isometric/3D — только отдельное исследование (P3).

## 6. Компендиум и контент

### P0

- packs с immutable published versions и draft workflow;
- типы: rules, classes, subclasses, species, backgrounds, feats, items, spells,
  creatures/NPC, conditions, actions, roll tables, journal templates;
- полнотекстовый поиск, autocomplete, facets, локаль и source filtering;
- drag-and-drop/reference add без копирования текста там, где достаточно ссылки;
- snapshot механики при публикации персонажа/encounter для воспроизводимости;
- campaign/private homebrew, public community content и moderation state;
- provenance, license, attribution, author, source/version у каждой записи;
- NPC template + instantiate to campaign/scene, редактируемая локальная копия;
- импорт/экспорт валидируемых пакетов; quarantine до проверки.

### Лицензионное ограничение — P0 до наполнения

Официальный [SRD 5.2.1](https://www.dndbeyond.com/srd) опубликован под CC-BY-4.0,
которая допускает коммерческое использование при корректной атрибуции, однако
SRD не включает весь контент книг и часть защищённых названий. Поэтому:

- built-in pack включает только проверенный SRD/open/licensed content;
- атрибуция хранится структурированно и отображается при экспорте/публикации;
- коммерческий закрытый продукт не должен полагаться на Fan Content Policy как
  замену лицензии;
- название/логотип не создают впечатление официального D&D-продукта;
- пользователь обязан подтверждать права на загружаемый/публичный контент;
- действует report/takedown/moderation flow и блокировка повторной публикации hash.

## 7. Бой и gameplay automation

### P0

- encounters, combatants, команды start/pause/end;
- initiative individual/group, tie-break policy, manual override с audit;
- turn/round counter, next/previous, delay/ready при поддержке ruleset;
- action resolution: targets, range, line-of-sight, cover, advantage/disadvantage;
- attack roll → hit/miss/critical → damage → resistance/immunity/vulnerability → HP;
- spell save: определение DC, автоматический roll управляемых целей либо prompt,
  последствия success/failure/half/no damage;
- healing, temporary HP, death states/death saves как ruleset effects;
- concentration и реакция на damage; reminders и automation policy;
- statuses/conditions с duration, source, stacking, save-at-end/start и expiration;
- расход spell slots, class resources, charges, ammunition и consumables;
- GM может применить/отменить результат с причиной; компенсация создаёт новое
  событие, история не переписывается;
- уровни автоматизации: `manual`, `assist`, `automatic`; campaign/encounter/action
  overrides;
- combat log с формулами, исходными rolls, modifiers и результатом.

### P1/P2

- reactions/interrupt windows (P1);
- auras, area templates и автоматический target selection (P1);
- legendary/lair/mythic actions и multi-phase encounters (P1);
- pathfinding и movement budget (P2);
- deterministic replay/GM simulation sandbox (P2).

## 8. Чат, броски и макросы

### P0

- campaign/session chat с cursor history, retention и export;
- public, GM-only, self-only и whisper messages;
- dice parser: `2d20kh1+5`, parentheses, named variables и ruleset functions;
- формула разбирается в AST, имеет лимиты размера/количества кубов/времени и не
  выполняет пользовательский код;
- криптографически безопасный server-side RNG, уникальный roll id и audit data;
- сообщения действий и кнопки apply damage/save с authorization + idempotency;
- edit/delete policy: обычный текст можно редактировать с отметкой, результат
  броска неизменяем; moderation/tombstone вместо физической потери аудита;
- slash commands, autocomplete и пользовательские macros/hotbar;
- защита от XSS, sanitize markdown, rate limits и spam controls.

### P1/P2

- roll tables и card decks (P1);
- 3D dice как визуализация уже полученного server result, не источник истины (P1);
- voice/video WebRTC и TURN как отдельный платный контур (P2);
- transcripts/recording только с явным согласием всех участников (P3).

## 9. Дополнительные функции из анализа рынка

- journal/wiki, handouts, folders, linked map notes и permissions — P1;
- adventure templates, ready-to-play scenes и roll tables — P1;
- playlists, global/ambient audio и soundboard — P1;
- macro hotbar и package SDK — P1/P2;
- публичные/приватные ruleset builders для других систем — P2;
- plugin/module ecosystem: signed manifest, permissions, version compatibility,
  sandboxed Web Worker/WASM; без произвольного серверного кода — P2;
- LFG, session scheduling, player safety tools, X-card/private signal — P2;
- PWA/offline read cache, reconnect queue и low-bandwidth mode — P1;
- localization RU/EN и локализованные content variants — P1;
- guided onboarding, demo campaign и contextual help — P1;
- API/webhooks для владельцев контента с scopes/rate limits — P2;
- accessibility presets, reduced motion и non-canvas alternatives — P1;
- campaign backup, restore point и disaster export — P1.

## 10. Нефункциональные требования

Подробные числовые цели приведены в
[нефункциональной спецификации](../architecture/non-functional.md) и
[модели нагрузки](../architecture/capacity-slo-cost.md).

Обязательный baseline:

- горизонтальное масштабирование stateless API и realtime nodes;
- отсутствие single point of failure в production tier;
- p95 server-to-peer real-time latency до 150 мс внутри целевого региона;
- graceful reconnect с resync по `lastSeenSequence`;
- идемпотентные команды и at-least-once consumers;
- шифрование TLS и at-rest, секреты вне репозитория, least privilege;
- tenant/campaign isolation во всех запросах и событиях;
- backups + регулярно проверяемое восстановление;
- OpenTelemetry traces/metrics/logs, correlation/causation ids;
- unit, property, integration, contract, E2E, performance и chaos tests;
- privacy-by-design, configurable retention, data export/deletion;
- zero-downtime compatible deploys и event upcasters.

## 11. Явно вне первого релиза

- полноценный 3D tabletop/VR;
- marketplace с выплатами авторам и налоговым контуром;
- нативные мобильные приложения;
- генеративная AI-автоматизация, принимающая игровые решения за пользователя;
- multi-region active-active запись одной кампании;
- импорт защищённого контента конкурентов без лицензии/официального API.

## 12. Критерий готовности функции

Функция не считается готовой только по UI. Для P0/P1 обязательны:

1. domain invariants и authorization policy;
2. versioned API/event contracts и обработка конфликтов;
3. telemetry и alert-worthy failure modes;
4. unit/integration tests, E2E critical path и accessibility check;
5. migration/rollback и backward compatibility;
6. пользовательская документация и понятная ошибка;
7. threat-model delta для загрузок, формул, прав или публичного контента.

## 13. Матрица трассировки исходных требований

| Исходное требование | Где раскрыто | Владелец реализации |
|---|---|---|
| .NET backend + React frontend | [Architecture overview](../architecture/overview.md) | все backend services + Edge/web |
| DDD, CQRS, Event Sourcing, microservices/bounded contexts | [Data and events](../architecture/data-and-events.md), [service map](../services/README.md) | каждый owning service |
| Высокая нагрузка, быстрый backend push и UI без лагов | [NFR](../architecture/non-functional.md), [capacity](../architecture/capacity-slo-cost.md) | Edge, Session, Scene, projections |
| Unit/integration/E2E и раннее обнаружение багов | [Testing](../architecture/testing.md) | все сервисы/CI |
| Аналоги Roll20/VTTG/Dungeon Book/Foundry/Rolebaza | [Competitors](competitors.md) | Product/architecture review |
| Методы характеристик и class recommendation | §4.2 | Character + Ruleset + Chat/Dice |
| Расовые/свободные бонусы, racial/species features | §4.2–4.3 | Character + Ruleset/Compendium |
| Background/origin features | §4.3 | Character + Compendium |
| Starting equipment или закуп | §4.5 | Character + Compendium |
| Автоматический HP, уровень N, proficiency/resources/features | §4.4 | Character static profile + Gameplay runtime |
| Spells и recommendations | §4.6 | Character + Compendium/Ruleset |
| Attacks, hit/damage, ammo | §4.5–4.6, §7 | Gameplay + Character + Chat/Dice |
| Артефакты/equipment effects | §4.5 | Character profile + Ruleset, counters in Gameplay |
| PC/NPC tokens | §5 | Scene + Character/Compendium |
| Maps/scenes upload | §5 | Media + Scene |
| Fog, lighting, darkvision | §5 | Scene + client worker + Session delivery |
| NPC compendium | §6 | Compendium + Gameplay/Scene instantiation |
| Initiative | §7 | Gameplay & Encounter |
| Damage/HP automation | §7 | Gameplay & Encounter |
| Automatic spell saves | §7 | Gameplay + Chat/Dice |
| Statuses | §7 | Gameplay & Encounter |
| Chat/history | §8 | Chat & Dice + Session |
| Arbitrary dice formulas | §8 | Chat & Dice + Ruleset dialect |
| Authorization, character rights, GM/player roles | §2–3 | Identity + Campaign + Character ACL |
| Concurrent join/reconnect | §3, NFR | Session & Realtime + Edge |
| Future game systems | §1, ruleset architecture | Ruleset + Compendium contracts |

Любое новое требование сначала добавляется в этот каталог с приоритетом и
владельцем bounded context, затем — в service contract/roadmap/tests. Это не
позволяет случайно реализовать одну и ту же функцию в двух источниках истины.
