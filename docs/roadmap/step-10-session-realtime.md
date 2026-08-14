# Шаг 10. Session & Realtime multiplayer

Статус: `Planned`  
Зависимости: шаги 08 и 09  
Результат: GM и игроки одновременно входят в игровую комнату, видят presence и
синхронное движение разрешённых tokens; reconnect восстанавливает state без
дублирования команд.

## Затрагиваемые сервисы

- **Session & Realtime** — session/room/join/presence/sequence owner.
- **Edge Gateway/BFF** — SignalR endpoint/protocol/admission/rate limits.
- **Campaign** — membership/policy revision.
- **Scene** — active scene and durable final token position.
- **Character** — active selection/controller relation.
- **Redis/NATS** — ephemeral routing и integration delivery.
- **React/PixiJS** — optimistic movement, correction, reconnect UX.

## Разрабатываемые возможности

- session lobby/start/pause/resume/end;
- short-lived one-use join ticket;
- presence snapshot/join/leave/heartbeat;
- protocol current/previous negotiation;
- room shard/epoch/fenced lease/monotonic sequence;
- ephemeral cursor/drag deltas with coalescing;
- final authoritative token move to Scene;
- optimistic UI + server correction;
- ack/replay buffer/delta resync/full authorized snapshot;
- node drain/reconnect and slow-client backpressure;
- live policy revoke/kick and active character/token selection.

## Конкретный план реализации

1. Реализовать GameSession aggregate and HTTP lifecycle.
2. Создать join ticket mint/consume with policy revision/protocol/nonce/TTL.
3. Реализовать SignalR MessagePack envelopes, size/type/cost validation.
4. Ввести room directory/lease/epoch/fencing and monotonic sequence.
5. Реализовать recipient-safe presence and session snapshot composition.
6. Сделать cursor/drag ephemeral path; coalesce/drop stale intermediates.
7. DragEnd вызывает idempotent Scene MoveToken expected object version.
8. Client stores lastAck/pending commands, performs delta or snapshot resync.
9. Slow-client bounded buffer → warning/resync/disconnect, never unbounded memory.
10. Graceful node drain передаёт rooms; hard kill triggers new epoch/reconnect.
11. Добавить multi-browser harness, WebSocket load/soak/reconnect storm tests.

## Definition of Ready

- [ ] client/server protocol schemas and compatibility window approved;
- [ ] authoritative vs ephemeral message table complete;
- [ ] room lease/fencing storage and failover behavior designed;
- [ ] join ticket TTL/capabilities/policy invalidation specified;
- [ ] replay buffer limits and snapshot contract measured/estimated;
- [ ] target room size/burst/load profiles from capacity doc selected;
- [ ] browser pending-command reconciliation state machine reviewed.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M10-01 | GM starts lobby/session, 5 users join in separate contexts | One-use tickets accepted once; correct roles/presence/scene snapshot |
| M10-02 | Reuse/expire ticket или изменить membership после mint | Admission rejected; no connection created with stale capability |
| M10-03 | Player moves controlled token | Local drag smooth; peers receive deltas; final Scene position/version committed |
| M10-04 | Player moves чужой/GM-only token | Server rejects; optimistic UI corrected; forbidden payload/state not exposed |
| M10-05 | Два controllers одновременно двигают token | Expected-version conflict resolved deterministically, clients converge |
| M10-06 | Отключить сеть во время drag, затем reconnect | Uncommitted delta discarded/corrected; committed final move not duplicated |
| M10-07 | Пропустить несколько room sequences | Delta replay if available; otherwise authorized snapshot; state converges |
| M10-08 | Hard-kill realtime node | Clients reconnect to new epoch/node, no stale old owner publishes after fence |
| M10-09 | Graceful deploy/drain node | Server draining signal, bounded reconnect, session remains usable |
| M10-10 | Замедлить один client/переполнить buffer | Только slow client resync/disconnect; room/shard memory bounded |
| M10-11 | Suspend/kick active member | Connection loses capabilities/disconnects, next command rejected immediately enough |
| M10-12 | Connect client previous protocol and unsupported protocol | Previous works in window; unsupported receives actionable upgrade error |
| M10-13 | Проверить event store после интенсивного drag/cursor | Только final meaningful moves/session facts; mouse/cursor events отсутствуют |
| M10-14 | 100 rooms × 6 connections smoke/soak | Delivery/reconnect/error/headroom within current stage targets |

## Definition of Done

- [ ] session lifecycle, join, presence and selections implemented;
- [ ] ordered room protocol current/previous and safe DTO shaping stable;
- [ ] optimistic drag/final Scene commit/correction converge;
- [ ] reconnect delta/snapshot and node failover verified;
- [ ] policy revoke, slow-client backpressure and message limits enforced;
- [ ] ephemeral updates absent from event store/NATS durable domain stream;
- [ ] load/soak correctness report зелёный;
- [ ] M10-01…M10-14 пройдены без P0/P1 defects.

## Критический check перед завершением

- Есть ли split-brain window, в котором два sequencers публикуют один room epoch?
- Может ли slow client вызвать unbounded queue/GC pause для всех?
- Отправляется ли GM/hidden payload в broad room до recipient filtering?
- Повторяется ли final command после reconnect с двойным state change?
- Можно ли восстановиться, если replay buffer полностью потерян вместе с Redis?
- Не попали ли cursor/drag deltas в event store/observability body logs?

`NO-GO`: lost/duplicate committed move, unauthorized payload, split-brain sequence,
unbounded buffer, reconnect без convergence, stale membership сохраняет control.

Evidence: multi-client sequence log, node-kill/reconnect video/report, event-store
inspection, authorization payload diff, 100-room load/soak report.

## Вне scope

Chat/dice, fog/light/vision, encounter/gameplay HP/status automation. Протокол должен
оставить typed namespaces, но пустые сообщения этих функций не реализуются.
