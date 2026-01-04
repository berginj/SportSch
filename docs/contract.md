# Sports Scheduler API Contract

This document is the single source of truth for UI/API integration.
It lives at `docs/contract.md` in this repo and must be kept current.

## Cross-cutting rules

### Auth
- All calls assume the user is authenticated via Azure Static Web Apps EasyAuth.
- The API may return 401 when the user is not signed in.

### Base path
- In Azure Static Web Apps, the API is exposed under `/api`. UI calls should use `/api/<route>` unless a different `VITE_API_BASE_URL` is configured.

### League scoping (non-negotiable)
- Every league-scoped endpoint requires header: x-league-id: <leagueId>
- Backend validates header presence and authorization (membership or global admin where specified).
- UI persists the selected league id (localStorage key `gameswap_leagueId`) and attaches it on every league-scoped request.

### Roles (locked)
League role strings:
- LeagueAdmin: can manage league setup (fields, divisions/templates, teams), update league contact, and perform all scheduler actions. Second only to global admin.
- Coach: can be approved before a team is assigned. A LeagueAdmin can assign (or change) the coach's team later. Coaches can offer slots, request swaps, and approve/deny slot requests. Some actions (like requesting a swap) may require a team assignment.
- Viewer: read-only. Can view available games/slots and upcoming schedule views. Cannot offer, request, approve, or manage setup.

Global admin:
- isGlobalAdmin is returned by /me. Global admins can create leagues and can perform any league-scoped admin action.

### Standard response envelope (non-negotiable)
All endpoints return JSON with one of:
- Success: { "data": ... }
- Failure: { "error": { "code": string, "message": string, "details"?: any } }

### Error codes (recommended)
- BAD_REQUEST (400)
- UNAUTHENTICATED (401)
- FORBIDDEN (403)
- NOT_FOUND (404)
- CONFLICT (409)
- INTERNAL (500)


### Time conventions (locked)
All schedule times are interpreted as **US/Eastern (America/New_York)**. The API stores and returns:
- `gameDate` / `eventDate` as `YYYY-MM-DD`
- `startTime` / `endTime` as `HH:MM` (24-hour)
The API does **not** convert between time zones.

---

## 1) Onboarding

### GET /me
Returns identity and memberships.

Response
```json
{
  "data": {
    "userId": "<string>",
    "email": "<string>",
    "isGlobalAdmin": false,
    "memberships": [
      { "leagueId": "ARL", "role": "LeagueAdmin" },
      { "leagueId": "ARL", "role": "Coach" },
      { "leagueId": "ARL", "role": "Coach", "team": { "division": "10U", "teamId": "TIGERS" } },
      { "leagueId": "XYZ", "role": "Viewer" }
    ]
  }
}
```

---

## 1b) Health

### GET /ping
Health check for the API.

Response
```json
{ "data": { "status": "ok" } }
```

---

## 2) Function call reference (authoritative)

This section is the **artifact for automated reviewers** (including Codex) to understand how every HTTP-triggered
function is called. Keep it current any time a route, method, or authorization requirement changes. The table below
is intentionally minimal: it defines how to call each function and where to find the implementation.

**For Codex reviewers:** treat this section as the canonical API call index. Read the table for routes/methods and
the notes for required headers or roles.

### Maintenance rules
- Every HttpTrigger must appear in the table below with its HTTP method(s) and route.
- If an endpoint becomes league-scoped, call out the `x-league-id` requirement in its notes.
- When refactoring or adding functions, update this table and the matching UI repo copy.

### Endpoint index

