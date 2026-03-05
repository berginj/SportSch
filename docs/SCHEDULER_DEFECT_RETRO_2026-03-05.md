# Scheduler Wizard Defect Retro (March 5, 2026)

## Scope
Review window: ~last 4 hours of scheduler work on `main`.

Primary commits reviewed:
- `723e1aa` Honor availability slot types in wizard scheduling
- `0040e4e` Restrict pool and bracket placement to game-capable slots
- `8ae431a` Cap guest assignments to team target and add single-missing apply override
- `e92c1ae` Make wizard apply atomic by removing client-side pre-reset

## What Failed

1. Game placement used wrong slot types
- Symptom: games were scheduled on days/slots that should not have been game-capable.
- Root cause: slot type/priority handling drifted in backend (`LoadAvailabilitySlotsAsync` + fallback behavior), and pool/bracket were using `filteredAllSlots` instead of `gameCapableSlots`.
- Fix: derive slot type/priority from allocation fields and enforce `gameCapableSlots` across all game phases.

2. Guest games inflated per-team totals beyond target
- Symptom: team projections showed extreme totals (example: ~19 when target was 14 total season games).
- Root cause: external/guest placement was additive without a hard cap tied to regular-season team target.
- Fix: guest assignment picker now enforces `maxTotalGamesPerTeam` for regular season, derived from `minGamesPerTeam`.

3. Apply looked like it didn’t run
- Symptom: clicking Apply in wizard appeared to do nothing / unclear outcome.
- Root cause: client-side pre-reset ran before apply, cleared/changed state, and made apply non-atomic from the user’s perspective.
- Fix: removed client pre-reset from apply path; apply now uses one backend call (backend handles reset+apply in one flow).

4. Hard-block override needed a precise boundary
- Symptom: legitimate case of exactly one missing required matchup needed an explicit acknowledge-and-apply path.
- Root cause: all hard violations were treated identically at apply boundary.
- Fix: narrow override only for one hard group: `unscheduled-required-matchups` with count `1`, behind explicit payload/confirmation.

## New Guardrails Added

### Runtime invariants (backend)
`api/Functions/ScheduleWizardFunctions.cs`
- Added wizard invariant checks that emit hard issues and block apply when violated:
  - `non-game-slot-assignment`: any non-request assignment not on a game-capable slot.
  - `regular-team-target-overflow`: any team above regular-season target (`minGamesPerTeam`).

### Regression tests (frontend)
`src/__tests__/manage/SeasonWizard.test.jsx`
- Added/updated tests to enforce:
  - apply override only for single missing required matchup.
  - apply remains blocked for other hard-rule sets.
  - apply does not trigger additional client-side reset calls.

### UX/apply behavior
`src/manage/SeasonWizard.jsx`
- Apply button state and messaging now reflect:
  - fully blocked hard-rule cases,
  - explicit single-missing-matchup override case.

## Going-Forward Quality Checks

Use this mini-checklist before merging scheduler changes:

1. Slot integrity
- Verify all game phases (regular/pool/bracket) source slots from `gameCapableSlots`.
- Verify no code path hardcodes availability slot type to `"game"` unless explicitly intended.

2. Team target integrity
- If guest/external logic is touched, confirm regular-season team totals cannot exceed target.
- Re-run preview for odd-team case (9 teams, 2 guest/week, 2 games/week cap).

3. Apply integrity
- Apply must be atomic from UI perspective (single apply request path).
- Any pre-apply cleanup must remain backend-owned in apply endpoint.

4. Hard-rule override integrity
- Override logic must stay narrow and explicit.
- No broad “ignore hard rules” path.

## Recommended CI Gate (next pass)

Add a dedicated scheduler guard job that runs on PR:
- `dotnet test api/Tests/GameSwap_Functions.Tests.csproj`
- `dotnet test api/GameSwap.Tests/GameSwap.Tests.csproj --filter "FullyQualifiedName~Schedule"`
- `npm run test -- src/__tests__/manage/SeasonWizard.test.jsx --run`

This keeps scheduler regressions visible before deploy.
