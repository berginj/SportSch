# User Personas - Research Input

> Status: research reference only, not the canonical shipped contract.
>
> Last re-baselined: 2026-04-12
>
> For current shipped behavior, use `docs/contract.md`, `docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md`, `README.md`, and `AGENTS.md`.

This file is intentionally narrower than the older persona set. It reflects the roles and workflows that are actually present in the product today, and it separates future-state ideas from current contract-backed behavior.

## Current Product Scope

- Authenticated league-member tool only.
- All league-scoped API calls use header `x-league-id`.
- Public calendar browsing and public subscribe-link flows are not part of the shipped contract.
- Standard game slots use the immediate-confirm workflow:
  - create slot -> `Open`
  - accept slot -> request `Approved`, slot `Confirmed`, `confirmedTeamId` stored
  - cancel slot -> `Cancelled`
- Practice workflows remain separate and may use commissioner-reviewed `Pending` states in the Practice Portal contract.

## Current-State Personas

### 1. League Administrator / Commissioner

- Product role: `LeagueAdmin`
- Real users: commissioners, division schedulers, league operations volunteers
- Core workflows:
  - Manage divisions, teams, fields, and coach assignments
  - Run schedule wizard preview/apply flows
  - Review calendar conflicts and edit confirmed games
  - Configure practice inventory and division-scoped practice enablement
  - Review access requests and manage league setup
- Current success signals:
  - Can complete league setup and schedule operations without touching storage directly
  - Can export schedules from the authenticated calendar workflows
  - Can resolve schedule conflicts from current admin/calendar surfaces
- Contract guardrails:
  - Uses authenticated, header-scoped APIs only
  - Works from canonical league roles and canonical table/DTO shapes
  - Does not rely on public calendar or public parent-sharing flows

### 2. Coach

- Product role: `Coach`
- Real users: head coaches and team operators
- Core workflows:
  - Complete coach onboarding after receiving league membership
  - Maintain team profile and assistant-coach/contact details
  - Create open game slots for their assigned team
  - Accept open game opportunities from other teams
  - Review confirmed schedule in calendar and coach surfaces
  - Use the Practice Portal for practice requests and practice follow-up
- Current success signals:
  - Assigned coach can offer or accept a game without admin intervention
  - Coach sees their team schedule and confirmed opponents consistently
  - Practice tasks flow through the separate practice contract
- Contract guardrails:
  - Team assignment is required for offer/accept game actions
  - Standard game acceptance is immediate confirm, not an approval queue
  - Calendar and ICS access are authenticated and league-scoped

### 3. Viewer

- Product role: `Viewer`
- Real users: read-only staff, parents with league access, helpers who only need schedule visibility
- Core workflows:
  - View authenticated schedule and events
  - Filter by league, division, date range, and team
  - Read confirmed games and field/location details
- Current success signals:
  - Viewer can see confirmed schedules without write access
  - Viewer cannot create, accept, cancel, or administer league data
- Contract guardrails:
  - Viewer is authenticated and league-scoped
  - Viewer access is not anonymous/public access
  - Public calendar and public subscription workflows remain out of scope

### 4. Global Administrator

- Product role: `GlobalAdmin`
- Real users: platform operators supporting multiple leagues
- Core workflows:
  - Create leagues
  - Access global-admin-only endpoints
  - Assist league admins with troubleshooting in a league-scoped context
  - Inspect global admin/system support surfaces
- Current success signals:
  - Can administer leagues without bypassing canonical contracts
  - Can move between league contexts while preserving header-scoped behavior
- Contract guardrails:
  - Global admin capability expands authorization, not data-shape rules
  - League-scoped operations still use the same canonical routes and headers

## Future-State Exploration

The items below may be useful for product planning, but they are not shipped behavior and should not drive implementation without a contract update first.

- Public parent calendar access
- Tokenized external ICS/subscription links
- Richer tournament-coordinator workflows beyond the current admin tooling
- Assistant-coach or delegated sub-roles separate from the base `Coach` role
- Push notifications and parent communication automation
- External integration workflows beyond the current authenticated exports and feeds

## How To Use This File

- Use it for prioritization and desirability checks.
- Treat `docs/contract.md` as authoritative when persona language and implementation differ.
- When a future-state item becomes real product behavior, update the contract first and then promote it here.

## Change Log

- 2026-04-12: Re-baselined personas to current shipped roles and workflows; moved public-parent and approval-queue narratives to future-state exploration.
- 2026-03-03: Initial persona research draft created.
