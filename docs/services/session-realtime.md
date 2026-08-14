# Session & Realtime Service

## Bounded context

Владеет живой игровой комнатой и transport semantics: session lifecycle, join
tickets, connections, presence, room shard/lease, monotonic room sequence,
ephemeral deltas, reconnect/resume and backpressure. Не владеет долговечными
scene objects, chat messages, rolls, HP или encounter results.

Realtime edge (SignalR nodes) принимает соединение, Session service решает, в
какую room shard направить и как упорядочить сообщения. Для small deployment эти
процессы могут быть одним deployable, но boundary сохраняется.

## Aggregates and operational entities

### `GameSession` aggregate

- id/campaign, GM creator, state `Scheduled|Lobby|Active|Paused|Ended`;
- active audience/scene refs, started/ended timestamps;
- admitted subjects/character selections and session settings snapshot;
- room epoch + last durable sequence checkpoint;
- session summary, not every presence transition forever.

### `JoinTicket` short-lived record

- opaque one-time token hash, session/campaign/subject/guest;
- roles/capabilities/object relations summary and policy revision;
- character/token selections, protocol version, expiry/nonce/used state.

### `ConnectionPresence` ephemeral record

- connection id, subject, device instance, room, node/shard, last heartbeat;
- controlled token refs, last ack room sequence, quality/backpressure state;
- Redis TTL; not event store source of truth.

### `RoomLease` / `RoomSequence`

- shard owner, epoch/fencing token, expires heartbeat;
- monotonically increasing sequence within epoch;
- compact replay buffer of client-safe messages with TTL/size bound.

## Invariants

- active session belongs to active/non-archived campaign;
- ticket current policy revision, unexpired, audience/protocol valid, one-use;
- subject cannot control token/character absent capability;
- room sequencer holds valid fenced lease; old owner cannot publish after failover;
- durable command receives stable command id and routes to one owner;
- client cannot choose recipients or inject server message type;
- replay buffer bounded per room/client; slow client degraded/disconnected;
- secret/GM payload shaped before entering recipient fan-out buffer.

## Message classes

1. `Ephemeral`: cursor, drag delta, typing, viewport pointer; coalesced, droppable,
   TTL seconds, never integration bus/event store.
2. `Authoritative command`: final token move, gameplay action, chat send; routed to
   owner with idempotency/expected version, then committed result sequenced.
3. `Authoritative notification`: client DTO derived from committed event/result;
   replayable within room buffer and ACL-shaped.
4. `Control`: ack, heartbeat, resync, backpressure, protocol error.

## Domain events

- `GameSessionScheduled/Started/Paused/Resumed/Ended`;
- `SessionSceneSelected`, `SessionCharacterSelected`;
- `JoinAdmissionGranted/Rejected` (sample/privacy policy may keep summary only);
- `RoomEpochAdvanced`, `SessionCheckpointCreated`;
- `SessionParticipantKicked`.

Presence/connection events go to operational telemetry, not durable domain history,
unless security/audit requirement explicitly needs a summarized fact.

## Integration events

- `SessionStarted.v1 { sessionId, campaignId, startedBy, roomRegion, settingsRevision }`;
- `SessionStateChanged.v1 { sessionId, state, activeSceneId?, roomEpoch }`;
- `SessionParticipantSelectionChanged.v1 { sessionId, subjectId, characterId?,
  tokenIds[], revision }`;
- `SessionEnded.v1 { sessionId, campaignId, endedAt, summaryRef }`.

Session consumes `SceneActivated`, `ActorRuntimeChanged`, `EncounterTurnChanged`,
`ChatMessageCreated`, `CampaignMembershipChanged` and turns them into recipient-
specific client notifications. It never republishes private domain payload blindly.

## Связи

- Edge authenticates transport and asks Session to consume join ticket.
- Campaign supplies membership/policy; revocation forcibly re-evaluates connections.
- Scene owns durable spatial commands/snapshots.
- Gameplay owns runtime/action commands/results.
- Chat & Dice owns message/roll commands/history.
- Character supplies selection/access summaries.
- Redis routes node/connection/presence; NATS delivers domain integration events.