| Method | Route | File | Notes |
| --- | --- | --- | --- |
| GET | /ping | `Functions/Ping.cs` | Health check. |
| GET | /me | `Functions/GetMe.cs` | Authenticated identity and memberships. |
| GET | /leagues | `Functions/LeaguesFunctions.cs` | List leagues for current user. |
| GET | /league | `Functions/LeaguesFunctions.cs` | Get current league details (requires `x-league-id`). |
| PATCH | /league | `Functions/LeaguesFunctions.cs` | Update current league (requires `x-league-id`, LeagueAdmin). |
| GET | /admin/leagues | `Functions/LeaguesFunctions.cs` | Global admin list of leagues. |
| POST | /admin/leagues | `Functions/LeaguesFunctions.cs` | Global admin create league. |
| GET | /global/leagues | `Functions/LeaguesFunctions.cs` | Global admin list of leagues (alt route). |
| POST | /global/leagues | `Functions/LeaguesFunctions.cs` | Global admin create league (alt route). |
| PATCH | /admin/leagues/{leagueId}/season | `Functions/LeaguesFunctions.cs` | Global admin update season settings. |
| PATCH | /global/leagues/{leagueId}/season | `Functions/LeaguesFunctions.cs` | Global admin update season settings (alt route). |
| GET | /admin/globaladmins | `Functions/GlobalAdminsFunctions.cs` | Global admin list. |
| POST | /admin/globaladmins | `Functions/GlobalAdminsFunctions.cs` | Add global admin. |
| DELETE | /admin/globaladmins/{userId} | `Functions/GlobalAdminsFunctions.cs` | Remove global admin. |
| POST | /admin/wipe | `Functions/AdminWipe.cs` | Global admin wipe for league-scoped tables (requires `x-league-id`). |
| POST | /admin/migrate/fields | `Functions/AdminMigrateFields.cs` | Global admin migrate fields PKs from legacy format (requires `x-league-id`). |
| POST | /global/migrate/fields | `Functions/AdminMigrateFields.cs` | Global admin migrate fields PKs from legacy format (alt route). |
| GET | /admin/storage/health | `Functions/StorageHealth.cs` | Global admin storage connectivity check. |
| GET | /storage/health | `Functions/StorageHealth.cs` | Global admin storage connectivity check (alt route). |
| POST | /accessrequests | `Functions/AccessRequestsFunctions.cs` | Create access request. |
| GET | /accessrequests/mine | `Functions/AccessRequestsFunctions.cs` | Current user's access requests. |
| GET | /accessrequests | `Functions/AccessRequestsFunctions.cs` | League access requests (requires `x-league-id`, LeagueAdmin). Global admins can use `all=true` to list across leagues. |
| PATCH | /accessrequests/{userId}/approve | `Functions/AccessRequestsFunctions.cs` | Approve access request (requires `x-league-id`, LeagueAdmin). |
| PATCH | /accessrequests/{userId}/deny | `Functions/AccessRequestsFunctions.cs` | Deny access request (requires `x-league-id`, LeagueAdmin). |
| POST | /admin/invites | `Functions/LeagueInvitesFunctions.cs` | Create invite (requires `x-league-id`, LeagueAdmin). |
| POST | /invites/accept | `Functions/LeagueInvitesFunctions.cs` | Accept invite. |
| GET | /memberships | `Functions/MembershipsFunctions.cs` | List memberships (requires `x-league-id`, LeagueAdmin). |
| PATCH | /memberships/{userId} | `Functions/MembershipsFunctions.cs` | Update membership (requires `x-league-id`, LeagueAdmin). |
| GET | /divisions | `Functions/DivisionsFunctions.cs` | List divisions (requires `x-league-id`). |
| POST | /divisions | `Functions/DivisionsFunctions.cs` | Create division (requires `x-league-id`, LeagueAdmin). |
| PATCH | /divisions/{code} | `Functions/DivisionsFunctions.cs` | Update division (requires `x-league-id`, LeagueAdmin). |
| GET | /divisions/templates | `Functions/DivisionsFunctions.cs` | List division templates (requires `x-league-id`). |
| PATCH | /divisions/templates | `Functions/DivisionsFunctions.cs` | Update division templates (requires `x-league-id`, LeagueAdmin). |
| GET | /teams | `Functions/TeamsFunctions.cs` | List teams (requires `x-league-id`). |
| POST | /teams | `Functions/TeamsFunctions.cs` | Create team (requires `x-league-id`, LeagueAdmin). |
| PATCH | /teams/{division}/{teamId} | `Functions/TeamsFunctions.cs` | Update team (requires `x-league-id`, LeagueAdmin). |
| DELETE | /teams/{division}/{teamId} | `Functions/TeamsFunctions.cs` | Delete team (requires `x-league-id`, LeagueAdmin). |
| GET | /fields | `Functions/FieldsFunctions.cs` | List fields (requires `x-league-id`). |
| PATCH | /fields/{parkCode}/{fieldCode} | `Functions/FieldsFunctions.cs` | Update field address details (requires `x-league-id`, LeagueAdmin). |
| POST | /import/fields | `Functions/ImportFields.cs` | CSV field import (requires `x-league-id`, LeagueAdmin). |
| POST | /import/teams | `Functions/ImportTeams.cs` | CSV teams import (requires `x-league-id`, LeagueAdmin). |
| POST | /import/slots | `Functions/ImportSlots.cs` | CSV slot import (requires `x-league-id`, LeagueAdmin). |
| GET | /slots | `Functions/GetSlots.cs` | List slots (requires `x-league-id`). |
| POST | /slots | `Functions/CreateSlot.cs` | Create slot (requires `x-league-id`, Coach or LeagueAdmin). |
| PATCH | /slots/{division}/{slotId}/cancel | `Functions/CancelSlot.cs` | Cancel slot (requires `x-league-id`, LeagueAdmin). |
| GET | /slots/{division}/{slotId}/requests | `Functions/GetSlotRequests.cs` | List requests for slot (requires `x-league-id`). |
| POST | /slots/{division}/{slotId}/requests | `Functions/CreateSlotRequest.cs` | Request slot (requires `x-league-id`, Coach). |
| PATCH | /slots/{division}/{slotId}/requests/{requestId}/approve | `Functions/ApproveSlotRequest.cs` | Approve slot request (requires `x-league-id`, offering coach, LeagueAdmin, or global admin). |
| GET | /availability/rules | `Functions/AvailabilityFunctions.cs` | List availability rules for a field (requires `x-league-id`, LeagueAdmin). |
| POST | /availability/rules | `Functions/AvailabilityFunctions.cs` | Create availability rule (requires `x-league-id`, LeagueAdmin). |
| PATCH | /availability/rules/{ruleId} | `Functions/AvailabilityFunctions.cs` | Update availability rule (requires `x-league-id`, LeagueAdmin). |
| PATCH | /availability/rules/{ruleId}/deactivate | `Functions/AvailabilityFunctions.cs` | Deactivate availability rule (requires `x-league-id`, LeagueAdmin). |
| GET | /availability/rules/{ruleId}/exceptions | `Functions/AvailabilityFunctions.cs` | List availability exceptions for a rule (requires `x-league-id`, LeagueAdmin). |
| POST | /availability/rules/{ruleId}/exceptions | `Functions/AvailabilityFunctions.cs` | Create availability exception (requires `x-league-id`, LeagueAdmin). |
| PATCH | /availability/rules/{ruleId}/exceptions/{exceptionId} | `Functions/AvailabilityFunctions.cs` | Update availability exception (requires `x-league-id`, LeagueAdmin). |
| DELETE | /availability/rules/{ruleId}/exceptions/{exceptionId} | `Functions/AvailabilityFunctions.cs` | Delete availability exception (requires `x-league-id`, LeagueAdmin). |
| GET | /availability/preview | `Functions/AvailabilityFunctions.cs` | Preview availability slots (requires `x-league-id`, LeagueAdmin). |
| POST | /schedule/preview | `Functions/ScheduleFunctions.cs` | Preview schedule for a division (requires `x-league-id`, LeagueAdmin). |
| POST | /schedule/apply | `Functions/ScheduleFunctions.cs` | Apply schedule assignments (requires `x-league-id`, LeagueAdmin). Blocks if validation issues exist. |
| POST | /schedule/validate | `Functions/ScheduleFunctions.cs` | Validate scheduled games for a division (requires `x-league-id`, LeagueAdmin). |
| POST | /schedule/slots/preview | `Functions/SlotGenerationFunctions.cs` | Preview generated availability slots (requires `x-league-id`, LeagueAdmin). |
| POST | /schedule/slots/apply | `Functions/SlotGenerationFunctions.cs` | Generate availability slots (requires `x-league-id`, LeagueAdmin). |
| GET | /calendar/ics | `Functions/CalendarFeed.cs` | Calendar subscription feed (requires `x-league-id` or leagueId query). |
| GET | /events | `Functions/GetEvents.cs` | List events (requires `x-league-id`). |
| POST | /events | `Functions/CreateEvent.cs` | Create event (requires `x-league-id`, LeagueAdmin). |
| PATCH | /events/{eventId} | `Functions/PatchEvent.cs` | Update event (requires `x-league-id`, LeagueAdmin). |
| DELETE | /events/{eventId} | `Functions/DeleteEvent.cs` | Delete event (requires `x-league-id`, LeagueAdmin). |

