# Scheduling Contract Review (2026-03-05)

## Scope

Review target: `docs/SCHEDULING_ENGINE_BEHAVIORAL_CONTRACT.md`

Result: contract is now enforced in active wizard scheduling flow with legacy scheduler behavior deprecated at the endpoint/UI surface.

## Compliance Summary

| Contract Area | Status | Notes |
| --- | --- | --- |
| Single canonical engine | Enforced | Legacy `/api/schedule/preview|apply|validate` now return `410 SCHEDULER_DEPRECATED`; Scheduler tab removed from Manage page. |
| Regular back-to-front + priority | Enforced | Regular phase continues using backward scheduling with priority rank ordering. |
| Priority rank direction (`1` highest) | Enforced | Existing rank ordering remains ascending by rank number. |
| Weekly cap only in Regular | Enforced | Regular uses configured cap; Pool uses `null` cap; Bracket path does not apply weekly cap. |
| Valid game-field only in Regular | Enforced | Regular uses game-capable slot pool; Pool/Bracket may use broader pool. |
| Pool/Bracket prioritize game-capable slots | Enforced | Slot ordering favors game-capable over practice in pool/bracket ordering. |
| No-doubleheader policy | Enforced with stronger interpretation | Engine enforces no second game on same day (stricter than adjacent-only). |
| Guest recurrence exclusions | Enforced | Guest reservation excludes week 1 and bracket range; pool excluded by regular-window slot selection. |
| Guest anchors strict (no fallback) | Enforced | Reserved guest selection removed non-anchor fallback; secondary external assignment path also anchor-gated. |
| Guest slots first-class requirements | Enforced | Missing anchor config/coverage now explicitly warned; guest reservations remain locked in repair proposals. |
| Request games outside scheduling metrics | Enforced | Request games removed from seeded constraint counts, validation tracked-team metrics, and summary assigned/slot totals. |
| Bracket fixed top-4 model | Enforced | Existing fixed template retained; consolation remains optional/non-required. |
| Apply with warnings | Enforced | Wizard no longer blocks apply on hard-rule status; preview warns and apply remains available. |
| Full-run apply (no partial apply) | Enforced | Apply writes the generated run in one pass through wizard apply path. |

## Notes

- API test project currently has pre-existing compile errors unrelated to this enforcement pass (`api.Tests/SchedulingTests.cs` constructor signatures mismatch). Backend function project itself builds cleanly.
