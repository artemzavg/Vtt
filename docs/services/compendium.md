# Compendium Service

## Bounded context

Владеет каталогами игрового контента и provenance: packs, entries, drafts,
published versions, локализации, license/attribution, campaign/homebrew visibility,
moderation state и structured mechanics documents conforming to a Ruleset contract.
Не вычисляет sheet/gameplay result и не хранит character-owned copies.

## Aggregates and entities

### `CompendiumPack` aggregate

- pack identity, owner/publisher, ruleset/version compatibility;
- visibility `Private|Campaign|Unlisted|Public`;
- license manifest, attribution, source URLs, allowed locales;
- maintainers, draft/version list, moderation state.

### `PackDraft` aggregate

- base version, entry revisions, dependency pins;
- validation/moderation report, import operation, publish candidate hash.

### `CompendiumEntry` versioned entity/document

- stable entry id + immutable version id;
- type (`class`, `species`, `background`, `feature`, `item`, `spell`, `creature`,
  `condition`, `action`, `rule`, `rollTable`, `journalTemplate`, custom type);
- localized name/description structured content;
- mechanics payload conforming to Ruleset content schema;
- tags/facets, source/license/provenance, media refs;
- dependencies/replaces/deprecated-by refs;
- visibility and moderation/takedown state inherited/overridden by policy.

### `EntryReference`

Always includes `packVersionId`, `entryId`, `entryVersion/hash`; a floating latest
ref допустим только в authoring UI, не в published character/gameplay snapshot.

## Invariants

- published pack/entry immutable; исправление создаёт новую version;
- every published entry has license/provenance and ruleset contract validation;
- dependency refs exact and accessible at publish time;
- private/campaign content never appears in public search/cache keys;
- takedown hides distribution but does not corrupt existing audit; legal policy
  decides whether existing sessions retain encrypted snapshot/reference;
- media asset must be `Ready` and audience-compatible;
- localized mechanics cannot differ silently; mechanics common, text variants
  explicit unless ruleset declares locale-specific content version.

## Domain events

- `CompendiumPackCreated`, `PackMaintainerChanged`, `PackVisibilityChanged`;
- `PackDraftCreated`, `EntryDraftAdded/Replaced/Removed`;
- `PackDependencyPinned`, `LicenseManifestChanged`;
- `PackValidationSucceeded/Failed`, `PackSubmittedForModeration`;
- `PackVersionPublished`, `EntryDeprecated`;
- `ContentReported`, `ContentQuarantined/Restored`, `TakedownApplied`;
- `PackImportStarted/Completed/Failed`.

## Integration events

- `CompendiumPackPublished.v1 { packId, packVersionId, rulesetRefs, hash,
  visibility, localeSet, licenseSummary }`;
- `CompendiumEntryPublished.v1 { entryId, entryVersionId, packVersionId, type,
  rulesetRef, mechanicsHash, searchSummary }`;
- `CompendiumAccessChanged.v1 { packId, visibility, campaignIds?, revision }`;
- `CompendiumContentQuarantined.v1 { entryOrPackId, reasonCode, effectiveAt }`;
- `CompendiumEntryDeprecated.v1 { entryVersionId, replacement? }`.

## Связи

- Ruleset supplies content JSON schemas/contracts and compiled semantics.
- Media supplies ready asset refs and quarantine events.
- Campaign allowlists packs and provides campaign-scope access.
- Character references immutable entries for build/inventory/spells and snapshots
  mechanics needed for reproducibility.
- Gameplay consumes mechanics projections, not arbitrary HTML descriptions.
- Search indexes authorized public/campaign documents from events.

## Public API

### Read/catalog

| Method/path | Query/params | Назначение |
|---|---|---|
| `GET /api/v1/compendium/packs` | `q`, rulesetVersionId, visibility, locale, owner, cursor | pack catalog scoped to principal |
| `GET /api/v1/compendium/packs/{packId}` | none | metadata, versions, license/access |
| `GET /api/v1/compendium/pack-versions/{versionId}` | none | immutable manifest/dependencies/hash |
| `GET /api/v1/compendium/entries/{entryVersionId}` | `locale`, optional campaignId | authorized immutable entry |
| `GET /api/v1/compendium/search` | `q`, types[], tags[], levels[], sources[], rulesetVersionId, campaignId, locale, cursor | filtered search; result explains source/version |
| `POST /api/v1/compendium/entries:batch-get` | max 100 exact refs | efficient sheet/scene hydration preserving order/errors |
| `GET /api/v1/compendium/entries/{id}/related` | relation type, cursor | dependencies/upgrades/similar structured refs |

### Authoring

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/compendium/packs` | name, rulesetRefs, visibility, license manifest | create pack |
| `PATCH /api/v1/compendium/packs/{id}` | metadata/access; `If-Match` | change shell policy |
| `POST /api/v1/compendium/packs/{id}/drafts` | optional baseVersionId | authoring draft |
| `PUT /api/v1/compendium/drafts/{draftId}/entries/{entryId}` | type, localized content, mechanics, sources, media; `If-Match` | add/replace draft entry |
| `DELETE /api/v1/compendium/drafts/{draftId}/entries/{entryId}` | `If-Match` | remove from draft |
| `POST /api/v1/compendium/drafts/{draftId}:validate` | profile | async schema/dependency/license validation |
| `POST /api/v1/compendium/drafts/{draftId}:publish` | semanticVersion, changelog, report hash | immutable publish; moderation if public |
| `POST /api/v1/compendium/imports` | uploaded asset id, format, target pack/draft, conflict policy | async quarantined import |
| `GET /api/v1/compendium/imports/{operationId}` | none | row-level validation/errors |
| `GET /api/v1/compendium/pack-versions/{id}/export` | `format=vtt-pack-v1`, locale mode | signed export artifact, license included |
| `POST /api/v1/compendium/entries/{id}/reports` | category, details, evidence refs | moderation report; no public accusation |

## D&D content policy

- SRD 5.1 and SRD 5.2.1 are different first-party open packs with exact source
  document/version and CC-BY-4.0 attribution.
- Content absent from SRD is not copied from commercial books without license.
- User-entered private notes/content remains subject to terms; public publishing
  requires rights declaration and moderation controls.
- Trademarked branding/art is a separate asset/license question from mechanics.
- Export always bundles required attribution and license manifest.

Official reference: [D&D SRD page](https://www.dndbeyond.com/srd). It explicitly
states SRD 5.2 versions remain under CC-BY-4.0 and VTTs may use SRD 5.1 content;
proper attribution remains required. Legal review decides product wording.

## Search/read models

- `PackCatalog`, `EntryByExactVersion`, `EntryFacetDocument`, `EntryDependency`,
  `LicenseAudit`, `ModerationQueue`;
- PostgreSQL FTS first; Search service builds cross-context index;
- description sanitized into display AST, not raw executable HTML;
- cache key includes entry version, locale and authorization audience revision.

## Key tests/SLI

- exact-version immutability, draft conflict, dependency cycle/missing refs;
- ruleset schema/golden mechanics validation;
- private/campaign/public authorization and cache leakage;
- license/attribution always present in publish/export;
- malicious import, oversized fields, HTML/XSS, quarantined media;
- search relevance golden queries and stale/quarantine removal;
- SLI: exact entry p95 <150 ms cached, search p95 <400 ms, publish validation
  operation reliability, access propagation p99 <2 s.