---

## 3) Leagues

### GET /leagues
Public list of active leagues (used before membership).

Response
```json
{ "data": [ { "leagueId": "ARL", "name": "Arlington", "timezone": "America/New_York", "status": "Active" } ] }
```

### GET /league (league-scoped)
Header: x-league-id

Response
```json
{
  "data": {
    "leagueId": "ARL",
    "name": "Arlington",
    "timezone": "America/New_York",
    "status": "Active",
    "contact": { "name": "...", "email": "...", "phone": "..." },
    "season": {
      "springStart": "2026-03-01",
      "springEnd": "2026-06-30",
      "fallStart": "2026-08-15",
      "fallEnd": "2026-11-01",
      "gameLengthMinutes": 120,
      "blackouts": [
        { "startDate": "2026-04-06", "endDate": "2026-04-12", "label": "Spring Break" }
      ]
    }
  }
}
```

### PATCH /league (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{ "name": "Arlington", "timezone": "America/New_York", "contact": { "name": "...", "email": "...", "phone": "..." }, "status": "Active" }
```

Response
```json
{ "data": { "leagueId": "ARL", "name": "Arlington", "timezone": "America/New_York", "status": "Active", "contact": { "name": "...", "email": "...", "phone": "..." } } }
```

### Admin: GET /admin/leagues
Requires: global admin.

### Admin: POST /admin/leagues
Requires: global admin.

Body
```json
{ "leagueId": "ARL", "name": "Arlington", "timezone": "America/New_York" }
```

### Admin: PATCH /admin/leagues/{leagueId}/season
Requires: global admin.

Body
```json
{
  "season": {
    "springStart": "2026-03-01",
    "springEnd": "2026-06-30",
    "fallStart": "2026-08-15",
    "fallEnd": "2026-11-01",
    "gameLengthMinutes": 120,
    "blackouts": [
      { "startDate": "2026-04-06", "endDate": "2026-04-12", "label": "Spring Break" }
    ]
  }
}
```

### Admin: POST /admin/wipe (league-scoped)
Requires: global admin.
Header: `x-league-id`

Body
```json
{ "tables": ["fields", "slots", "slotrequests"], "confirm": "WIPE" }
```

Notes
- `tables` is optional; when omitted, defaults to all supported league tables.
- Supported table keys: `accessrequests`, `divisions`, `events`, `fields`, `invites`, `memberships`, `slotrequests`, `slots`, `teams`.

### Admin: POST /admin/migrate/fields (league-scoped)
Requires: global admin.
Header: `x-league-id`

Migrates legacy field rows with PK `FIELD#<leagueId>#<parkCode>` into the new PK format `FIELD|<leagueId>|<parkCode>`.

