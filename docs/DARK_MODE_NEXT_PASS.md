# Dark Mode: Next Pass Checklist

This is the follow-up scope after the MVP dark mode toggle.

## Completed in pass 2

1. Added reusable surface/status tokens in [`src/index.css`](../src/index.css) for both light/dark themes.
2. Migrated calendar/agenda status visuals to shared tokens in [`src/components/CalendarView.css`](../src/components/CalendarView.css).
3. Improved top-nav toggle accessibility (`aria-pressed`) and label clarity.
4. Added theme persistence/toggle tests in [`src/__tests__/App.theme.test.jsx`](../src/__tests__/App.theme.test.jsx).

## Completed in pass 3

1. Added legibility-focused dark overrides for `SeasonWizard` typography, controls, tables, and status rails.
2. Improved overlay contrast by tokenizing badge/day-entry backgrounds and text colors.
3. Improved notification dropdown/center dark-mode readability (item states, header/footer, action buttons).

## Remaining UI coverage gaps

1. Convert remaining hard-coded light gradients/colors in [`src/index.css`](../src/index.css) to semantic variables.
2. Normalize all `@apply` color tokens (`bg-gray-*`, `text-gray-*`, `border-gray-*`, etc.) to theme-aware utility classes or CSS variables.
3. Add dark-mode-specific overrides for wizard-heavy surfaces:
   - `SeasonWizard` visual rails/cards/status regions
   - Scheduler overlays and validation panels
   - Availability and slot generator dense table views
4. Review status chips/badges for WCAG contrast in dark mode (open/confirmed/cancelled/scheduled).

## Behavior and UX follow-ups

1. Add a 3-state theme setting: `Light`, `Dark`, `System`.
2. Observe `prefers-color-scheme` changes live when `System` is selected.
3. Add a settings-page control for theme (currently only in top nav).
4. Add subtle transition tuning for large background gradient switches to reduce perceived flicker.

## Testing follow-ups

1. Add component tests for theme toggle persistence (`localStorage` + `data-theme` on `documentElement`).
2. Add visual regression snapshots for key pages in light/dark:
   - Home
   - Calendar
   - Manage (commissioner + wizard)
   - Admin
3. Add accessibility checks for contrast and focus rings in dark mode.

## Suggested implementation sequence

1. Token cleanup and CSS variable migration.
2. Page-by-page dark polish (Home, Calendar, Manage, Admin).
3. Theme mode expansion (`System`) and settings page control.
4. Visual regression + a11y verification pass.
