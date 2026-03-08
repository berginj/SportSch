# UI Improvement Plan

Status: roadmap input, not current contract.

This file captures future UI ideas without redefining the shipped product. Current-state behavior lives in `docs/contract.md`.

## Current Product Shape
- Authenticated league operations tool.
- `CalendarPage` is the canonical schedule surface.
- Game acceptance is immediate confirm.
- Practice requests remain a separate commissioner-reviewed workflow.
- Global admin tooling currently lives in the existing `Admin` and `Debug` surfaces, not a separate `/global-admin/*` route tree.

## Near-Term UI Priorities
- Keep coach, admin, and global-admin workflows focused on the most relevant actions first.
- Reduce duplicate surfaces and dead navigation paths.
- Make calendar defaults and filters role-aware.
- Keep terminology aligned with shipped behavior and API contracts.

## Coach Experience
- Emphasize upcoming games, open opportunities, and practice status.
- Keep quick actions close to the calendar and offer/request workflows.
- Continue simplifying onboarding so setup hands off cleanly to the Practice Portal.

## League Admin Experience
- Keep access approval, coach assignment, imports, and schedule operations in one coherent admin flow.
- Favor inline editing and bulk actions where they remove repetitive work.
- Keep field, division, and team management aligned on the current DTO and API contracts.

## Global Admin Experience
- Consolidate user, membership, and league oversight into clearer sections inside the current admin surfaces.
- Prefer league-context switching over inventing a second navigation model unless the product explicitly moves there later.
- Keep system health, user management, and league management aligned to the existing authenticated tool direction.

## Calendar and Notifications
- Continue improving role-based quick views and clean filter presentation.
- Preserve one canonical authenticated calendar model.
- Keep notification copy aligned with immediate-confirm games and commissioner-reviewed practices.

## Out of Scope for Current Product
- Public slot browsing.
- External calendar subscription URLs that depend on unauthenticated or tokenized access.
- Separate `/global-admin/*` route trees unless product direction changes later.

## Success Checks
- Coaches find open or confirmed games faster.
- League admins resolve access and scheduling work with fewer clicks.
- Global admins can inspect league and user state without switching between duplicate tools.
- Docs and UI labels match shipped workflows.