---

## 3b) Global admins (admin)

### Admin: GET /admin/globaladmins
Requires: global admin.

Response
```json
{ "data": [ { "userId": "...", "email": "admin@example.com" } ] }
```

### Admin: POST /admin/globaladmins
Requires: global admin.

Body
```json
{ "userId": "<string>" }
```

Response
```json
{ "data": { "userId": "...", "email": "admin@example.com" } }
```

### Admin: DELETE /admin/globaladmins/{userId}
Requires: global admin.

Response
```json
{ "data": { "userId": "...", "removed": true } }
```

---

## 4) Access

### POST /accessrequests (league-scoped)
Header: x-league-id
Creates an access request for the selected league. Callers may not be members yet.

Body
```json
{ "requestedRole": "Coach", "notes": "I coach the Tigers" }
```

Response
```json
{ "data": { "leagueId": "ARL", "userId": "...", "email": "...", "requestedRole": "Coach", "status": "Pending", "notes": "..." } }
```

### GET /accessrequests/mine
Returns the caller's access requests across leagues.

Response
```json
{ "data": [ { "leagueId": "ARL", "requestedRole": "Coach", "status": "Pending", "notes": "..." } ] }
```

### Admin: GET /accessrequests (league-scoped)
Header: x-league-id
Requires: LeagueAdmin or global admin.
Query: status (default Pending)

Global admin: list across all leagues
Query: `all=true` (requires global admin; ignores league header scope)

### Admin: PATCH /accessrequests/{userId}/approve (league-scoped)
Header: x-league-id
Requires: LeagueAdmin or global admin.

Body (optional overrides)
```json
{ "role": "Coach", "team": { "division": "10U", "teamId": "TIGERS" } }
```

Response
```json
{ "data": { "leagueId": "ARL", "userId": "...", "status": "Approved" } }
```

### Admin: PATCH /accessrequests/{userId}/deny (league-scoped)
Header: x-league-id
Requires: LeagueAdmin or global admin.

Body
```json
{ "reason": "Not a coach" }
```

---

## 4b) Invites (admin)

### Admin: POST /admin/invites (league-scoped)
Header: x-league-id
Requires: LeagueAdmin or global admin.

Body
```json
{
  "inviteEmail": "coach@example.com",
  "role": "Coach",
  "team": { "division": "10U", "teamId": "TIGERS" },
  "expiresHours": 168
}
```

Response
```json
{
  "data": {
    "inviteId": "...",
    "inviteEmail": "coach@example.com",
    "role": "Coach",
    "status": "Sent",
    "team": { "division": "10U", "teamId": "TIGERS" },
    "acceptUrl": "https://<app>/?inviteId=...&leagueId=ARL"
  }
}
```

### POST /invites/accept
Accepts an invite using the invite id + league id.

Body
```json
{ "leagueId": "ARL", "inviteId": "<string>" }
```

Response
```json
{ "data": { "leagueId": "ARL", "status": "Accepted" } }
```

---

## 4c) Memberships (admin)

### Admin: GET /memberships (league-scoped)
Header: x-league-id
Requires: LeagueAdmin or global admin.

Response
```json
{
  "data": [
    { "userId": "...", "email": "...", "role": "LeagueAdmin" },
    { "userId": "...", "email": "...", "role": "Coach" },
    { "userId": "...", "email": "...", "role": "Coach", "team": { "division": "10U", "teamId": "TIGERS" } },
    { "userId": "...", "email": "...", "role": "Viewer" }
  ]
}
```

