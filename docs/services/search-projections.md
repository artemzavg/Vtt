# Search & Projections Service

## Bounded context

Query-only context, владеющий денормализованными cross-context views для поиска,
dashboard, discovery и BFF composition. Он не принимает доменные изменения и не
становится источником правды. Любая карточка содержит source ids/versions/asOf;
команда всегда направляется owning service.

Сервис оправдан как отдельный deployable, потому что cross-context search/read
нагрузка, rebuild lifecycle и индекс имеют другой scaling/failure profile. Простые
service-local projections остаются внутри owning service.

## Projection documents

- `UserDashboard`: recent/active campaigns, characters, pending invites;
- `CampaignWorkspaceIndex`: scene/character/encounter/chat summary refs;
- `CharacterSearchDocument`: permitted name/system/class/level/public summary;
- `CompendiumSearchDocument`: localized text, type/facets/license/access audience;
- `RulesetCatalogDocument`: system/version/compatibility/publisher;
- `SceneNavigationDocument`: authorized scene summary/thumb/active audience;
- `PublicContentDiscoveryDocument`: public packs/rulesets/homebrew moderation state;
- optional `CampaignJournalSearchDocument`; chat indexing disabled by default.

Every document fields:

- document id/type, source service/aggregate/version/event id;
- tenant/audience/access revision;
- locale/search analyzer version;
- indexedAt/sourceOccurredAt;
- public summary, not sensitive full domain payload;
- tombstone/quarantine state.

## Processing model

- durable JetStream consumers per projection family;
- inbox dedupe by event id and monotonic source aggregate version;
- projection handler fetches detail only from authorized internal query if event
  intentionally carries minimal data; no broad DB access;
- out-of-order old events ignored, gap triggers source resync/rebuild;
- delete/quarantine/access event has high-priority lane to shrink leakage window;
- periodic reconciliation samples projection vs owner versions/checksum;
- full rebuild writes versioned shadow index/table, validates, then atomically
  switches alias; old remains for rollback window.

## Search engine choice

MVP uses PostgreSQL FTS + `pg_trgm`/structured facet tables for fewer dependencies.
Introduce OpenSearch when measured needs include high corpus, multilingual scoring,
complex facets/autocomplete or PostgreSQL interference. Query API stays stable.

No semantic/vector search in P0. If later added, embedding source/license/access
and deletion propagation are mandatory; vectors are projections, not canonical.

## Consistency and authorization

- search is eventually consistent, p99 target ≤2 s normal;
- query includes principal/campaign scope; access filter applied in index/database
  and final result guard, not only post-filter page;
- cache key includes subject audience/policy revision/locale/query;
- stale policy revision on sensitive result fails closed or validates through owner;
- results expose `asOf`, optional `isIndexing`; absence in search does not prove
  resource does not exist;
- public index physically/logically separated from private tenant index where
  possible, reducing accidental cross-scope queries.

## Consumed integration events

From Identity: public profile revision/deactivation.  
From Campaign: lifecycle, membership/policy/content/ruleset changes.  
From Ruleset/Compendium: publish/deprecate/access/quarantine.  
From Character: profile/access/campaign link/retire.  
From Media: Ready/audience/quarantine/delete.  
From Scene: create/activate/config/archive.  
From Session/Gameplay: active session/encounter summaries, not every frame/action.  
From Chat: channel/message only for explicitly enabled campaign search/retention.

Search publishes no domain events. Operational `ProjectionRebuildStarted/Completed`,
`ProjectionPoisonEventQuarantined`, `ProjectionReconciliationMismatch` go to
telemetry/operator stream, not domain consumers.

## Public API

| Method/path | Query/body | Назначение |
|---|---|---|
| `GET /api/v1/search` | `q`, types[], campaignId?, rulesetVersionId?, locale, facets, cursor, limit | unified authorized search with typed hits |
| `GET /api/v1/search/suggestions` | `q`, types[], campaignId?, locale, limit ≤20 | low-latency autocomplete, no secret snippets |
| `GET /api/v1/discovery/rulesets` | q/tags/locale/sort/cursor | public ruleset catalog |
| `GET /api/v1/discovery/compendium-packs` | q/system/license/tags/locale/sort/cursor | public approved content |
| `GET /api/v1/dashboard` | optional workspace/campaign filters | user dashboard composite projection |
| `GET /api/v1/campaigns/{campaignId}/index-status` | authorized GM | lag/source versions/degraded projection families |

Typed search hit:

```json
{
  "type": "compendium-entry",
  "id": "...",
  "sourceVersion": 12,
  "title": "...",
  "snippet": "...",
  "highlights": [],
  "facets": {},
  "accessRevision": 8,
  "asOf": "2026-08-14T12:00:00Z"
}
```

Snippet generated from sanitized/indexable text and is itself audience-filtered.

## Internal/admin API

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /internal/v1/projections/{name}/rebuilds` | source range/tenant/shadow target | authorized operator async rebuild |
| `GET /internal/v1/projections/{name}/checkpoints` | partition | lag/last event/error |
| `POST /internal/v1/projections/{name}/reconcile` | sample/full scope | compare source versions/checksums |
| `POST /internal/v1/projections/{name}/poison/{eventId}:retry` | fixed handler version | retry quarantined event |

Admin API separate audience/network and immutable operator audit.

## Storage/read models

- PostgreSQL per projection family initially; OpenSearch alias/index at growth;
- inbox/checkpoints/poison queue/rebuild generations;
- no domain event store needed because projection is rebuildable; operational
  rebuild audit can be conventional append log;
- backups reduce recovery time, but canonical recovery comes from source events.

## Failure modes

- consumer lag: UI shows stale badge only where decision matters; gameplay unaffected;
- poison event: quarantine one event/aggregate partition, alert, continue safe
  partitions; owner access/takedown events may require fail-closed affected docs;
- index unavailable: recent/pinned resources from owning local projections;
- rebuild: shadow generation and atomic alias; commands never write to index.

## Key tests/SLI

- duplicate/out-of-order/gap/rebuild/reconciliation;
- every access transition removes/adds exactly scoped docs; cross-tenant fuzz;
- locale analyzer, facets, cursor stability and search relevance golden queries;
- quarantine/delete prioritization and cache invalidation;
- source unavailable during enrichment and recovery;
- SLI: search p95 ≤400 ms, suggestions p95 ≤150 ms, projection staleness p99
  ≤2 s, access/quarantine removal p99 ≤2 s, zero unauthorized hits/snippets.
