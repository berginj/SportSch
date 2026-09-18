# Sports Scheduler MVP Workback Plan

This is the durable MVP workback plan for GameSwap / SportsScheduler.

## Product Direction

MVP scope:
- Game scheduling coordination
- Season management
- Field management
- SportsEngine export
- Public schedules

MVP users:
- League admins
- Coaches

Public access:
- Public schedules and Google Calendar feeds are fully public.
- Public surfaces expose only a safe schedule projection.

Deferred:
- Registration
- Payments
- Parent workflows
- Public youth/person data
- GameChanger sync
- Full umpire management

Version 1.1:
- Lightweight umpire offering workflow driven by `NeedsUmpire` flags on games and practices.

## Architecture Decisions

- Keep the low-cost stack: Azure Static Web Apps Free, managed .NET Functions API, Azure Table Storage, no Redis, no Cosmos DB, no containers, capped Application Insights, and GitHub Actions deploy.
- Keep authenticated league APIs header-scoped with `x-league-id`; do not add route/query league fallbacks for private APIs.
- Route all production API traffic through Static Web Apps. Direct Function App access must be blocked or rejected for non-public endpoints.
- Add distinct public endpoints under `/api/public/*`. These do not use `x-league-id`, do not require auth, and return only public-safe schedule data.
- Store league public settings in `GameSwapLeagues`, PK `LEAGUE`, RK `{leagueId}`:
  - `PublicSlug`
  - `PublicScheduleEnabled`
  - `PublicCalendarEnabled`
  - `PublicDisplayName`
  - `Timezone`
  - `UmpireOrganizationEmail` for 1.1
- Public schedule projection may include:
  - league display name
  - division
  - public team names
  - date
  - start/end time
  - field name
  - venue/park
  - status
  - event type
  - last updated
- Public schedule projection must exclude:
  - player names
  - parent data
  - coach names
  - emails
  - phone numbers
  - invite/access data
  - notes containing personal details
  - internal IDs not needed for subscription stability
- Google Calendar subscription uses a fully public ICS URL:
  - `GET /api/public/leagues/{publicSlug}/calendar.ics?division=&team=`
  - no token, no auth, no cookies
  - stable `UID`, correct timezone, `SEQUENCE`/last-modified behavior, no personal data
- Public web schedule uses:
  - `GET /api/public/leagues/{publicSlug}/schedule?division=&team=&from=&to=`
  - SPA public route that bypasses the signed-in app shell
- SportsEngine export remains authenticated and admin-only for MVP.

## Phase 0: Foundation Repair, 0-2 Weeks

Fix the product's trust baseline before adding public surfaces.

Done means:
- `npm run build` passes.
- `npm run lint` passes.
- `npm run test -- --run` passes.
- Both .NET test projects compile and pass.
- Known high dependency vulnerabilities are upgraded, removed, or documented with explicit mitigation.
- Calendar `canManage` runtime issue is fixed.
- Viewer/write-action mismatch is fixed or Viewer is removed from MVP UI.
- Slot creation uses the documented deterministic slot key or the contract is updated with a migration-safe decision.
- Direct Function App access is verified blocked/rejected for private APIs.
- App Insights is capped/sampled; frontend telemetry is disabled unless explicitly configured.
- Azure budget alerts exist at `$5`, `$10`, `$15`, and `$20`.

## Phase 1: MVP Season and Field Management, 3-6 Weeks

Make league admin and coach workflows complete enough for real early usage.

Done means:
- League admin can create/configure league settings, divisions, teams, fields, and coach memberships.
- League admin has a setup checklist showing missing fields/teams/divisions/coaches/schedule.
- Coach dashboard shows only coach-relevant jobs: upcoming games, open slots, accepted games, and reschedule actions.
- Coaches can create, accept, cancel, and view game slots using the immediate-accept workflow.
- League admin can run schedule preview/apply and export SportsEngine CSV.
- Practice/field management uses one canonical flow; legacy practice quick actions are removed or routed to the normalized practice portal.
- All private APIs enforce authenticated membership or league admin authorization as appropriate.
- No parent, payment, registration, or umpire UI is shown as MVP functionality.

## Phase 2: Public Schedule and Google Calendar, 6-8 Weeks

Add public visibility without exposing youth/person data.