### Admin: PATCH /memberships/{userId} (league-scoped)
Header: x-league-id
Requires: LeagueAdmin or global admin.
Assigns (or clears) a coach's team assignment. This does not change the user's role.

Body
```json
{ "team": { "division": "10U", "teamId": "TIGERS" } }
```

Clear assignment
```json
{ "team": null }
```

Response
```json
{ "data": { "userId": "...", "role": "Coach", "team": { "division": "10U", "teamId": "TIGERS" } } }
```

Notes
- Some coach actions (e.g., requesting a swap) require a team assignment. When missing, the API returns 400 with error code COACH_TEAM_REQUIRED.

## 5) Divisions

### GET /divisions (league-scoped)
Header: x-league-id
Requires: member (any role).

### POST /divisions (league-scoped)
Requires: LeagueAdmin or global admin.

### PATCH /divisions/{code} (league-scoped)
Requires: LeagueAdmin or global admin.

### GET /divisions/templates (league-scoped)
Returns the division template catalog for this league.

### PATCH /divisions/templates (league-scoped)
Requires: LeagueAdmin or global admin.
Sets the league's division template catalog.

---

## 6) Fields

Fields are league-scoped via `x-league-id` and are referenced by a stable `fieldKey` (provided by admins via CSV import).

Field status strings
- Active
- Inactive

### GET /fields (league-scoped)
Requires: member (Viewer allowed).

Query:
- `activeOnly` (optional, default true)

Response
```json
{
  "data": [
    {
      "fieldKey": "gunston/turf",
      "parkName": "Gunston Park",
      "fieldName": "Turf",
      "displayName": "Gunston Park > Turf",
      "address": "",
      "city": "",
      "state": "",
      "notes": "",
      "status": "Active"
    }
  ]
}
```

### POST /import/fields (league-scoped)
Requires: LeagueAdmin or global admin.

Body: raw CSV (`Content-Type: text/csv`)

Required columns:
- `fieldKey` (unique within league; format `parkCode/fieldCode`)
- `parkName`
- `fieldName`

Optional columns:
- `displayName`
- `address`
- `city`
- `state`
- `notes`
- `status` (`Active` or `Inactive`)

Import behavior:
- Upserts by `fieldKey`.
- `status=Inactive` deactivates the field (it will not appear in slot creation pickers when `activeOnly=true`).

### POST /import/teams (league-scoped)
Requires: LeagueAdmin or global admin.

Body: raw CSV (`Content-Type: text/csv`)

Required columns:
- `division`
- `teamId`
- `name`

Optional columns:
- `coachName`
- `coachEmail`
- `coachPhone`

Import behavior:
- Upserts by `division + teamId`.
- Coach contact fields populate the team’s primary contact info.

### PATCH /fields/{parkCode}/{fieldCode} (league-scoped)
Requires: LeagueAdmin or global admin.

Body (any subset)
```json
{ "address": "2701 S Lang St", "city": "Arlington", "state": "VA", "notes": "Gate code 1234" }
```

---

## 7) Teams

Teams are identified by league (header) + division + teamId.

### GET /teams (league-scoped)
Query: division (optional)
Requires: member (any role).

