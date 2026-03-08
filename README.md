# GameSwap / SportsScheduler

GameSwap / SportsScheduler is an authenticated league operations tool for youth sports scheduling.

It includes:
- a React + Vite frontend at the repo root
- an Azure Functions isolated backend in `api/`
- Azure Table Storage for leagues, memberships, fields, slots, slot requests, events, and scheduling data

## Product Boundary
- Authenticated league-member tool first
- `CalendarPage` is the canonical schedule surface
- Game acceptance is immediate confirm
- Practice requests remain a separate commissioner-reviewed workflow
- Public slot browsing is not part of the current product
- External calendar subscription-link flows are not part of the current product

## Roles
- `LeagueAdmin`: league setup, scheduling, access approval, practice portal configuration
- `Coach`: offer slots, accept open game opportunities, manage team-facing schedule workflows
- `Viewer`: read-only schedule access
- `Global admin`: cross-league administration and oversight

## Canonical Rules
- League-scoped UI calls send `x-league-id` from `localStorage.gameswap_leagueId`
- Standard API response envelope is `{ "data": ... }` or `{ "error": { "code": "...", "message": "..." } }`
- Fields use canonical storage key `FIELD|{leagueId}|{parkCode}` and `FieldName`
- Slots use canonical storage key `SLOT|{leagueId}|{division}`
- Slot requests use canonical storage key `SLOTREQ|{leagueId}|{division}|{slotId}`
- Division DTOs use `code`, `name`, and `isActive`
- Practice portal one-off enablement is division-scoped

## Repo Layout
- UI: repo root
- API: `api/`
- Tests: `src/__tests__/` and `api/GameSwap.Tests/`

## Source Of Truth Docs
- [docs/contract.md](C:/Users/bergi/app/SportSch/docs/contract.md): current API/UI/storage contract
- [docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md](C:/Users/bergi/app/SportSch/docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md): game slot workflow behavior
- [docs/PRACTICE_REQUESTS_AND_CLAIMS_BEHAVIORAL_CONTRACT.md](C:/Users/bergi/app/SportSch/docs/PRACTICE_REQUESTS_AND_CLAIMS_BEHAVIORAL_CONTRACT.md): practice workflow behavior
- [docs/SCHEDULING_ENGINE_BEHAVIORAL_CONTRACT.md](C:/Users/bergi/app/SportSch/docs/SCHEDULING_ENGINE_BEHAVIORAL_CONTRACT.md): scheduling engine behavior
- [docs/scheduling.md](C:/Users/bergi/app/SportSch/docs/scheduling.md): scheduling operations and exports

## Roadmap / Research Docs
These are planning inputs, not current contract:
- [docs/ui-improvement-plan.md](C:/Users/bergi/app/SportSch/docs/ui-improvement-plan.md)
- [USER_PERSONAS.md](C:/Users/bergi/app/SportSch/USER_PERSONAS.md)
- [UX_IMPROVEMENTS_BY_PERSONA.md](C:/Users/bergi/app/SportSch/UX_IMPROVEMENTS_BY_PERSONA.md)
- [UI_IMPROVEMENTS.md](C:/Users/bergi/app/SportSch/UI_IMPROVEMENTS.md)

## Development
- Frontend build: `npm.cmd run build`
- Frontend tests: `npm.cmd run test -- --run ...`
- Backend tests: `dotnet test api/GameSwap.Tests/GameSwap.Tests.csproj`

When behavior changes, update backend, frontend, and the contract docs together.
