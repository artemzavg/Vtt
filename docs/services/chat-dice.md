# Chat & Dice Service

## Bounded context

Владеет campaign/session chat channels, messages, whispers/visibility, immutable
server-side dice results, formula parser/evaluator contract, roll history, macros,
roll tables/card-like outputs (later), moderation tombstones and exports.

Не применяет damage/HP, не решает hit/save semantics и не хранит encounter order.
Gameplay передаёт точную roll requirement и использует returned immutable result.

## Aggregates and entities

### `ChatChannel` aggregate

- campaign/session scope, channel type, retention and posting policy;
- last sequence/message refs, archived state;
- audience definition and moderation settings.

### `ChatMessage` entity/stream item

- author/service actor, created/edited timestamps;
- kind `text|roll|action-card|system|whisper`;
- sanitized rich-text AST or structured card payload;
- audience/visibility, reply/thread ref, edit/tombstone revision;
- correlation refs to roll/action/encounter;
- attachments as authorized Ready media refs.

### `DiceRoll` immutable aggregate/record

- roll id/request id/idempotency key;
- original normalized formula + parsed AST/hash;
- resolved variable inputs/modifiers/provenance;
- individual die outcomes and total/success metadata;
- RNG algorithm/version and auditable entropy metadata (без секрета, позволяющего
  предсказывать будущие rolls);
- visibility `public|gm|self|whisper-targets`;
- requester/actor/action refs; creation server timestamp.

### `MacroSet` aggregate

- owner/campaign, named macros/hotbar slots;
- safe formula/command template AST, parameters, visibility/version;
- no arbitrary script, network or DOM access.

## Formula language P0

Supports bounded grammar:

- dice: `NdS`, keep/drop `kh/kl/dh/dl`, explode only if explicitly enabled;
- arithmetic `+ - * /`, parentheses, min/max/floor/ceil and comparisons allowed by
  ruleset profile;
- named read-only variables resolved from authorized actor profile/runtime;
- ruleset aliases/functions map to typed AST nodes;
- optional target number/success counting where system defines it.

Limits: expression chars/depth/AST nodes, max dice/sides/explosions, integer/
decimal range, evaluation steps and result payload. Regex parser/backtracking and
`eval` forbidden. Division by zero/unknown variable produce stable error, not NaN.

## RNG and fairness

- cryptographically secure OS RNG on server; client 3D physics only visualizes
  already returned results;
- retries with same request id return same roll; new roll id is visible reroll;
- hidden GM roll result encrypted/ACL protected and never broadcast to players;
- optional future commit-reveal/verifiable randomness is separate ADR; do not claim
  provable fairness without it;
- test RNG injectable only in test environment/explicit deterministic simulation,
  blocked by production composition root.

## Invariants

- roll immutable after creation; edit/delete not allowed, only legal tombstone
  visibility policy retaining audit;
- requester authorized for actor variables and visibility audience;
- one request id/formula context resolves once;
- text edit preserves edit history/revision; author/window policy enforced;
- whisper audience fixed at creation unless new message;
- macro expansion re-parsed and cost-limited; parameter cannot inject AST code;
- action card buttons contain opaque authorized command descriptor/ref, not trusted
  amounts supplied by rendered HTML.

## Domain events

- `ChatChannelCreated/Archived`, `ChannelRetentionChanged`;
- `ChatMessageCreated/Edited/Tombstoned`, `MessageReactionAdded/Removed`;
- `DiceRollRequested/Resolved/Rejected`, `DiceRollLinkedToAction`;
- `MacroCreated/Changed/Deleted`, `HotbarSlotAssigned`;
- `ChatExportRequested/Completed`;
- `MessageReported`, `MessageModerationApplied`.

## Integration events

- `ChatMessageCreated.v1 { channelId, messageId, sequence, kind, audienceClass,
  clientSafeSummaryRef }`;
- `ChatMessageChanged.v1 { messageId, revision, state }`;
- `RollRecorded.v1 { rollId, requestId, actorId?, visibilityClass, formulaHash,
  resultSummaryRef }`;
- `MacroSetChanged.v1 { ownerScope, revision }`;
- `ChatChannelArchived.v1`.

Private text/result not placed in broad bus subject. Session fetches recipient-safe
DTO or event goes to audience-partitioned subject with service ACL.