### POST /teams (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{ "division": "10U", "teamId": "TIGERS", "name": "Tigers", "primaryContact": { "name": "...", "email": "...", "phone": "..." } }
```

### PATCH /teams/{division}/{teamId} (league-scoped)
Requires: LeagueAdmin or global admin.

### DELETE /teams/{division}/{teamId} (league-scoped)
Requires: LeagueAdmin or global admin.

---

## 8) Slots

Slots are **open game offers/requests** placed on the calendar by a coach (or LeagueAdmin). Another coach can see an open slot and **accept** it (via `POST /slots/{division}/{slotId}/requests`). Acceptance immediately confirms the slot (scheduled on the calendar).

Slot status strings
- Open
- Confirmed (accepted + scheduled)
- Cancelled

Game type strings
- Swap (offer)
- Request

Availability slots
- `isAvailability=true` denotes raw field availability generated by admins.
- Availability slots are not shown to coaches and cannot be accepted until scheduled.

### POST /import/slots (league-scoped)
Requires: LeagueAdmin or global admin.

Body: raw CSV (`Content-Type: text/csv`)

Required columns:
- `division`
- `offeringTeamId`
- `gameDate` (YYYY-MM-DD)
- `startTime` (HH:MM)
- `endTime` (HH:MM)
- `fieldKey` (format `parkCode/fieldCode`)

Optional columns:
- `notes`
- `offeringEmail`
- `gameType`
- `status`

Import behavior:
- Creates slots for the specified league and division.
- `fieldKey` must match an imported field.

### GET /slots (league-scoped)
Query (all optional): division, status (comma-separated), dateFrom (YYYY-MM-DD), dateTo (YYYY-MM-DD)  
Requires: member (Viewer allowed).

Visibility:
- Confirmed slots are visible to all league members (including Viewer).

Default behavior:
- If `status` is omitted, the API returns **Open + Confirmed** slots.
- To see cancelled slots, pass `status=Cancelled`.

Response
```json
{
  "data": [
    {
      "slotId": "slot_123",
      "leagueId": "ARL",
      "division": "10U",
      "offeringTeamId": "TIGERS",
      "confirmedTeamId": "",
      "homeTeamId": "TIGERS",
      "awayTeamId": "",
      "isExternalOffer": false,
      "isAvailability": false,
      "gameDate": "2026-04-10",
      "startTime": "18:00",
      "endTime": "20:00",
      "parkName": "Gunston",
      "fieldName": "Turf",
      "displayName": "Gunston > Turf",
      "fieldKey": "gunston/turf",
      "gameType": "Swap",
      "status": "Open",
      "notes": "Open game offer"
    }
  ]
}
```

### POST /slots (league-scoped)
Requires: Coach or LeagueAdmin (not Viewer).

Body
```json
{
  "division": "10U",
  "offeringTeamId": "TIGERS",
  "gameDate": "2026-04-10",
  "startTime": "18:00",
  "endTime": "20:00",
  "fieldKey": "gunston/turf",
  "notes": "Open game offer",
  "gameType": "Swap",
  "offeringEmail": "coach@example.com"
}
```

Rules
- If caller role is `Coach`, the API enforces `offeringTeamId` and `division` must exactly match the coach's assigned team.
  - If the coach has no team assignment: 400 `COACH_TEAM_REQUIRED`.
  - If team/division do not match: 403 `FORBIDDEN` or 409 `DIVISION_MISMATCH`.
- `gameDate`, `startTime`, and `endTime` are interpreted as US/Eastern and must be valid (`HH:MM`, start < end).
- `fieldKey` must reference an imported field (`parkCode/fieldCode`). The server normalizes `parkName`, `fieldName`, and `displayName` from that record.
- LeagueAdmins (and global admins) may create slots for any team.


### POST /slots/{division}/{slotId}/requests (league-scoped)
Creates a request to take an open slot (this is what the UI calls "Accept").

Requires: Coach or LeagueAdmin (not Viewer).  
Rules
- Requesting coach must have a team assignment (otherwise 400 `COACH_TEAM_REQUIRED`).
- League admins/global admins may pass `requestingTeamId` (and optional `requestingDivision`) to accept on behalf of a team.
- **Division validation:** requesting team division must exactly match `{division}`.
- Cannot request your own slot.
- Slot must be `Open`.
- Slots with `awayTeamId` assigned and `isExternalOffer=false` are reserved for league scheduling and cannot be accepted.

Body
```json
{ "notes": "We can play!", "requestingTeamId": "EAGLES", "requestingDivision": "10U" }
```

Response
```json
{
  "data": {
    "requestId": "req_123",
    "requestingTeamId": "EAGLES",
    "status": "Approved",
    "requestedUtc": "2026-03-01T12:00:00Z",
    "slotStatus": "Confirmed",
    "confirmedTeamId": "EAGLES"
  }
}
```

### GET /slots/{division}/{slotId}/requests (league-scoped)
Requires: member (Viewer allowed).

### PATCH /slots/{division}/{slotId}/requests/{requestId}/approve (league-scoped)
Legacy/compatibility endpoint.

With immediate-confirmation semantics, slot acceptance already confirms the slot. This endpoint is idempotent:
- If the slot is already confirmed for the given requestId, it returns ok.
- Otherwise it returns 409 conflict.

Requires: member role is not Viewer.  
Rules
- Allowed for: offering coach (offeringTeamId) OR LeagueAdmin OR global admin.
- If the slot is already confirmed for this requestId, returns ok.
- If the slot is confirmed for a different requestId, returns 409.

Response
```json
{ "data": { "ok": true, "slotId": "slot_123", "division": "10U", "requestId": "req_123", "status": "Confirmed" } }
```

### PATCH /slots/{division}/{slotId}/cancel (league-scoped)
Requires: offering team OR accepting team (confirmedTeamId) OR LeagueAdmin OR global admin.

---

## 8a) Field availability rules (admin)

Availability rules define recurring field availability windows. Use exceptions to remove or adjust specific date ranges.

### GET /availability/rules (league-scoped)
Requires: LeagueAdmin or global admin.  
Query: `fieldKey` (format `parkCode/fieldCode`)

### POST /availability/rules (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{
  "fieldKey": "gunston/turf",
  "division": "10U",
  "divisionIds": ["10U"],
  "startsOn": "2026-03-01",
  "endsOn": "2026-05-31",
  "daysOfWeek": ["Mon", "Wed"],
  "startTimeLocal": "17:00",
  "endTimeLocal": "22:00",
  "recurrencePattern": "Weekly",
  "timezone": "America/New_York",
  "isActive": true
}
```

