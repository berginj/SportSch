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
- Fields: PK = FIELD#{leagueId}, RK = <fieldIdSafeKey>, display name FieldName
- Slots: PK = SLOT#{leagueId}#{division}, RK = SafeKey("{offeringTeamId}|{gameDate}|{start}|{end}|{fieldKey}")
- Slot Requests: PK = SLOTREQ#{leagueId}#{division}#{slotId}, RK = <requestId GUID>
- New writes must use canonical PKs; legacy reads can fall back only when needed.

## League scoping
- UI sends header x-league-id from localStorage.activeLeagueId and credentials: include via apiFetch.
- Backend reads leagueId header first, then query fallback, then route fallback.
- If header and route leagueId differ, reject the request.

## Field naming
- Use FieldName everywhere. Backend may temporarily alias Name, but target is FieldName.

## Workflow (Request -> Approve)
- Create slot: Status = Open
- Request slot: create request Status = Pending, slot Status = Pending
- Approve request: slot Status = Confirmed, store ConfirmedRequestId and ConfirmedTeamId; approved request Approved, others Rejected
- Cancel slot: Status = Cancelled (optionally reject pending requests)

## API endpoints (preferred)
- GET /api/me
- GET /api/slots?division=...&status=...
- POST /api/slots
- PATCH /api/slots/{division}/{slotId}/cancel
- POST /api/slots/{division}/{slotId}/requests
- GET /api/slots/{division}/{slotId}/requests
- PATCH /api/slots/{division}/{slotId}/requests/{requestId}/approve
- GET /api/leagues/{leagueId}/fields

## Debugging checklist for 403/empty data
- Ensure apiFetch is used and sends credentials and x-league-id.
- Verify localStorage.activeLeagueId matches a membership.
- Check GameSwapMemberships row PK = UserId, RK = LeagueId.
- Confirm data is written to canonical partitions.
