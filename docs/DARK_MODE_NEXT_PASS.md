# Dark Mode: Next Pass Checklist

This is the follow-up scope after the MVP dark mode toggle.

## Remaining UI coverage gaps

1. Convert remaining hard-coded light gradients/colors in [`src/index.css`](../src/index.css) to semantic variables.
2. Convert remaining hard-coded light gradients/colors in [`src/components/CalendarView.css`](../src/components/CalendarView.css) to semantic variables.
3. Normalize all `@apply` color tokens (`bg-gray-*`, `text-gray-*`, `border-gray-*`, etc.) to theme-aware utility classes or CSS variables.
4. Add dark-mode-specific overrides for wizard-heavy surfaces:
   - `SeasonWizard` visual rails/cards/status regions
   - Scheduler overlays and validation panels
   - Availability and slot generator dense table views
5. Review status chips/badges for WCAG contrast in dark mode (open/confirmed/cancelled/scheduled).

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
