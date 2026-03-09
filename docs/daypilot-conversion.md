# DayPilot Conversion

## Purpose

This note tracks the DayPilot Lite rollout across SportsCH so calendar work stays focused on actual calendar surfaces instead of every screen that happens to list dated rows.

## Current State

Completed:

- `src/pages/CalendarPage.jsx`
  - DayPilot Lite is now the primary alternate calendar visualization via `src/components/CalendarView.jsx`
  - supports timeline and month views
  - preserves slot/event actions through the shared selection panel
- `src/manage/SeasonWizard.jsx`
  - regular-season preview now includes a DayPilot month summary via `src/components/SeasonSummaryCalendar.jsx`
  - existing assignment/detail tables remain for auditability and repair workflows

Validated locally:

- focused frontend tests
  - `src/__tests__/pages/CalendarPage.test.jsx`
  - `src/__tests__/manage/SeasonWizard.test.jsx`
- production build
  - `npm.cmd run build`

## In Scope for Further Conversion

These are the remaining user-facing schedule surfaces worth evaluating for DayPilot usage:

- `src/pages/CalendarPage.jsx`
  - remove the classic fallback once the DayPilot view has soaked long enough
  - keep export/filter/action behavior unchanged while doing that
- `src/manage/SeasonWizard.jsx`
  - decide whether the remaining preview tables should stay tabular or gain an additional DayPilot drill-down view
  - recommendation: keep tables for review/repair, keep DayPilot for visual load balancing

## Out of Scope for DayPilot Conversion

These surfaces show schedule-adjacent information, but they are not primary calendar experiences:

- `src/pages/HomePage.jsx`
  - dashboard summaries and shortcuts
- `src/pages/AdminDashboard.jsx`
  - KPI cards and operational summaries
- `src/pages/CoachDashboard.jsx`
  - upcoming game list and quick actions
- `src/manage/AvailabilityManager.jsx`
  - rule setup and preview tables
- `src/manage/AvailabilityAllocationsManager.jsx`
  - allocation generation, conflicts, and slot previews
- `src/pages/DebugPage.jsx`
  - diagnostics and audit tooling

Those should stay list/table-first unless a specific product workflow requires a calendar interaction.

## Next Recommended Cuts

1. Keep `CalendarPage` and `SeasonWizard` as the DayPilot proving ground.
2. Remove the `CalendarPage` classic fallback after a short soak period and test pass.
3. Only add more DayPilot views where users need calendar reasoning, not where they need admin tables.

## Guardrails

- DayPilot should not replace operational tables that are better for auditing, bulk edits, or conflict review.
- Shared calendar behavior should stay centralized in `src/components/CalendarView.jsx`.
- Additional calendar surfaces should reuse DayPilot components instead of introducing one-off wrappers.