Response
```json
{
  "data": {
    "ruleId": "rule_123",
    "fieldKey": "gunston/turf",
    "division": "10U",
    "divisionIds": ["10U"],
    "startsOn": "2026-03-01",
    "endsOn": "2026-05-31",
    "daysOfWeek": ["Mon", "Wed"],
    "startTimeLocal": "17:00",
    "endTimeLocal": "22:00",
    "recurrencePattern": "Weekly",
    "timezone": "America/New_York",
    "isActive": true
  }
}
```

### PATCH /availability/rules/{ruleId} (league-scoped)
Requires: LeagueAdmin or global admin.  
Body: same as create.

### PATCH /availability/rules/{ruleId}/deactivate (league-scoped)
Requires: LeagueAdmin or global admin.

### GET /availability/rules/{ruleId}/exceptions (league-scoped)
Requires: LeagueAdmin or global admin.

### POST /availability/rules/{ruleId}/exceptions (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{
  "dateFrom": "2026-04-10",
  "dateTo": "2026-04-10",
  "startTimeLocal": "18:00",
  "endTimeLocal": "20:00",
  "reason": "Tournament blackout"
}
```

Response
```json
{
  "data": {
    "exceptionId": "ex_123",
    "dateFrom": "2026-04-10",
    "dateTo": "2026-04-10",
    "startTimeLocal": "18:00",
    "endTimeLocal": "20:00",
    "reason": "Tournament blackout"
  }
}
```

### PATCH /availability/rules/{ruleId}/exceptions/{exceptionId} (league-scoped)
Requires: LeagueAdmin or global admin.  
Body: same as create.

### DELETE /availability/rules/{ruleId}/exceptions/{exceptionId} (league-scoped)
Requires: LeagueAdmin or global admin.

### GET /availability/preview (league-scoped)
Requires: LeagueAdmin or global admin.  
Query: `dateFrom` (YYYY-MM-DD), `dateTo` (YYYY-MM-DD)

Response
```json
{
  "data": {
    "slots": [
      {
        "gameDate": "2026-03-03",
        "startTime": "17:00",
        "endTime": "22:00",
        "fieldKey": "gunston/turf",
        "division": "10U"
      }
    ]
  }
}
```

---

## 8c) Division scheduling (admin)

### POST /schedule/preview (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{
  "division": "10U",
  "dateFrom": "2026-04-01",
  "dateTo": "2026-06-30",
  "constraints": {
    "maxGamesPerWeek": 2,
    "noDoubleHeaders": true,
    "balanceHomeAway": true,
    "externalOfferPerWeek": 1
  }
}
```

Response
```json
{
  "data": {
    "summary": {
      "slotsTotal": 24,
      "slotsAssigned": 20,
      "matchupsTotal": 21,
      "matchupsAssigned": 20,
      "externalOffers": 0,
      "unassignedSlots": 4,
      "unassignedMatchups": 1
    },
    "assignments": [
      {
        "slotId": "slot_1",
        "gameDate": "2026-04-10",
        "startTime": "18:00",
        "endTime": "19:30",
        "fieldKey": "gunston/turf",
        "homeTeamId": "TIGERS",
        "awayTeamId": "EAGLES",
        "isExternalOffer": false
      }
    ],
    "unassignedSlots": [
      {
        "slotId": "slot_24",
        "gameDate": "2026-06-29",
        "startTime": "18:00",
        "endTime": "19:30",
        "fieldKey": "gunston/turf",
        "homeTeamId": "",
        "awayTeamId": "",
        "isExternalOffer": false
      }
    ],
    "unassignedMatchups": [
      { "homeTeamId": "SHARKS", "awayTeamId": "OWLS" }
    ],
    "failures": [
      {
        "ruleId": "double-header",
        "severity": "warning",
        "message": "TIGERS has 2 games on 2026-04-10.",
        "details": { "teamId": "TIGERS", "gameDate": "2026-04-10", "count": 2 }
      }
    ]
  }
}
```

### POST /schedule/apply (league-scoped)
Requires: LeagueAdmin or global admin.

Body: same as preview.

Response
```json
{
  "data": {
    "runId": "sched_123",
    "summary": {
      "slotsTotal": 24,
      "slotsAssigned": 20,
      "matchupsTotal": 21,
      "matchupsAssigned": 20,
      "externalOffers": 0,
      "unassignedSlots": 4,
      "unassignedMatchups": 1
    },
    "assignments": [
      {
        "slotId": "slot_1",
        "gameDate": "2026-04-10",
        "startTime": "18:00",
        "endTime": "19:30",
        "fieldKey": "gunston/turf",
        "homeTeamId": "TIGERS",
        "awayTeamId": "EAGLES",
        "isExternalOffer": false
      }
    ],
    "failures": []
  }
}
```

