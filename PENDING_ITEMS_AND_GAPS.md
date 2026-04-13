# Pending Items and Gaps

Status: operational backlog, not a release sign-off.

Last re-baselined: 2026-04-12

This file replaces older "production ready" claims with a verified baseline and a shorter list of work that still needs deliberate follow-through.

## Verified Baseline

The following were verified or corrected on 2026-04-12:

- `GET /api/slots` now returns `confirmedTeamId` in the canonical slot payload.
- Reminder and admin-cancellation automation now fall back to `confirmedTeamId` when `awayTeamId` is blank.
- Confirmed-game UI surfaces now render opponents from `awayTeamId || confirmedTeamId`.
- `vitest` now excludes `_cleanup_backup/**`, so archived trees no longer pollute the active test graph.
- Persona and backlog docs are now explicitly aligned to the authenticated, header-scoped contract.

## Test Baseline

Verified commands on 2026-04-12:

- Backend targeted contract tests:
  - `dotnet test api/GameSwap.Tests/GameSwap.Tests.csproj --filter "FullyQualifiedName~RequestServiceTests|FullyQualifiedName~GameReminderFunctionTests|FullyQualifiedName~SlotStatusFunctionsTests"`
- Frontend page regression tests:
  - `npm run test -- --run --exclude _cleanup_backup/** src/__tests__/pages/CalendarPage.test.jsx src/__tests__/pages/OffersPage.test.jsx src/__tests__/pages/CoachDashboard.test.jsx src/__tests__/pages/CoachOnboardingPage.test.jsx`
- Frontend default suite:
  - `npm run test -- --run`

Current verified result:

- Default Vitest run passed: 29 files, 165 tests.
- Targeted backend regression run passed for the confirmed-slot contract and automation path.

## Remaining High-Priority Work

These are still the next serious items, but they are operational hardening tasks rather than contract-drift blockers:

1. Add CI gates for the contract-sensitive backend tests and the default frontend suite.
2. Re-verify production deployment configuration:
   - real CORS origins
   - auth configuration
   - storage/app insights wiring
3. Tighten release evidence:
   - explicit deployment checklist
   - alerting/monitoring ownership
   - rollback and incident notes
4. Finish API documentation coverage where it is still incomplete.

## Medium-Priority Follow-Ons

- Larger refactors in oversized backend/frontend modules
- Additional notification work beyond the current reminder/cancellation paths
- Broader E2E coverage for cross-role workflows
- Operational rate-limiting improvements where bulk admin flows still rely on current defaults

## Things This File Should Not Claim

Do not reintroduce blanket statements like these without fresh evidence:

- "Production ready"
- "All critical items resolved"
- "300+ tests, 100% passing"
- "No critical gaps found"

Those claims were previously broader than the verified state of the repo.

## Recent Corrections Closed

Completed on 2026-04-12:

1. Exposed `confirmedTeamId` through the slot DTO boundary.
2. Updated slot automation to honor immediate-confirm opponents.
3. Fixed confirmed-opponent rendering across calendar and coach-facing UI surfaces.
4. Removed archived cleanup trees from the default Vitest discovery path.
5. Re-baselined persona and backlog docs to the actual contract.

## Next Session Starting Point

If work resumes from here, start with:

1. CI enforcement for the verified frontend/backend test commands.
2. Production config audit with exact deployment values.
3. OpenAPI/documentation completeness review.
