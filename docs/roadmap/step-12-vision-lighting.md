# Шаг 12. Fog of War, walls, doors, lighting и vision

Статус: `Planned`  
Зависимости: шаги 09 и 10  
Результат: игрок получает только разрешённое представление сцены с per-user fog,
wall-aware lighting и darkvision; GM может точно preview-ить его видимость.

## Затрагиваемые сервисы

- **Scene** — walls/lights/fog checkpoints/visibility revisions owner.
- **Session & Realtime** — visibility invalidation and audience delivery.
- **Campaign** — GM/player/token control policy.
- **Character** — senses/darkvision static profile.
- **Media** — optional fog texture/source assets.
- **React/Pixi/Web Workers** — polygons, masks, lighting render/quality presets.
- **Gameplay** — только будущий spatial contract fixture, без combat action.

## Разрабатываемые возможности

- walls: movement/sight/light restrictions;
- doors: open/closed/locked/secret and permissions;
- ambient/global light, bright/dim radius, darkness, directional sources;
- token vision/darkvision and basic detection modes;
- current visibility vs explored fog;
- automatic per-user/token fog exploration;
- manual GM reveal/hide/reset with generation/version;
- recipient-filtered hidden tokens/objects;
- GM «preview as player/token» and diagnostics;
- Web Worker visibility/light computation, revision cache and quality presets;
- chunked fog checkpoint + bounded deltas/compaction.

## Конкретный план реализации

1. Зафиксировать geometry semantics and golden scene fixtures.
2. Реализовать Wall/Light typed entities, object commands and revisions.
3. Реализовать door state machine/capabilities and collision flags.
4. Создать server visibility authority for sensitive payload + client worker render
   algorithm; common test vectors verify polygon agreement.
5. Реализовать FogExploration aggregate by audience, reset generation and chunks.
6. Automatic exploration applies authorized visibility polygon, coalesced/checkpointed.
7. Session sends revision invalidation/delta, not hidden source data.
8. Pixi lighting/fog masks, darkvision appearance, low-quality/reduced-animation.
9. GM editor/debug overlay: invalid walls, vision token, light sources, preview.
10. Benchmark 2k walls/50 lights/300 tokens and weak reference device.

## Definition of Ready

- [ ] exact bright/dim/darkvision semantics chosen for first ruleset;
- [ ] geometry fixed precision/tolerance and wall/door types specified;
- [ ] current vs explored fog and per-user/group audience policy agreed;
- [ ] source-of-truth split server authorization/client rendering reviewed;
- [ ] golden maps include doors, holes, angles, overlapping lights and edge cases;
- [ ] performance/quality preset budgets prepared;
- [ ] fog data retention/reset/compaction design approved.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M12-01 | Добавить walls/door и двигать controlled token | Vision/movement blocked; open door updates visibility for authorized clients |
| M12-02 | Проверить locked/secret door as Player/GM | Player cannot discover/control secret metadata; GM sees/controls/audits |
| M12-03 | Создать bright/dim/directional lights за walls | Radii/angle/wall constraints соответствуют fixture, changes real-time |
| M12-04 | Сравнить normal vision и darkvision token | Unlit/lit appearance/range per ruleset, no color-only critical distinction |
| M12-05 | Исследовать коридор, уйти и reload | Current area normal; explored area fogged; unexplored hidden; checkpoint persists |
| M12-06 | Два players исследуют разные области | Per-user policy не смешивает fog, если campaign setting не group-shared |
| M12-07 | GM manual reveal/hide/reset во время session | Только chosen audience; generation prevents stale client delta reappearing |
| M12-08 | GM preview as each player/token | Preview совпадает с реальным second browser view/authorized payload |
| M12-09 | Скрытый token вне vision/за wall | Игрок не получает token/object secret data, не только invisible sprite |
| M12-10 | Reconnect после wall/light/fog revisions | Client получает latest snapshot/deltas и не показывает кратко forbidden state |
| M12-11 | Запустить golden scenes в worker/server | Polygon/visibility hashes within defined equivalence/tolerance |
| M12-12 | 2k walls/50 lights/reference device quality high/low | Budgets met; preset degrades effects, не authorization correctness |
| M12-13 | Повредить/задержать fog checkpoint job | Gameplay continues with safe current vision; recovery no exploration loss beyond RPO |

## Definition of Done

- [ ] walls/doors/lights/darkness/vision/darkvision implemented for P0 scope;
- [ ] automatic/manual per-audience fog, reset and checkpoints work;
- [ ] hidden objects filtered server-side and verified adversarially;
- [ ] GM preview equals player view in golden/E2E;
- [ ] worker/server geometry fixtures and reconnect tests зелёные;
- [ ] canvas quality presets meet performance budget;
- [ ] fog compaction/rebuild/retention operational test passed;
- [ ] M12-01…M12-13 пройдены без P0/P1 defects.

## Критический check перед завершением

- Авторизация видимости или только рендеринг выполняется на клиенте?
- Можно ли узнать secret door/token из websocket/bootstrap/devtools?
- Совпадает ли GM preview с реальным authorized second client?
- Может ли stale fog delta пережить reset и раскрыть/скрыть неверную область?
- Не event-source-ится ли каждый кадр vision/movement?
- Не падает ли слабый клиент ниже budget при обычной сцене?

`NO-GO`: secret data delivered, preview mismatch, stale reset corruption, client-
only authorization, unbounded fog events, performance below agreed minimum.

Evidence: authorized payload diffs, golden visibility hashes, two-browser GM preview
record, fog reset/rebuild report, reference-device performance trace.

## Вне scope

Advanced invisibility/truesight/tremorsense rules, weather, roof/elevation,
interactive regions and pathfinding. Они входят в шаги 14, 17–18 as separate scope.