## Связи

- Campaign supplies members/channel policy/retention.
- Session broadcasts authorized message/roll DTOs and presence typing only.
- Character/Gameplay provide allowed variable projection; Dice never queries full
  private sheet synchronously if immutable roll context supplied.
- Gameplay requests rolls and consumes immutable result by request id.
- Media supplies authorized attachments; Search indexes only allowed messages and
  may omit chat entirely by default privacy policy.
- Ruleset supplies formula dialect/function registry artifacts.

## Public API

### Chat

| Method/path | Body/query | Назначение |
|---|---|---|
| `GET /api/v1/campaigns/{campaignId}/channels` | session/state/cursor | authorized channels |
| `POST /api/v1/campaigns/{campaignId}/channels` | type/name/audience/retention | create custom/system channel if allowed |
| `GET /api/v1/channels/{channelId}/messages` | `before|after` opaque cursor, limit ≤100, kinds | authorized ordered history |
| `POST /api/v1/channels/{channelId}/messages` | kind text/whisper, content AST/markdown, recipients, replyTo, attachments | create message; Idempotency-Key |
| `PATCH /api/v1/channels/{channelId}/messages/{messageId}` | text content; `If-Match` | edit within policy, retains revision history |
| `DELETE /api/v1/channels/{channelId}/messages/{messageId}` | reason; `If-Match` | tombstone own/moderated message |
| `POST /api/v1/channels/{channelId}/messages/{messageId}/reports` | category/details | moderation report |
| `POST /api/v1/channels/{channelId}/exports` | date range/format/audience | async authorized export |

### Dice

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/dice/rolls` | formula, variable context/actor ref, visibility, channelId, label | parse/authorize/roll/store/post optional |
| `POST /api/v1/dice/rolls:preview` | formula, actor ref/context | AST, normalized formula, variable values, cost/warnings; no RNG |
| `GET /api/v1/dice/rolls/{rollId}` | none | authorized immutable result/provenance |
| `GET /api/v1/dice/grammar` | rulesetVersionId | supported functions/limits/autocomplete metadata |

Internal gameplay roll:

| Method/path | Body | Назначение |
|---|---|---|
| `POST /internal/v1/dice/rolls` | requestId, exact normalized roll requirement, variable snapshot/hash, visibility/action refs | low-latency immutable resolve; idempotent |
| `POST /internal/v1/chat/action-cards` | committed resolution summary/ref, audiences | structured non-forgeable action message |

### Macros

| Method/path | Body/query | Назначение |
|---|---|---|
| `GET /api/v1/macros` | owner/campaign/cursor | visible macros/hotbar |
| `POST /api/v1/macros` | name, safe template, typed parameters, visibility | compile/store macro |
| `PATCH /api/v1/macros/{macroId}` | fields; `If-Match` | update/version |
| `DELETE /api/v1/macros/{macroId}` | `If-Match` | delete |
| `POST /api/v1/macros/{macroId}:execute` | parameter values, actor/targets/channel context | expand → authorize → dice/action route |
| `PUT /api/v1/macro-hotbars/{slot}` | macroId/clear; `If-Match` | assign personal/campaign slot |

## Storage/read models

- channel/message append streams partitioned by channel/month if measured;
- immutable roll records and formula AST; text content encrypted at rest;
- `ChannelHistory`, `UnreadCursor`, `RollHistory`, `MacroLibrary`, `ModerationView`;
- cursor includes channel sequence; edits do not change original order;
- retention job tombstones/deletes content according to policy while preserving
  minimal integrity/security audit if legally allowed.

## Key tests/SLI

- parser fuzz/property, complexity limits, integer/overflow/division edge cases;
- retry same request = same roll; concurrent requests distinct; production RNG DI;
- hidden/whisper ACL at API, bus, cache, realtime and export layers;
- XSS/markdown/link/attachment sanitize; macro injection;
- history cursor under concurrent append/edit/tombstone;
- Gameplay timeout/retry and action card cannot double-apply;
- SLI: dice p95 ≤150 ms/p99 ≤400 ms, chat append p95 ≤200 ms, history p95
  ≤250 ms, zero hidden result leaks, durable request correctness 99.9999% target.
