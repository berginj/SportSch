# AGENTS

These are the operating rules for work in this repo.

## Scope and drift control
- This repo contains the GameSwap / SportsScheduler system: React + Vite UI and Azure Functions backend.
- Avoid drift in table names, partition keys, league scoping, API routes, field names, or workflow states.
- When behavior changes, update backend and UI consistently.
- If asked for full file replacements, provide full file content (not snippets).

## Canonical tables
- GameSwapMemberships
- GameSwapFields
- GameSwapSlots
- GameSwapSlotRequests

## PartitionKey and RowKey conventions
- Memberships: PK = <userId>, RK = <leagueId>
- Fields: PK = FIELD|{leagueId}|{parkCode}, RK = <fieldCode>, display name FieldName
- Slots: PK = SLOT|{leagueId}|{division}, RK = SafeKey("{offeringTeamId}|{gameDate}|{start}|{end}|{fieldKey}")
- Slot Requests: PK = SLOTREQ|{leagueId}|{division}|{slotId}, RK = <requestId GUID>
- New writes and reads should use canonical PKs. Do not add new legacy read fallbacks.

## League scoping
- UI sends header x-league-id from localStorage.gameswap_leagueId and credentials: include via apiFetch.
- Header-scoped league APIs are canonical.
- Do not introduce new query or route leagueId fallbacks unless a current contract explicitly requires them.
- If header and route leagueId differ, reject the request.

## Field naming
- Use FieldName everywhere. Do not reintroduce Name aliases.

## Canonical DTOs
- Divisions use code, name, and isActive.
- Coach membership/team shape is team.division + team.teamId.

## Workflow (Game Slots: Immediate Accept)
- Create slot: Status = Open
- Accept slot: create request Status = Approved, slot Status = Confirmed, store ConfirmedRequestId and ConfirmedTeamId
- Cancel slot: Status = Cancelled
- Practice workflows may still use Pending under the separate practice-request contract

## Practice portal
- One-off request enablement is division-scoped only.

## Calendar boundary
- Calendar feeds are authenticated and header-scoped.
- Do not add public slot/calendar surfaces or external subscription-link flows without updating the contract first.

## Response envelope
- API responses must use the standard envelope: { data: ... } or { error: { code, message, details? } }.

## API endpoints (preferred)
- GET /api/me
- GET /api/slots?division=...&status=...
- POST /api/slots
- PATCH /api/slots/{division}/{slotId}/cancel
- POST /api/slots/{division}/{slotId}/requests
- GET /api/slots/{division}/{slotId}/requests
- GET /api/fields
- GET /api/globaladmins
- GET /api/users
- GET /api/storage/health

## Debugging checklist for 403/empty data
- Ensure apiFetch is used and sends credentials and x-league-id.
- Verify localStorage.gameswap_leagueId matches a membership.
- Check GameSwapMemberships row PK = UserId, RK = LeagueId.
- Confirm data is written to canonical partitions.
