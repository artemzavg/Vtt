# Шаг 11. Chat, Dice и Macros

Статус: `Planned`  
Зависимости: шаги 05 и 10  
Результат: участники ведут историю чата, выполняют безопасные серверные броски и
макросы; public/GM/self/whisper visibility не раскрывается другим клиентам.

## Затрагиваемые сервисы

- **Chat & Dice** — channels/messages/rolls/macros/RNG owner.
- **Session & Realtime** — authorized low-latency delivery.
- **Campaign** — channel/audience/retention capabilities.
- **Ruleset** — dice dialect/functions and actor variable contract.
- **Character** — authorized static variable projection.
- **Edge/React** — chat panel, formula editor, history, hotbar baseline.
- **Search & Projections** — chat indexing выключен по умолчанию; summary only.

## Разрабатываемые возможности

- campaign/session chat channels and cursor history;
- public, GM-only, self-only, whisper messages/rolls;
- sanitized text/structured messages, reply/edit/tombstone;
- formula grammar: dice/keep-drop/arithmetic/parentheses/named variables;
- preview AST/cost/provenance;
- CSPRNG server result, immutable roll id/dice outcomes/reroll history;
- slash `/roll` and autocomplete;
- personal/campaign macros with typed safe parameters;
- macro hotbar baseline;
- rate/cost limits, moderation report stub and export operation;
- real-time delivery/reconnect history convergence.

## Конкретный план реализации

1. Расширить minimal roll kernel шага 07 до full typed formula grammar.
2. Property/fuzz parser, integer/decimal/overflow/limits and production RNG wiring.
3. Реализовать ChatChannel, message append/edit/tombstone and cursor sequence.
4. Создать audience-specific DTO/event delivery; private payload не идёт в broad
   bus subject/cache.
5. Реализовать formula preview and exact variable snapshot authorization.
6. Связать roll result с optional channel message; retry request returns same roll.
7. Реализовать MacroSet/parameter AST expansion/reparse/cost limit.
8. React: chat/history/whisper audience indicator/formula editor/result detail/
   keyboard shortcuts/hotbar.
9. Добавить retention/export minimum and report workflow skeleton.
10. Интегрировать realtime gap/reconnect через persisted channel cursor.

## Definition of Ready

- [ ] grammar/functions/rounding/error codes/limits опубликованы;
- [ ] visibility matrix public/GM/self/whisper agreed;
- [ ] edit/tombstone/retention/export policy определена;
- [ ] RNG version/audit metadata and test injection boundary reviewed;
- [ ] character variable allowlist and snapshot semantics готовы;
- [ ] macro capability intentionally excludes script/network/DOM;
- [ ] session delivery subject/DTO не содержит private payload broadly.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M11-01 | Отправить public text и перезагрузить clients | Message ordered/persisted, history cursor resumes without duplicate |
| M11-02 | Отправить GM-only/self/whisper из разных ролей | Только audience получает payload/notification/history/count |
| M11-03 | Проверить XSS markdown/link/attachment fixtures | Отображается sanitized content; script/event/unsafe URL не выполняются |
| M11-04 | Выполнить `2d20kh1+5`, parentheses и named character variable | AST/result/die outcomes/variable source/provenance верны |
| M11-05 | Отправить invalid/deep/huge dice/explosion formula | Stable bounded error/429; CPU/memory/connection остаются стабильными |
| M11-06 | Повторить roll request id и выполнить явный reroll | Retry возвращает тот же roll; reroll создаёт новый id, оба видны |
| M11-07 | Попытаться изменить/удалить roll result | Immutable result не меняется; допустимая visibility tombstone audited |
| M11-08 | Edit/tombstone обычный text в/после allowed window | Revision mark/history policy соблюдены, order не меняется |
| M11-09 | Создать macro с typed params и попыткой injection | Valid macro re-parsed/executed; injection/script rejected |
| M11-10 | Выполнить macro без доступа к actor variable | Authorization error без раскрытия value/existence |
| M11-11 | Потерять realtime messages и reconnect | Chat cursor догружает историю, клиенты сходятся |
| M11-12 | Открыть hidden roll logs/cache/bus as unauthorized test consumer | Payload отсутствует/зашифрован и не доступен broad consumer |
| M11-13 | Export allowed channel/date range | Только разрешённая audience history, edit/tombstone policy reflected |

## Definition of Done

- [ ] chat/history/visibility/edit/tombstone/export baseline работают;
- [ ] formula parser/evaluator fuzz/property and bounded cost verified;
- [ ] server RNG immutable/idempotent and production test RNG impossible;
- [ ] macros/hotbar safe AST, no arbitrary code;
- [ ] realtime/history convergence and cursor tests зелёные;
- [ ] hidden/whisper privacy checked API/cache/bus/client;
- [ ] accessible chat/formula/result UI готов;
- [ ] M11-01…M11-13 пройдены без P0/P1 defects.

## Критический check перед завершением

- Можно ли предсказать следующие rolls из audit metadata?
- Может ли retry/RNG failure незаметно сделать новый roll?
- Утёк ли hidden result в NATS, trace, cache, export или websocket buffer?
- Может ли formula/macro вызвать CPU/memory/regex exhaustion или code execution?
- Подменяет ли клиент actor variables/total?
- Стабилен ли message order при edit/reconnect/duplicate delivery?

`NO-GO`: client-authoritative total, hidden leak, mutable roll, formula DoS,
production deterministic RNG, macro code execution, duplicate message/roll effects.

Evidence: parser fuzz/cost report, hidden audience payload audit, RNG/idempotency
test, reconnect history report, security sanitization report.

## Вне scope

3D dice, roll tables/decks, reactions/action buttons applying gameplay, full
moderation UI and chat search. Action cards подключаются на шаге 14; расширения — 17.
