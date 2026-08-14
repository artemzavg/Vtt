# Campaign Service

## Bounded context

Владеет кампанией как collaboration/security boundary: metadata, lifecycle,
memberships, roles, invitations, campaign policy, ruleset pin, quotas plan references
и relation-based permissions. Не владеет персонажами, сценами, сообщениями или
ruleset definitions.

## Aggregates and entities

### `Campaign` aggregate

- id, owner, name/description, locale/timezone;
- status `Draft|Active|Archived|DeletionPending|Deleted`;
- pinned `RulesetVersionRef`, allowed content/homebrew pack refs;
- settings: automation level, dice policy, guest policy, default ACL templates;
- active session/scene references as hints updated by events;
- policy revision and quota/subscription reference.

Invariants:

- ровно один owner; ownership transfer explicit and accepted;
- campaign не активируется без valid pinned ruleset;
- archived campaign rejects gameplay writes, but readable by policy;
- owner deletion has cooling-off and dependency report;
- policy revision increments on any authorization-affecting change.

### `Membership` aggregate (per campaign/user or guest)

- role set (`Owner`, `CoGM`, `Player`, `Observer`), status, display projection;
- capability overrides and selected character references;
- invited/accepted/suspended/left timestamps;
- `GuestSubject` has expiry and cannot own content.

### `Invitation` aggregate

- creator, campaign, role template, token hash, expiry, usage limit/count;
- optional email/domain restriction, requires account or guest allowed;
- state `Active|Exhausted|Expired|Revoked`.

### `AccessPolicy` entities/value objects

Relations: campaign→member; resource→owner/editor/controller/viewer/audience.
Campaign stores default/policy relations and publishes revisions; object-specific
relations can be owned by Character/Scene while following the same policy schema.

## Domain events

- `CampaignCreated/Activated/Archived/Restored`;
- `CampaignDeletionRequested/Cancelled/Completed`;
- `CampaignOwnershipTransferRequested/Accepted`;
- `CampaignRulesetPinned`, `CampaignSettingsChanged`;
- `ContentPackAllowed/Removed`;
- `InvitationCreated/Used/Revoked/Expired`;
- `MemberInvited/Joined/RoleChanged/Suspended/Left/Removed`;
- `GuestJoined/Expired`;
- `CampaignPolicyChanged`, `QuotaPlanReferenceChanged`.

## Integration events

- `CampaignCreated.v1 { campaignId, ownerId, rulesetRef, policyRevision }`;
- `CampaignLifecycleChanged.v1 { campaignId, status }`;
- `CampaignMembershipChanged.v1 { campaignId, subjectId, effectiveCapabilities,
  status, policyRevision }`;
- `CampaignPolicyRevisionChanged.v1 { campaignId, policyRevision }`;
- `CampaignRulesetPinned.v1 { campaignId, oldRef?, newRef, migrationPlanId? }`;
- `CampaignContentSetChanged.v1 { campaignId, contentSetRevision }`.

## Связи

- Identity: user lifecycle/display revision.
- Ruleset: validate available version/migration preview; no copy of DSL.
- Compendium: validate pack refs/licenses/access.
- Character/Scene/Chat/Gameplay: consume membership/policy and lifecycle.
- Session: asks for effective join capabilities or uses current projection.
- Search: public/discoverable campaign metadata only when explicitly enabled.

Cross-context creation uses `CampaignCreated` consumers; partial dependent setup
reported by operation, campaign itself remains valid.

## Public API

| Method/path | Body/query | Назначение |
|---|---|---|
| `POST /api/v1/campaigns` | `name`, `locale`, `timezone`, `rulesetVersionId`, optional template | создать draft; caller owner |
| `GET /api/v1/campaigns` | `status`, `role`, cursor/limit | campaigns available to principal |
| `GET /api/v1/campaigns/{campaignId}` | none | metadata/settings/effective capabilities/version |
| `PATCH /api/v1/campaigns/{campaignId}` | name/description/locale; `If-Match` | изменить metadata |
| `POST /api/v1/campaigns/{id}:activate` | `If-Match` | activate after readiness validation |
| `POST /api/v1/campaigns/{id}:archive` | reason optional; `If-Match` | archive, stop new sessions |
| `POST /api/v1/campaigns/{id}:restore` | `If-Match` | restore within retention |
| `POST /api/v1/campaigns/{id}/deletion` | confirmation; step-up | schedule deletion |
| `DELETE /api/v1/campaigns/{id}/deletion` | none | cancel during cooling-off |
| `GET /api/v1/campaigns/{id}/members` | role/status/cursor | authorized member list |
| `PATCH /api/v1/campaigns/{id}/members/{memberId}` | `roles`, capability overrides; `If-Match` | change role/access |
| `DELETE /api/v1/campaigns/{id}/members/{memberId}` | reason; `If-Match` | remove; owner invariants |
| `POST /api/v1/campaigns/{id}/invitations` | role, expiresAt, maxUses, guestAllowed, email restriction | create opaque invite; token shown once |
| `GET /api/v1/campaigns/{id}/invitations` | state/cursor | invitation metadata, never token |
| `DELETE /api/v1/campaigns/{id}/invitations/{inviteId}` | `If-Match` | revoke |
| `POST /api/v1/invitations/{token}:accept` | optional displayName/activeCharacterId | atomically consume and join |
| `GET /api/v1/campaigns/{id}/permissions/effective` | `resourceType`, `resourceId`, optional subject | policy explanation for authorized caller |
| `PUT /api/v1/campaigns/{id}/settings` | automation/dice/guest/default ACL; `If-Match` | replace versioned settings |
| `POST /api/v1/campaigns/{id}/ruleset-migrations:preview` | target version | async compatibility plan |
| `POST /api/v1/campaigns/{id}/ruleset-migrations/{planId}:approve` | plan hash; `If-Match` | pin target and start dependent drafts |
| `PUT /api/v1/campaigns/{id}/content-packs/{packVersionId}` | none; `If-Match` | allow immutable pack |
| `DELETE /api/v1/campaigns/{id}/content-packs/{packVersionId}` | `If-Match` | remove if dependencies allow |

## Internal authorization API

| Method/path | Параметры | Назначение |
|---|---|---|
| `POST /internal/v1/authorization/check` | subject, campaign, actions/resources, knownRevision | bounded batch decision + revision |
| `GET /internal/v1/campaigns/{id}/policy-snapshot` | service audience | signed/hashed current policy projection |

High-frequency services должны consume policy events. Internal check — cache miss/
critical fallback с short deadline, не вызов для каждого frame.

## Storage/read models

- event streams Campaign/Membership/Invitation;
- `CampaignSummaryByUser`, `MemberDirectory`, `EffectiveCapability`,
  `ActiveInvitation`, `CampaignDependencySummary`;
- invitation token strong hash, never logs/events;
- policy snapshots signed/hash-versioned and short cached.

## Key tests/SLI

- last owner cannot leave/remove self; concurrent invitation use maxUses;
- revoked/stale invite and membership revision invalidates join;
- exhaustive role×resource×action policy matrix and cross-tenant ids;
- ruleset migration races and archive/session behavior;
- projection rebuild gives same capabilities;
- SLI: membership command p95, authz cache freshness p99 <2 s, invitation accept
  correctness, policy propagation to realtime.
