# Шаг 05. Ruleset engine и публикация версий

Статус: `Planned`  
Зависимости: шаги 02 и 04  
Результат: автор создаёт typed ruleset draft, валидирует, компилирует и публикует
immutable version; .NET и browser worker вычисляют одинаковый result/provenance.

## Затрагиваемые сервисы

- **Ruleset** — DSL/schema/draft/compile/publish/artifact owner.
- **Campaign** — pin exact published version.
- **Edge Gateway/BFF** — authoring/debug API routing and limits.
- **Search & Projections** — ruleset catalog.
- **React web** — authoring minimum + browser evaluation worker.

## Разрабатываемые возможности

- typed fields/resources/choice sets/features/actions/modifiers;
- bounded expression AST: arithmetic, conditions, selectors, roll references;
- dependency graph/cycle detection/static types/cost budget;
- modifier stacking and provenance;
- draft/validate/compile/golden-tests/publish;
- immutable server/browser artifacts by hash;
- campaign pin и deprecate metadata;
- author/debug evaluator;
- минимальный generic fixture, D&D-oriented golden scenarios без hardcode.

## Конкретный план реализации

1. Зафиксировать DSL JSON schemas и canonical serialization/hash.
2. Реализовать parser/AST/static type system/cost estimator без `eval`.
3. Реализовать dependency graph, unknown selector и cycle diagnostics.
4. Реализовать modifier algebra/stacking groups и explanation tree.
5. Сделать .NET evaluator и TypeScript/browser worker evaluator на одном corpus.
6. Реализовать Ruleset/RulesetDraft aggregates, document revisions и conflicts.
7. Добавить validation operation, compilation targets и signed/hash manifest.
8. Publish допускает только successful validation/golden report, version immutable.
9. Реализовать artifact cache/pull-by-hash и campaign exact pin.
10. Создать fixtures минимум для двух разных механик: D&D d20 и dice-pool/percentile,
    чтобы не зацементировать одну систему.
11. Fuzz/property/performance tests и sandbox resource limits.

## Definition of Ready

- [ ] DSL primitives и явно неподдерживаемые конструкции согласованы;
- [ ] numeric/null/boolean/list types, rounding и overflow semantics описаны;
- [ ] modifier order/stacking and provenance examples утверждены;
- [ ] canonical hashing и semantic version policy определены;
- [ ] browser/server golden corpus format готов;
- [ ] expression/package limits имеют начальные числа;
- [ ] threat model исключает code/network/filesystem/time access.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M05-01 | Создать ruleset draft и добавить typed base/computed fields | Draft version растёт, schema errors указывают JSON Pointer/stable code |
| M05-02 | Добавить неизвестный selector и type mismatch | Validation отклоняет до publish с понятным path |
| M05-03 | Создать dependency cycle A→B→A | Cycle route показан, compile/publish запрещены |
| M05-04 | Проверить add/set/min/max/multiply и stacking conflict | Result и suppressed modifiers соответствуют agreed algebra/provenance |
| M05-05 | Выполнить один golden case в .NET и browser worker | Result/profile hash идентичны byte-for-byte canonical output |
| M05-06 | Изменить draft конкурентно из двух tabs | Stale update получает conflict/diff, чужое изменение не теряется |
| M05-07 | Опубликовать invalid и затем valid draft | Invalid rejected; valid immutable version/artifact/hash/catalog event |
| M05-08 | Попытаться изменить published definition | API rejects; создаётся только новый draft/version |
| M05-09 | Pin version к campaign, затем deprecate её | Campaign остаётся воспроизводимой, GM видит warning/replacement, нет auto-upgrade |
| M05-10 | Подать expression/package на лимите и сверх лимита | На лимите bounded latency; сверх — deterministic validation error, без resource spike |
| M05-11 | Попытаться внедрить JS/function/network-like payload | Не парсится/не исполняется; CSP worker без eval |
| M05-12 | Выполнить non-D&D dice-pool/percentile fixture | Engine выражает agreed primitive без D&D-specific field names |

## Definition of Done

- [ ] DSL schemas/versioning/canonical hash published in contracts;
- [ ] .NET/browser parity corpus выполняется в CI;
- [ ] draft concurrency, validation, compile, publish/deprecate работают;
- [ ] published artifact immutable/content-addressed and cacheable;
- [ ] cycle/type/cost/sandbox fuzz tests зелёные;
- [ ] provenance показывает active/suppressed modifiers;
- [ ] Campaign pin и Search catalog integration verified;
- [ ] typical cached evaluation укладывается в выбранный budget;
- [ ] M05-01…M05-12 пройдены без P0/P1 defects.

## Критический check перед завершением

- Есть ли D&D-specific magic в engine вместо package definitions?
- Может ли package заставить engine зависнуть, выделить unbounded memory или
  выполнить код?
- Совпадают ли rounding/order/canonical hashes на .NET и JS для edge cases?
- Можно ли воспроизвести старый character после публикации новой версии engine?
- Не меняется ли published artifact задним числом через dependency `latest`?
- Достаточен ли язык для двух различных system fixtures?

`NO-GO`: browser/server divergence, mutable published version, arbitrary code,
unbounded expression, скрытый `latest`, hardcoded universal `Strength/AC` model.

Evidence: golden parity report, fuzz/cost benchmark, artifact hashes before/after,
two-system fixture demo, security review.

## Вне scope

Full visual no-code system builder, WASM extensions, migrations between arbitrary
versions и public creator marketplace. Шаг создаёт стабильный engine и минимальный
authoring API/UI, достаточный для внутренних packages.
