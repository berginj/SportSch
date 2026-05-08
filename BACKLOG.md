# Backlog

Consolidated from prior planning docs. Last updated: 2026-05-08.

---

## Feature Gaps

### Guest Game Auto-Balancer
- **Priority:** Medium
- **Status:** Foundation exists (balance validation + warnings), auto-balancing algorithm not implemented
- **Effort:** ~1 day
- Post-processing balancer in ScheduleEngine to redistribute guest games when spread > 1

### ~~Smart Schedule Reminders~~ ✅
- Already implemented: `GameReminderFunction.cs` with 24h/2h windows, deduplication, preference checking

---

## Code Quality & CI

### ~~CI Gates~~ ✅
- Done: `.github/workflows/pr-tests.yml` runs all backend tests + frontend lint + test:ci on PR/push to main

### ~~Integration Test Failures~~ ✅
- No integration tests exist (only `IntegrationTestBase.cs` helper with no test methods)
- All 190 unit tests pass

### ~~OpenAPI Coverage~~ ✅
- Added `[OpenApiOperation]` to 30 functions across 12 files
- Most user-facing endpoints now have Swagger discovery attributes

### ~~State Transition Validation~~ ✅
- Already implemented: `IsValidStatusTransition()` in SlotStatusFunctions.cs

### ~~Skip Completed/Postponed Slots in Conflict Checks~~ ✅
- Already implemented: SlotRepository.cs skips Cancelled, Completed, and Postponed

### ~~Confirmed Slot Requires ConfirmedTeamId~~ ✅
- Already implemented: SlotStatusFunctions.cs validates ConfirmedTeamId presence

---

## Security Hardening

### ~~Timing attack fix~~ ✅
- Done: ApiKeyService uses `CryptographicOperations.FixedTimeEquals`

### ~~Input length validation~~ ✅
- Done: `ApiGuards.InputLimits` + `EnsureMaxLength()` added, applied to slot creation and reschedule

### Medium Term (1-3 months)
- **Key Vault migration** — move secrets from env vars to Azure Key Vault (2-3 days)
- **Global admin privilege review** — granular permissions, audit logging (2-3 days)
- **Session timeouts** — configure EasyAuth TTL + client-side session monitoring (1 day)

### Long Term (3-6 months)
- **Production error verbosity** — sanitize error messages in production responses (1 day)
- **CSRF tokens** — defense-in-depth beyond EasyAuth SameSite cookies (2 days)
- **CSP hardening** — remove unsafe-inline, use nonces (1-2 days)
- **Log sanitization** — prevent log injection, truncate long inputs (1 day)

---

## Contract / Documentation Updates

### ~~All contract updates~~ ✅
- Slot lifecycle atomicity guarantee (section 6.2) — already documented
- Team conflict validation (section 6.6) — already documented
- Practice lead-time 72h — already documented
- Error codes expansion — already documented in docs/contract.md

---

## Polish & Low Priority

- ~~Remove deprecated `UNAUTHORIZED` constant~~ ✅ Already deprecated with `[Obsolete]` and `@deprecated`
- ~~Add `aria-busy` to loading buttons~~ ✅ Added across 5 files (8 buttons)
- ~~Component extraction (AdminPage sections)~~ ✅ Already extracted to `src/pages/admin/` (7 sections)
- Guest slot counting verification test
- Back-to-front scheduling order verification test
- Orphaned request cleanup job (daily timer function)
- Rate-limit bulk operations (bulk approve/deny, imports)
- Enhance audit logging (role changes, bulk ops, exports)
- E2E test expansion (slot management, team/league, schedule generation)
- Custom hooks extraction (useCoachAssignments, useGlobalAdminData, useCsvImport)
- Application Insights dashboards and alerts

---

## Production Deployment Checklist

- [ ] Verify CORS origins match production domains
- [ ] Confirm auth configuration (AAD/Google)
- [ ] Confirm storage and App Insights wiring
- [ ] Document rollback procedure
- [ ] Set up monitoring alerts
