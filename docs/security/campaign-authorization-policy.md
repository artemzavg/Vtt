# Campaign authorization policy

Дата review: 2026-08-21. Policy revision freshness target: p99 < 2 seconds.
Campaign — единственный владелец membership и coarse tenant authorization.

## Disclosure и trust boundary

- Edge получает subject только из активной opaque Identity session; browser не
  передаёт `userId`, role или capabilities.
- Edge выполняет CSRF и coarse prefilter. Campaign повторяет окончательную проверку
  по текущей membership projection и `policyRevision`.
- Неучастник получает одинаковый `404 campaign.not_found` для существующего и
  отсутствующего campaign id. Активный участник без capability получает `403`.
- Internal API требует отдельный key в development; production replacement —
  workload identity/mTLS и service audience.
- Любое неизвестное действие запрещено. Character/Scene relations не принадлежат
  Campaign и появятся в соответствующих bounded contexts.

## Role → capability matrix

| Capability | Owner | CoGM | Player | Observer |
|---|:---:|:---:|:---:|:---:|
| `campaign.read` | ✓ | ✓ | ✓ | ✓ |
| `campaign.update` | ✓ | ✓ | — | — |
| `campaign.activate` | ✓ | ✓ | — | — |
| `campaign.archive` / `campaign.restore` | ✓ | ✓ | — | — |
| `members.read` | ✓ | ✓ | ✓ | ✓ |
| `members.manage` | ✓ | ✓ | — | — |
| `invitations.manage` | ✓ | ✓ | — | — |
| `ownership.transfer` | ✓ | — | — | — |
| `gameplay.write` | ✓ | ✓ | ✓ | — |

Archived campaign дополнительно запрещает `gameplay.write` для всех ролей.

## Invitations и ownership

- Invite token содержит 256 бит entropy, хранится только как SHA-256 hash, raw
  значение возвращается ровно один раз и передаётся на accept в JSON body, а не URL.
- TTL: больше текущего времени и не более 30 дней; `maxUses`: 1–100; Owner invite
  запрещён. Accept выполняется serializable transaction с concurrency token.
- Invalid/expired/revoked/exhausted/replayed token имеет единый публичный ответ.
- Ровно один Owner. Его нельзя suspend/remove/demote обычной membership command.
  Transfer создаёт pending request на 48 часов; до target acceptance owner прежний,
  после acceptance old owner становится CoGM, target — единственным Owner.

## Invalidation и события

Любое authorization-affecting изменение увеличивает `policyRevision` и атомарно
пишет integration outbox/audit. Consumers принимают только revision больше текущей,
duplicate игнорируют, gap вызывает snapshot refresh. Critical cache miss/stale
revision fail-closed и использует `/internal/v1/authorization/check`; подписанный
snapshot доступен для rebuild. Suspend/remove/archive начинают действовать в
Campaign transaction немедленно, не дожидаясь Search.