Apply failure (validation issues)
```json
{
  "error": {
    "code": "SCHEDULE_VALIDATION_FAILED",
    "message": "Schedule validation failed with 2 issue(s). Review the Schedule preview and adjust constraints, then try again. See /#schedule."
  }
}
```

### POST /schedule/validate (league-scoped)
Requires: LeagueAdmin or global admin.

Body: same as preview.

Response
```json
{
  "data": {
    "summary": {
      "slotsTotal": 20,
      "slotsAssigned": 20,
      "matchupsTotal": 20,
      "matchupsAssigned": 20,
      "externalOffers": 0,
      "unassignedSlots": 0,
      "unassignedMatchups": 0
    },
    "issues": [
      {
        "ruleId": "double-header",
        "severity": "warning",
        "message": "TIGERS has 2 games on 2026-04-10.",
        "details": { "teamId": "TIGERS", "gameDate": "2026-04-10", "count": 2 }
      }
    ],
    "totalIssues": 1
  }
}
```

Scheduler export formats
- Internal CSV: division, gameDate, startTime, endTime, fieldKey, homeTeamId, awayTeamId, isExternalOffer
- SportsEngine CSV template (`docs/sportsenginetemplate.csv`): Event Type, Date, Start Time, End Time, Duration (minutes), Home Team, Away Team, Venue, Status (other event-only columns left blank)

### POST /schedule/slots/preview (league-scoped)
Requires: LeagueAdmin or global admin.

Notes
- Uses league-level `gameLengthMinutes` for back-to-back slot sizing.
- Skips league blackout ranges.
- `source=rules` pulls slot windows from field availability rules.

Body
```json
{
  "division": "10U",
  "fieldKey": "gunston/turf",
  "dateFrom": "2026-03-01",
  "dateTo": "2026-05-31",
  "daysOfWeek": ["Mon", "Sun"],
  "startTime": "17:00",
  "endTime": "22:00",
  "source": "adhoc"
}
```

Rules-based body (uses availability rules for the field/division)
```json
{
  "division": "10U",
  "fieldKey": "gunston/turf",
  "dateFrom": "2026-03-01",
  "dateTo": "2026-05-31",
  "source": "rules"
}
```

Response
```json
{
  "data": {
    "slots": [
      { "gameDate": "2026-03-01", "startTime": "17:00", "endTime": "19:00", "fieldKey": "gunston/turf", "division": "10U" }
    ],
    "conflicts": [
      { "gameDate": "2026-03-08", "startTime": "19:00", "endTime": "21:00", "fieldKey": "gunston/turf", "division": "10U" }
    ]
  }
}
```

### POST /schedule/slots/apply?mode=skip|overwrite|regenerate (league-scoped)
Requires: LeagueAdmin or global admin.

Body: same as preview.

Response
```json
{
  "data": {
    "created": [
      { "gameDate": "2026-03-01", "startTime": "17:00", "endTime": "19:00", "fieldKey": "gunston/turf", "division": "10U" }
    ],
    "overwritten": [],
    "skipped": [],
    "cleared": 0
  }
}
```

---

## 8b) Calendar feed

### GET /calendar/ics (league-scoped)
Requires: member (Viewer allowed).  
Header: x-league-id (or query param `leagueId`).

Query (all optional):
- `division`
- `dateFrom` (YYYY-MM-DD)
- `dateTo` (YYYY-MM-DD)
- `includeSlots` (true/false, default true)
- `includeEvents` (true/false, default true)
- `status` (slot status list, comma-separated)
- `includeCancelled` (true/false, default false when `status` omitted)

Returns iCalendar (ICS) with slots + events.

---

## 9) Events

Events are calendar items that are **not** Slots (e.g., practices, meetings, clinics, tryouts).
They are league-scoped via x-league-id.

Event types (string)
- Practice
- Meeting
- Clinic
- Other

Event status (string)
- Scheduled
- Cancelled

### GET /events (league-scoped)
Query (all optional): division, dateFrom (YYYY-MM-DD), dateTo (YYYY-MM-DD)  
Requires: member (Viewer allowed).

### POST /events (league-scoped)
Requires: LeagueAdmin or global admin.

Body (required fields: title, eventDate, startTime, endTime)
```json
{
  "type": "Practice",
  "division": "10U",
  "teamId": "TIGERS",
  "title": "Practice",
  "eventDate": "2026-04-05",
  "startTime": "18:00",
  "endTime": "19:30",
  "location": "Gunston",
  "notes": "Bring water"
}
```

### PATCH /events/{eventId} (league-scoped)
Requires: LeagueAdmin or global admin.

### DELETE /events/{eventId} (league-scoped)
Requires: LeagueAdmin or global admin.
