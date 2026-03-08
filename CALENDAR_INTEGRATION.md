# Calendar UI Status

> Status: current-state integration note for the calendar UI.
>
> For canonical workflow and API behavior, use `docs/contract.md`, `docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md`, `README.md`, and `AGENTS.md`.

## Current State

- `src/pages/CalendarPage.jsx` is the single canonical schedule surface.
- `src/components/CalendarView.jsx` and `src/components/CalendarView.css` are integrated into `CalendarPage`.
- The old `SchedulePage` surface has been removed.
- Legacy `#schedule` hashes are redirected to `#calendar` in `src/App.jsx`.

## Active Calendar Views

- `Classic View`: detailed linear list with status badges and action buttons.
- `Week Cards`: compact weekly summaries with expandable day detail.
- `Agenda`: chronological list optimized for smaller screens.

The compact views now share the same slot and event actions as the classic list:

- accept open slots
- accept as selected team
- edit scheduled games
- cancel slots
- delete events

## Filter Model

Server-backed filters in `CalendarPage` update the data set automatically:

- league
- division
- date range
- show slots / show events
- slot status

Page-only filters refine the currently loaded data without reloading:

- slot type
- team

Schedule exports follow the server-backed filters only.

## Known Boundaries

- `SeasonWizard` keeps its own preview surfaces; it does not use `CalendarView` as the canonical planner UI.
- Public or tokenized view-only calendar pages are not current product scope.
- Team and slot-type filters are page-view filters, not export filters.
- External calendar subscription links are not part of the current authenticated workflow.

## Cleanup Notes

This file replaces the older "not yet integrated" guidance. Do not use previous references to:

- integrating `CalendarView` into a future `SchedulePage`
- treating the compact view as an experiment outside `CalendarPage`
- preserving dual schedule surfaces
