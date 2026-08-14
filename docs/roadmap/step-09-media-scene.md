# Шаг 09. Media pipeline и Scene foundation

Статус: `Planned`  
Зависимости: шаги 04 и 06  
Результат: GM безопасно загружает карту, создаёт сцену с grid/layers и размещает
PC/NPC tokens; браузер быстро pan/zoom/render-ит сцену.

## Затрагиваемые сервисы

- **Media** — upload/quota/scan/derivatives/object access owner.
- **Scene** — scene/grid/layers/spatial objects/tokens owner.
- **Campaign** — GM/member/audience policy.
- **Character** — exact token source/profile/access.
- **Compendium** — NPC snapshot source.
- **Edge Gateway/BFF** — upload reservation/scene bootstrap/signed media URLs.
- **React/PixiJS** — canvas, map tiles, object editing.

## Разрабатываемые возможности

- presigned multipart map/token/portrait upload;
- checksum/MIME/magic/dimension/quota validation, scan, quarantine;
- thumbnails/WebP/map tile pyramid/manifest;
- scene/folder/navigation, background, dimensions;
- square/hex/gridless configuration and snapping;
- layers, audience/lock/z-order;
- token placement/move/rotate/size/nameplate/bars definitions;
- token source Character или immutable Compendium NPC snapshot;
- basic tiles/drawings/text/notes placeholders;
- viewport chunk query, Pixi rendering/culling/texture cache;
- scene edit concurrency, bounded batch, safe undo compensation baseline.

## Конкретный план реализации

1. Реализовать upload reservation/quota/direct object upload/complete operation.
2. Isolated worker проверяет hash/magic/decoded pixels, scan and derivatives.
3. Сделать private bucket/CDN signing contract and audience-aware batch resolve.
4. Реализовать Scene aggregate/grid/layers/object versioning.
5. Хранить coordinates fixed-precision; property tests square/hex/gridless.
6. Реализовать token source snapshot/access and scene bootstrap DTO by audience.
7. Добавить PostGIS/spatial projection и viewport/layer/kind query.
8. React/Pixi: tiled background, pan/zoom, selection, drag locally, snapping,
   culling/LOD; до шага 10 final move сохраняется HTTP command.
9. Добавить edit batch/compensation and audit for GM setup.
10. Performance fixture: large map, 300 tokens, 2 000 primitive wall placeholders.

## Definition of Ready

- [ ] media type/size/decoded pixel/quota limits зафиксированы;
- [ ] object storage/CDN local and target provider semantics reviewed;
- [ ] scene coordinate/grid conventions and zoom scale specified;
- [ ] layer/audience/token controller matrix согласована;
- [ ] Character/NPC snapshot behavior при source update/delete определено;
- [ ] canvas reference device/performance fixture готов;
- [ ] safe delete/reference/retention policy описана.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M09-01 | Загрузить valid PNG/JPEG/WebP карту через multipart | Direct upload; Ready operation; thumbnail/tiles/hash manifest доступны по signed URL |
| M09-02 | Прервать multipart и дождаться TTL/abort | Reservation/quota освобождены, orphan parts очищены |
| M09-03 | Подать spoofed MIME, huge dimensions и malicious fixture | Quarantined/rejected без worker/API crash; URL не выдаётся |
| M09-04 | Создать scene с square/hex/gridless и настроить origin/scale | Grid/snapping/measure reference точны после reload |
| M09-05 | Создать/reorder/lock GM/player layers | Player payload не содержит GM layer objects; locked layer reject edits |
| M09-06 | Добавить PC token и NPC snapshot token | Correct appearance/source/access; snapshot не меняется скрыто при source update |
| M09-07 | Двигать/масштабировать/вращать token, открыть stale second tab | First commit accepted, stale object edit conflicts без потери других objects |
| M09-08 | Выполнить multi-object batch и undo после/без conflict | Safe batch compensates; conflict prevents destructive blind undo |
| M09-09 | Pan/zoom большой tiled map на reference device | Нет загрузки full-resolution целиком; frame/memory budget соблюдён |
| M09-10 | Отобразить 300 tokens и viewport culling | Interaction budget соблюдён, offscreen objects не грузят render path |
| M09-11 | Quarantine/delete уже связанный test asset | Scene показывает safe placeholder/degraded state, не public stale URL |
| M09-12 | Подставить чужой campaign asset/scene/object id | Read/write/signed URL rejected без metadata leakage |
| M09-13 | Restart media worker во время processing | Operation resumes/retries logically once, duplicate derivatives не появляются |

## Definition of Done

- [ ] secure upload/scan/processing/quarantine/delete lifecycle работает;
- [ ] map derivatives and private delivery URLs готовы;
- [ ] scenes/grids/layers/tokens/object concurrency/batches реализованы;
- [ ] PC/NPC refs и audience payload server-filtered;
- [ ] Pixi canvas pan/zoom/select/drag/render meets reference budget;
- [ ] spatial/read projections rebuild and checksum verified;
- [ ] media/scene threat and performance tests зелёные;
- [ ] M09-01…M09-13 пройдены без P0/P1 defects.

## Критический check перед завершением

- Может ли private/GM asset получить долгоживущий public URL?
- Декодирует ли untrusted image API process вместо isolated worker?
- Отправляется ли скрытый layer/object игроку «для сокрытия CSS»?
- Стабильны ли coordinates при zoom/reload и разных browsers?
- Не блокирует ли один object edit всю огромную Scene aggregate?
- Переживает ли source deletion token snapshot/scene без corruption?

`NO-GO`: XSS/malicious upload execution, private URL leak, GM payload leak,
full-map memory blowup, silent stale overwrite, unsafe asset deletion.

Evidence: upload security report, audience payload diff, canvas performance trace,
large-scene demo, object concurrency/undo report.

## Вне scope

Realtime/presence, fog/walls/light/vision, runtime HP/status bars, video/animated
maps and ambient sound. Следующие этапы 10, 12–14 и 17.