Done means:
- League admin can enable/disable public schedule and choose a public slug.
- Public schedule page loads without sign-in.
- Public page has league/division/team/date filters and mobile-friendly layout.
- Public API response contains only the approved public projection.
- Google Calendar ICS URL works when pasted into Google Calendar.
- ICS feed includes stable event IDs, updates when games move, and omits personal data.
- Public endpoints have tests proving no coach/parent/player/contact/email/invite fields appear.
- `staticwebapp.config.json` allows anonymous `/api/public/*` before authenticated `/api/*`.
- Existing authenticated calendar remains header-scoped and unchanged.

## Phase 3: Email and Operational Polish, 8-10 Weeks

Add low-risk communication and operational controls.

Done means:
- Email sending is opt-in per league and fails safely when SendGrid is not configured.
- Email templates HTML-encode user-controlled content.
- Email queue has retention cleanup and does not store unnecessary body content long-term.
- In-app notification polling is reduced or paused when the tab is hidden.
- Admins can see basic system health: storage health, email configured/not configured, public schedule enabled, last schedule update.
- Cost dashboard or runbook documents current Azure services and expected monthly cost.

## Phase 4: Umpire Offering 1.1, After MVP Stabilizes

Build a lightweight umpire workflow, not a full umpire management suite.

Done means:
- League admin can enter `UmpireOrganizationEmail`.
- Games and practice slots can be flagged `NeedsUmpire`.
- Flagged items appear on a public umpire offering page with only safe public schedule data.
- League can email the umpire organization a link to current open umpire needs.
- Public umpire signup captures umpire name, email, optional phone, and organization note.
- Signup creates an umpire claim record, not a full app user account.
- Duplicate claims are prevented with optimistic concurrency.
- League admin can approve, reject, or clear a claim.
- Approved umpire claim is visible to league admins and relevant coaches only.
- Public pages never expose umpire personal contact data after claim.
- This feature reuses field/game/practice scheduling data and does not introduce a new scheduling engine.

Recommended 1.1 storage:
- Add `NeedsUmpire`, `UmpireStatus`, and `UmpireClaimId` to game/practice schedule entities.
- Add `GameSwapUmpireClaims`:
  - PK `UMPIRECLAIM|{leagueId}|{division}|{slotId}`
  - RK `{claimId}`
  - fields: claim status, umpire name/email/phone, organization note, timestamps, approvedBy

## Public API and Interface Changes

Add public routes:
- `GET /api/public/leagues/{publicSlug}/schedule`
- `GET /api/public/leagues/{publicSlug}/calendar.ics`
- 1.1: `GET /api/public/leagues/{publicSlug}/umpire-offers`
- 1.1: `POST /api/public/leagues/{publicSlug}/umpire-offers/{offerId}/claims`

Add authenticated admin route:
- `GET/PATCH /api/league/settings`
- Uses `x-league-id`
- League admin only
- Manages public slug, public schedule/calendar flags, timezone, public display name, and 1.1 umpire organization email

Public response shape:
- Continue standard JSON envelope for JSON APIs: `{ data: ... }` or `{ error: ... }`
- ICS endpoint returns `text/calendar`
- Public JSON never returns private entity rows directly; it returns DTOs only

## Required Automated Tests

- Private API without auth fails.
- Private API without valid league membership fails.
- Public schedule works without auth.
- Public schedule does not include email, phone, coach name, player name, parent data, notes, invite data, or internal audit fields.
- Google Calendar ICS parses as valid calendar data.
- ICS updates after game date/time/field changes.
- SportsEngine export remains admin-only.
- Coach cannot access league-admin setup routes.
- Viewer either has no MVP route or is strictly read-only.
- Umpire claim duplicate submission is rejected or safely serialized.
- Azure cost guardrails are documented and budget alerts exist.

## Manual Acceptance Scenarios

- New league admin can configure fields, divisions, teams, coaches, generate schedule, export SportsEngine CSV, and publish public schedule in one guided flow.
- Coach can sign in, see their team schedule, create an open slot, accept another slot, and see the confirmed game.
- Anonymous public visitor can view schedule and subscribe in Google Calendar without seeing personal data.
- League admin can disable public schedule and public endpoints stop returning schedule data.

## Assumptions and Defaults

- Public team names are allowed on public schedules; player, parent, coach contact, and umpire contact information is not.
- Public Google Calendar feeds are intentionally fully public because that was selected over tokenized feeds.
- Umpire management is not MVP; 1.1 is a claim/offering workflow, not authenticated umpire self-service.
- Registration and payments are stubbed as future integrations, not implemented now.
- GameChanger comes after SportsEngine, Google Calendar, and email.
- The target Azure spend remains under `$20/month`; any service that risks that target requires explicit approval before implementation.
