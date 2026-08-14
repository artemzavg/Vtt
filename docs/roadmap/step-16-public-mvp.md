# Шаг 16. Публичный MVP: production readiness и 1 000 CCU

Статус: `Planned`  
Зависимость: шаг 15  
Результат: продукт доступен публичным пользователям в одном регионе, выдерживает
1 000 CCU с 30% headroom, имеет измеряемый SLO, восстановление, privacy/security,
accessible/mobile core UX и управляемую эксплуатацию.

## Затрагиваемые сервисы

Все сервисы и production infrastructure. Основные feature additions затрагивают
Identity, Campaign, Scene, Session, Compendium, Media, Edge/React и operations.

## Разрабатываемые возможности

- production multi-AZ/safe single-region deployment and autoscaling;
- passkeys/TOTP, external OIDC provider optional, account export/deactivation;
- guest/observer join с TTL/ограниченными правами;
- multiple scenes/folders/navigation, ruler/templates/drawings/ping and robust undo;
- private campaign homebrew draft minimum;
- PWA app shell, immutable offline read cache, low-bandwidth mode;
- responsive tablet/mobile character/chat/basic scene controls;
- WCAG 2.2 AA critical flows, RU/EN UI baseline;
- quotas/abuse/report/takedown minimum, WAF/DDoS/rate limits;
- SLO/error-budget/status page/on-call/incident process;
- PITR/multi-AZ/backups/key rotation/disaster drill;
- 1 000 CCU/150–200 rooms +30%, canary and capacity dashboards;
- public terms/privacy/content attribution/support documentation.

## Конкретный план реализации

1. Freeze MVP feature/region/data-residency/business/free-tier policy.
2. Provision production via IaC: network, secrets/KMS, DB HA/PITR, NATS/Redis,
   object storage/CDN/WAF, autoscaling, observability retention.
3. Implement guest/observer admission, quotas and abuse controls.
4. Complete MFA/passkeys/session security/account data lifecycle.
5. Finish multi-scene GM tools and private homebrew authoring/validation.
6. Build PWA/cache/reconnect/low-bandwidth; no offline authoritative writes beyond
   queued idempotent commands with explicit pending state.
7. Accessibility/localization/device audit and fixes.
8. Privacy/security/legal review, DAST/pentest, subprocessor/data retention docs.
9. Run full load/soak/chaos/failover/restore/key rotation/canary drills.
10. Limited public rollout: internal → 5% → 25% → 100% with error-budget gates.

## Definition of Ready

- [ ] alpha metrics prove core journey stable over agreed observation window;
- [ ] public MVP scope, target region and data residency/legal basis approved;
- [ ] production budget/capacity and support/on-call staffing approved;
- [ ] SLI definitions and proposed SLO/SLA wording reviewed;
- [ ] quotas/retention/moderation/guest/MFA policies approved;
- [ ] WCAG device/browser matrix and localization glossary ready;
- [ ] pentest vendor/scope or equivalent independent security review scheduled;
- [ ] incident communication/status page/rollback authority assigned.

## Подробный план ручного тестирования

| ID | Действия | Ожидаемый результат |
|---|---|---|
| M16-01 | Register/login/passkey/TOTP/recovery/revoke flows on production-like | Factors/recovery/session rotation secure, accessible, audited |
| M16-02 | Guest join link expire/revoke/role escalation attempts | TTL/capabilities enforced, guest cannot own/edit forbidden resources |
| M16-03 | GM manages multiple scenes/folders/templates/ruler/draw/ping/undo | Tools persist/realtime correctly; undo conflicts safe |
| M16-04 | Author private homebrew item/feature and use in builder | Validated/versioned/campaign-only, provenance visible, no public leak |
| M16-05 | Install PWA, go offline/read cached sheet, reconnect pending command | Read cache safe; pending explicit/idempotent; no stale unauthorized access after revoke |
| M16-06 | Use core flows keyboard/screen reader/reduced motion/high contrast | WCAG critical criteria and non-color cues pass |
| M16-07 | Use phone/tablet/desktop and throttled/packet-loss networks | Supported core flows usable, quality/low-bandwidth graceful |
| M16-08 | Account export/deactivation and retention workflow | Export scoped; sessions revoked; dependent-data state transparent/audited |
| M16-09 | Report/quarantine test public/private content/asset | Abuse workflow works, unauthorized content distribution stops within SLO |
| M16-10 | Rotate signing/encryption/object/CDN keys | Rolling overlap works; clients recover; no mass logout/data exposure beyond policy |
| M16-11 | Fail one app/realtime/data node/AZ component | Availability/degraded/reconnect meets target, no committed data loss |
| M16-12 | Restore PITR/object data to isolated environment | Production RPO/RTO and checksum targets met |
| M16-13 | Run 1 000 CCU +30%, hot room and reconnect storm | SLO/headroom/DB/NATS/Redis/CDN cost and correctness pass |
| M16-14 | Execute canary bad-build rollback and feature kill switch | Automatic/manual rollback safe, schema compatible, core restored quickly |
| M16-15 | Verify status page/support correlation during injected incident | User impact clear, no private data, support can diagnose/run runbook |

## Definition of Done

- [ ] production IaC/multi-AZ/backups/PITR/secret rotation operational;
- [ ] 1 000 CCU +30% load, soak, chaos and hot-room tests passed;
- [ ] published SLO dashboards/error-budget/on-call/status page active;
- [ ] independent security review/pentest P0/P1 closed;
- [ ] WCAG 2.2 AA critical flows and supported device matrix pass;
- [ ] guest/MFA/PWA/low-bandwidth/multi-scene/private homebrew complete;
- [ ] privacy/terms/attribution/retention/export/deactivation docs approved;
- [ ] canary/rollback/kill switch/DR drills passed;
- [ ] M16-01…M16-15 accepted with no P0/P1 defects.

## Критический check перед завершением

- Имеет ли production реальную failover capacity, а не только HPA на бумаге?
- Измеряет ли availability user journey, а не pod uptime?
- Остаются ли revoked/private data в PWA/CDN/search cache?
- Реалистичны ли on-call/support/budget при 1 000 CCU?
- Выполнены ли legal/data localization obligations для выбранного региона?
- Не обещан ли договорной SLA до достаточной history/error budget evidence?

`NO-GO`: failed pentest high/critical, untested restore/failover, private offline
cache leakage, load without 30% headroom, inaccessible critical flow, no incident
owner/legal approval.

Evidence: production readiness review, pentest closure, WCAG report, capacity/cost
report, DR/key rotation/canary transcripts, legal/privacy sign-offs.

## Вне scope

Marketplace, public creator ecosystem, built-in voice/video, second full ruleset,
plugin runtime, multi-region active-active and contractual enterprise SLA.