## HTTP API

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/campaigns/{campaignId}/sessions` | optional scheduledAt/name/settings | create lobby/scheduled session |
| `GET /api/v1/campaigns/{campaignId}/sessions` | state/date/cursor | session history/schedule summaries |
| `GET /api/v1/sessions/{sessionId}` | none | authorized state/participants/scene/encounter refs |
| `POST /api/v1/sessions/{id}:start` | initial scene, settings; `If-Match` | acquire room and start |
| `POST /api/v1/sessions/{id}:pause` | reason; `If-Match` | pause authoritative gameplay commands |
| `POST /api/v1/sessions/{id}:resume` | `If-Match` | resume |
| `POST /api/v1/sessions/{id}:end` | confirmation; `If-Match` | checkpoint/end/release room |
| `POST /api/v1/sessions/{id}/selections` | characterId, requested tokenIds | choose active controllable actor |
| `DELETE /api/v1/sessions/{id}/participants/{subjectId}` | reason; GM capability | kick/revoke session admission, not campaign membership |
| `GET /api/v1/sessions/{id}/snapshot` | `afterSequence`, client capabilities | compact authorized room state/delta cursor |

Join ticket endpoint public расположен в Edge, internal creation/consume:

| Method/path | Параметры | Назначение |
|---|---|---|
| `POST /internal/v1/join-tickets` | principal/campaign/session/character/protocol + policy revision | mint one-use TTL token |
| `POST /internal/v1/join-tickets:consume` | token, connection nonce/node | atomic admission result |

## WebSocket protocol

Client envelope (MessagePack keys represented as JSON):

```json
{
  "v": 1,
  "type": "scene.tokenDragDelta",
  "commandId": "0198...",
  "clientSeq": 19,
  "roomEpoch": 4,
  "lastAck": 8812,
  "payload": {}
}
```

Server envelope:

```json
{
  "v": 1,
  "type": "scene.tokenMoveCommitted",
  "roomEpoch": 4,
  "roomSeq": 8813,
  "correlationId": "0198...",
  "payload": {}
}
```

Commands from client:

- `control.ack`, `control.resyncRequest`, `presence.heartbeat`;
- `cursor.move`, `scene.tokenDragStarted|Delta|Ended`, `scene.ping`;
- `gameplay.executeAction`, `gameplay.applyManualDelta`, `encounter.nextTurn`;
- `chat.send`, `dice.roll`.

Server notifications:

- `control.joined|resyncSnapshot|backpressure|error|serverDraining`;
- `presence.snapshot|joined|left`;
- scene/gameplay/encounter/chat typed committed DTOs;
- `*.pending` optional acknowledgment and `*.corrected` authoritative rollback.

Unknown type/protocol rejected; max frame/decoded payload enforced before allocation.
Per-type authorization and cost limits. Client never trusts event order across rooms.

## Reconnect algorithm

1. Client connects with fresh join ticket, `roomEpoch` and `lastAck`.
2. If epoch/buffer contain all missing sequences, server sends ordered delta.
3. Otherwise sends compact authorized snapshot at sequence N.
4. Client replaces normalized server state, reapplies only pending commands whose
   idempotency result is unknown, then resumes ack.
5. Expired/revoked membership rejects admission; UI returns to campaign lobby.

## Scaling/backpressure

- rendezvous/consistent hashing room→shard, fenced lease in Redis/consensus-backed
  store; room moves on node drain;
- local node fan-out, cross-node Redis/NATS ephemeral channel selected by benchmark;
- per-room ring buffer bounded bytes/messages/time;
- coalesce cursor/drag by `(room,subject,object,type)`; drop stale intermediate;
- authoritative messages never silently drop; slow client resyncs/disconnects;
- heartbeat adaptive, browser background state accounted for;
- autoscale connections + inbound/outbound bytes/messages + event loop/GC latency.

## Storage/read models

- durable GameSession event stream/summary in PostgreSQL;
- Redis TTL presence, join tickets, room directory, short replay buffers;
- `ActiveSessionByCampaign`, `SessionHistory`, `SessionParticipantSelection`;
- no full chat/scene/gameplay copies; snapshot is composed authorized projection.

## Key tests/SLI

- current/previous protocol, ticket replay/expiry/revocation, role changes live;
- fenced lease split-brain, node drain, reconnect delta/snapshot;
- ordering/gap/ack, duplicate client command, slow client/backpressure;
- secret payload never enters unauthorized buffer; 100-observer hot room;
- burst/soak/reconnect storm and memory/connection leak;
- SLI: admitted connections, p95 server→peer <150 ms, reconnect p95 <2 s,
  zero lost committed notifications after resync, room sequence gaps recoverable.
