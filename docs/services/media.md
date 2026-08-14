# Media Service

## Bounded context

Владеет жизненным циклом пользовательских бинарных assets: quota reservation,
direct upload, integrity/security validation, metadata, derivatives, map tiling,
access audience, retention и deletion. Не владеет сценой, портретом персонажа или
compendium entry: они хранят `AssetId` и реагируют на состояние Media.

## Aggregates and entities

### `MediaAsset` aggregate

- owner subject + tenant scope (`user`, `campaign`, `public-pack`);
- original object key, expected/actual size, SHA-256, detected MIME;
- state `Reserved|Uploading|Uploaded|Scanning|Processing|Ready|Quarantined|
  DeletionPending|Deleted|Failed`;
- dimensions/duration/frame count and safe normalized metadata;
- purpose (`map`, `token`, `portrait`, `handout`, `audio`, `video`, `pack-art`);
- audience/access revision, retention/legal hold;
- derivative set/current processing revision.

### `UploadReservation` entity

- multipart upload id, reserved bytes/quota, expiresAt, part limits;
- checksum requirement, allowed purpose/content types;
- one active completion result per idempotency key.

### `Derivative` entity

- kind (`thumbnail`, `token`, `webp`, `map-tile`, `waveform`, etc.);
- dimensions/quality/content hash/object key/size;
- source processing revision and ready state.

### `QuotaAccount` aggregate/read owner

Usage/reservation by user/campaign/subscription ref. Billing plan belongs to
Campaign/billing future context; Media enforces supplied limit and owns byte usage.

## Invariants

- object key generated server-side and never trusts filename/path;
- completion size/hash/parts match reservation and object storage metadata;
- Ready only after scan + safe decode + required derivatives;
- decoded pixels/duration/archive ratio within purpose limits;
- quarantined asset cannot mint delivery URL or be newly referenced;
- audience cannot be broader than owning campaign/pack policy;
- delete source only after reference/dependency check + retention period;
- quota reservations atomic; abandoned multipart releases quota by expiry worker;
- derivatives are reproducible and never replace original audit/hash.

## Domain events

- `UploadReserved`, `UploadPartObserved`, `UploadCompleted`;
- `AssetIntegrityVerified/Failed`, `AssetScanPassed/Failed`;
- `AssetProcessingStarted`, `DerivativeCreated`, `AssetReady`;
- `AssetQuarantined/Restored`, `AssetProcessingFailed/Retried`;
- `AssetAudienceChanged`, `AssetReferenceCountChanged`;
- `AssetDeletionRequested/Cancelled/Completed`, `UploadReservationExpired`;
- `QuotaReserved/Committed/Released/Exceeded`.

## Integration events

- `AssetReady.v1 { assetId, tenantScope, purpose, mediaType, dimensions,
  derivativeManifestRevision, contentHash, audienceRevision }`;
- `AssetQuarantined.v1 { assetId, reasonCode, effectiveAt }`;
- `AssetAudienceChanged.v1 { assetId, audienceRevision }`;
- `AssetDeletionScheduled.v1 { assetId, deleteAfter }`;
- `AssetDeleted.v1 { assetId }`.

Events содержат metadata, не signed URL/object credentials.

## Связи

- Identity provides user lifecycle; Campaign provides membership/quota plan ref.
- Scene, Character, Compendium validate Ready + audience before setting asset ref.
- Search indexes only safe public metadata/thumbnails.
- Object storage events are untrusted infrastructure signals; Media verifies them.
- CDN signed URL/key service uses Media audience decision and short TTL.

## Public API

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/media/uploads` | `tenantScope`, `purpose`, filename display only, size, MIME hint, SHA-256, multipart preference | reserve quota and return presigned URL/parts constraints |
| `POST /api/v1/media/uploads/{uploadId}/parts` | requested part numbers | mint bounded presigned part URLs |
| `POST /api/v1/media/uploads/{uploadId}:complete` | parts + ETags/checksums | verify and enqueue scan/process; returns operation |
| `DELETE /api/v1/media/uploads/{uploadId}` | none | abort multipart/release reservation |
| `GET /api/v1/media/assets/{assetId}` | none | metadata/state/derivatives/reference count summary |
| `GET /api/v1/media/assets` | tenant/purpose/state/cursor | authorized asset library |
| `POST /api/v1/media/assets:batch-resolve` | max 100 ids, requested variants, context | short-lived signed delivery URLs after ACL |
| `POST /api/v1/media/assets/{assetId}:retry-processing` | expected processing revision | owner/operator safe retry |
| `PATCH /api/v1/media/assets/{assetId}` | display name/alt text/audience; `If-Match` | metadata/access, no binary replacement |
| `POST /api/v1/media/assets/{assetId}/deletion` | reason; `If-Match` | schedule if references/policy allow |
| `DELETE /api/v1/media/assets/{assetId}/deletion` | none | cancel within retention |
| `GET /api/v1/media/usage` | tenant scope | used/reserved/quota by media class |

### Map derivative contract

For `purpose=map`, processing generates:

- normalized dimensions/color space, thumbnail;
- image pyramid tiles by content-hash path, manifest with tile size/levels;
- optional low-quality placeholder;
- no automatic grid inference in P0 unless explicit separate operation; inference
  result is suggestion, never changes Scene.

Signed delivery URL is CDN/object-store URL, not `/api` byte stream. Immutable
derivatives use long cache; authorization token/audience layer must not expose a
private asset through a previously public URL.

## Limits initial

- allowed source images PNG/JPEG/WebP; animated/video enabled by feature/purpose;
- max original 100 MiB map, 10 MiB token/portrait initially;
- max decoded 200 megapixels map, 25 megapixels token; tune by worker memory;
- multipart part/total count bounded; upload reservation TTL 24 h;
- SVG, HTML, executable archives rejected in core flow;
- audio/video codecs/limits documented when feature enabled.

## Storage/read models

- event stream per asset/quota account; PostgreSQL metadata/reference projections;
- source/private derivatives in S3-compatible buckets with versioning;
- quarantine bucket/prefix separate permissions; processing workers cannot mutate
  metadata directly, only submit result command;
- `AssetLibraryView`, `DerivativeManifest`, `QuotaUsage`, `DeletionQueue`.

## Key tests/SLI

- spoofed MIME/magic, checksum mismatch, oversized decode, polyglot, zip bomb;
- expired/replayed presigned completion, multipart concurrency, quota race;
- private/public/campaign URL isolation and quarantine invalidation;
- worker crash/retry exactly-once logical derivative set;
- referenced asset deletion and account/campaign erasure workflow;
- SLI: reservation p95 <250 ms, 99% normal images Ready <30 s, map tile job p95
  by size class, quarantine delivery zero, queue oldest age.
