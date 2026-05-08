# Backlog

Consolidated from prior planning docs. Last updated: 2026-05-08.

---

## Feature Gaps

### Guest Game Auto-Balancer
- **Priority:** Medium
- **Status:** Foundation exists (balance validation + warnings), auto-balancing algorithm not implemented
- **Effort:** ~1 day
- Post-processing balancer in ScheduleEngine to redistribute guest games when spread > 1

### Smart Schedule Reminders
- **Priority:** Medium
- **Status:** Notification + email infrastructure exists, timer-based reminders not built
- **Effort:** ~2 days
- Timer-triggered function (24h and 2h before game), user preferences, deduplication table

---

## Code Quality & CI

### CI Gates
- Add GitHub Actions workflow for contract-sensitive backend tests on PR
- Add default frontend test suite to CI pipeline

### Integration Test Failures
- 7 pre-existing integration test failures (environment/mocking issues, not code bugs)
- Investigate and fix or document as known

### OpenAPI Coverage
- Scan all Functions for missing `[OpenApiOperation]` attributes
- Ensure 100% Swagger UI coverage

### State Transition Validation
- `SlotStatusFunctions.UpdateSlotStatus` allows invalid transitions (e.g., Cancelled → Confirmed)
- Add `IsValidTransition()` guard

### Skip Completed/Postponed Slots in Conflict Checks
- Currently only Cancelled slots are skipped; Completed and Postponed should also be excluded

### Confirmed Slot Requires ConfirmedTeamId
- Admin can set status=Confirmed without ConfirmedTeamId; add validation guard

---

## Contract / Documentation Updates

### Slot Lifecycle Contract
- Add atomicity guarantee to section 6.2 (slot updated BEFORE request created)
- Add team conflict validation to section 6.6 (UpdateSlot)

### Practice Contract
- Update lead-time policy from 48h to 72h
- Document LEAD_TIME_VIOLATION error code

### Main Contract (docs/contract.md)
- Add "Lead time policies" section
- Expand error codes list (FIELD_INACTIVE, LEAD_TIME_VIOLATION, deprecate UNAUTHORIZED)

### Best-Effort Denial Documentation
- Document that other pending requests are denied best-effort after slot confirmation
- Orphaned pending requests are acceptable (slot status is source of truth)

### Midnight Boundary
- Document game start/end same-day constraint in contracts (partially done)

---

## Security Hardening

### Medium Term (1-3 months)
- **Key Vault migration** — move secrets from env vars to Azure Key Vault (2-3 days)
- **Input length validation** — add max length constants and validation to all endpoints (1 day)
- **Global admin privilege review** — granular permissions, audit logging (2-3 days)
- **Session timeouts** — configure EasyAuth TTL + client-side session monitoring (1 day)

### Long Term (3-6 months)
- **Production error verbosity** — sanitize error messages in production responses (1 day)
- **CSRF tokens** — defense-in-depth beyond EasyAuth SameSite cookies (2 days)
- **Timing attack fix** — constant-time comparison in ApiKeyService (1 hour)
- **CSP hardening** — remove unsafe-inline, use nonces (1-2 days)
- **Log sanitization** — prevent log injection, truncate long inputs (1 day)

---

## Polish & Low Priority

- Remove deprecated `UNAUTHORIZED` constant (breaking change — plan for major version)
- Add `aria-busy` to remaining loading buttons (admin/debug pages)
- Guest slot counting verification test
- Back-to-front scheduling order verification test
- Orphaned request cleanup job (daily timer function)
- Rate-limit bulk operations (bulk approve/deny, imports)
- Enhance audit logging (role changes, bulk ops, exports)
- E2E test expansion (slot management, team/league, schedule generation)
- Backend function refactoring (service layer pattern for remaining 36+ functions)
- Component extraction (4 AdminPage sections)
- Custom hooks extraction (useCoachAssignments, useGlobalAdminData, useCsvImport)
- Application Insights dashboards and alerts

---

## Production Deployment Checklist

- [ ] Verify CORS origins match production domains
- [ ] Confirm auth configuration (AAD/Google)
- [ ] Confirm storage and App Insights wiring
- [ ] Document rollback procedure
- [ ] Set up monitoring alerts
