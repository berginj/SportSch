# Scheduling Engine Behavioral Contract

## 1. Scope and Authority

This contract is the authoritative behavioral specification for scheduling.

- The system MUST use a single canonical scheduling engine based on the Wizard scheduler.
- Legacy scheduler behavior MUST be treated as deprecated and non-canonical.
- Any scheduling feature or endpoint that conflicts with this contract MUST be updated to conform.

## 2. Canonical Definitions

- `Regular Season`: primary league schedule window before Pool Play starts.
- `Pool Play`: postseason pool phase between regular season and bracket.
- `Championship Bracket`: fixed top-4 championship bracket phase.
- `Consolation Bracket`: optional and not required by this contract.
- `Game-capable slot`: slot type `Game` or `Both`.
- `Practice/non-game slot`: slot type `Practice`.
- `Guest slot`: recurring anchored slot reserved for a league team to host an external opponent.
- `Request game`: externally arranged extra game row (locked, informational to planner).

## 3. Scheduling Model

- The engine MUST schedule in three phases: Regular Season, Pool Play, Championship Bracket.
- Championship Bracket MUST use a fixed top-4 template.
- The engine MUST produce a full-season plan as one run; partial apply is not allowed.

## 4. Slot Priority and Ordering

- Slot priority rank MUST use `1` as highest priority.
- Regular Season assignment MUST schedule from back to front in time (later dates first), while respecting slot priority.
- Pool Play and Championship Bracket are not required to use back-to-front ordering.

## 5. Hard vs Soft Constraints by Phase

### 5.1 Regular Season

- Games MUST only be scheduled on game-capable slots (`Game` or `Both`).
- No-doubleheader MUST apply:
  - no second game for a team on the same day,
  - no adjacent back-to-back slots for a team.
- Weekly cap MUST apply based on configured max games per week.

### 5.2 Pool Play

- Practice/non-game slots MAY be used, but game-capable slots SHOULD be prioritized.
- No-doubleheader SHOULD apply, but unmet placement due to infeasibility is warning-level.
- Weekly cap MUST NOT be enforced as a hard requirement.

### 5.3 Championship Bracket

- Practice/non-game slots MAY be used, but game-capable slots SHOULD be prioritized.
- Teams are not final until near bracket execution; fields/times are provisional.
- Weekly cap MUST NOT be enforced.

## 6. Guest Game Rules

- Guest slots MUST be anchored to fixed weekly requirements.
- Anchors are strict requirements; fallback to non-anchor guest slots in the same week MUST NOT be used.
- Guest recurrence window MUST exclude:
  - week 1 of the season,
  - all Pool Play weeks,
  - all Championship Bracket weeks.
- A league home team MUST be assigned for each scheduled guest slot so an opponent exists.
- Guest assignment does not need to execute before all regular matchup placement, but anchored guest requirements remain mandatory.
- Guest games MUST count toward team game totals and no-doubleheader constraints.

## 7. Required Games and Counting Rules

- Regular Season minimum games per team are required targets.
- Pool Play minimum games per team are required targets but unmet pool targets are warning-level and do not block apply.
- Championship Bracket games MUST NOT count toward regular/pool required minimums.
- Request games are extra and currently outside scheduling count logic:
  - they MUST NOT affect required-game totals,
  - they MUST NOT affect weekly-cap calculations,
  - they MUST NOT affect no-doubleheader enforcement in schedule generation.

## 8. Fairness Rules

- Regular Season fairness target:
  - for seasons of 10-15 games per team, spread SHOULD be within +/-1 game,
  - for seasons above 15 games per team, spread MAY be up to +/-2 games.
- Pool Play fairness target SHOULD be within +/-1 game.
- Home/away balance SHOULD be reasonably even; imbalance is warning-level unless explicitly elevated by league policy.

## 9. Apply and Infeasibility Policy

- The engine MUST support apply with warnings, including hard-rule warning visibility.
- Apply MUST write the full generated run atomically (full season window for the run), or write nothing.
- The system MUST not silently apply a subset of the generated plan.
- Rule-health and issue reporting MUST clearly identify unresolved items before apply.

## 10. Season-Configurable Rule Inputs

The following controls remain valid and MUST be season-configurable inputs:

- no-games-on-date rules,
- no-games-before-time / no-games-after-time windows,
- max external offers per team.

## 11. Change Control

- Any behavior change to scheduling MUST update this contract in the same change set.
- UI, API, and scheduling engine behavior MUST remain aligned to this contract.
- If future policy changes conflict with this document, this document MUST be revised first and then enforced in code.
