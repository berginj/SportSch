# Review implementation

This tracks implementation of the September 2026 repository review. Existing public-calendar and low-cost-stack proposals remain separate from this work.

## Accepted boundaries

- Authenticated, header-scoped calendar; no new public endpoints.
- Commit makes a reviewed draft visible; no separate publication stage.
- Full-season commits may pause booking changes in the affected league while retaining the previous complete read view.
- Commissioner scheduling overrides require recorded reasons; they never bypass authorization or stale-write protection.
- Preserve existing identifiers and canonical tables/partitions. Reconcile incompatible practice records explicitly, without a legacy read fallback.

## Work packages

- [ ] WP0: Integrated test/release harness and baseline telemetry.
- [ ] WP1: Tenant and authorization hardening.
- [ ] WP2: Canonical practice and nondestructive preview.
- [ ] WP3: Storage initialization, session feedback, pagination, accessibility.
- [ ] WP4: Durable worker, operation coordination, recovery, outbox.
- [ ] WP5: Persisted immutable drafts and exact commit.
- [ ] WP6: Unified booking transitions and rescheduling.
- [ ] WP7: Bounded calendar read model and projections.
- [ ] WP8: Calendar workspace and accessible workflows.
- [ ] WP9: Eastern time, exports, notification delivery.
- [ ] WP10: Scheduling domain/explanation separation and benchmarks.
- [ ] WP11: Recovery, readiness, action queue, operational dashboard.

## Verified implementation slices

The work packages above remain open until their full scope is delivered. These slices are implemented and verified:

- Tenant/header matching, strict role checks, request-scoped authorization caching, synchronous league selection, stale-response suppression, and server-side `If-Match` protection for schedule edits.
- Canonical field-inventory practice booking; retired incompatible practice routes; Eastern-time 72-hour validation; invalid/ambiguous DST times fail closed.
- Non-destructive wizard preview behavior in the UI; the old broad reset is no longer called by preview.
- Startup-only table provisioning, paginated slot reads, responsive calendar action placement, modal keyboard focus handling, loading/error telemetry, and correlation IDs.
- Conditional two-slot Table transaction for game rescheduling, idempotent operation marker/replay, division-aware coach authorization, and tests for stale replacement/concurrent safety.
- Local Azurite Table tests, seeded authenticated desktop/mobile Playwright tests, full CI lint/build/unit/backend/browser gates, and operational baseline documentation.
- Structured audit events now cover bulk access-request outcomes and schedule exports, grouped by league and correlated to the originating request.
- Membership administration now records correlated audit events for new memberships and role changes; scheduler tests verify external-offer distribution, season caps, and backward slot ordering.
- Bulk access operations retain the global request limiter and enforce a 250-item request bound.

The following remain deliberately open: persisted immutable wizard drafts, exact draft commit, cross-table operation journal/recovery, an outbox/worker for notifications, a bounded calendar projection, production latency baselines, and complete backup/restore readiness.

Packages are checked only when implemented and verified. Local checks do not establish production deployment, latency, delivery, or recovery guarantees.
