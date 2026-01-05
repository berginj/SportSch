# GameSwap / SportsScheduler

## What does this app do?
GameSwap / SportsScheduler is a multi-tenant youth sports scheduling system that helps leagues manage field availability,
generate schedules, and coordinate game or practice slot swaps. League admins can define leagues, divisions, teams, and fields;
coaches can offer up slots and request swaps; and viewers can follow schedules without making changes. The UI is a React + Vite
front end that talks to an Azure Functions (isolated) API backed by Azure Table Storage, with Azure Static Web Apps handling
hosting and EasyAuth for authentication. The system enforces league scoping across all API calls and keeps data in canonical
Table Storage partitions, so every slot, request, field, or membership is isolated to the active league.

Key workflows include:
- **League setup**: manage leagues, divisions, teams, fields, and availability rules.
- **Scheduling**: create slots, request swaps, approve or reject requests, and export schedules.
- **Multi-tenant access**: users can belong to multiple leagues with distinct roles.

## Roles
Roles are defined in the API contract and enforced by the backend:
- **LeagueAdmin**: Full league setup and scheduling control.
- **Coach**: Offer/request slots and approve requests (with potential team assignment later).
- **Viewer**: Read-only access to schedules and available slots.
- **Global admin**: Cross-league administrative access; returned by `/api/me`.

## Contract files (source of truth)
- `docs/contract.md`: The API/UI contract, including routes, roles, storage table names, and key formats. Keep this current
  whenever endpoints or payloads change.
- `docs/scheduling.md`: Scheduling rule creation, exceptions, generation, validation reruns, and SportsEngine export steps.

## Azure components required
To run or deploy this system, you need the following Azure services configured:
- **Azure Static Web Apps**: Hosts the React UI and provides EasyAuth; proxies `/api` to the Functions backend.
- **Azure Functions (v4, isolated worker)**: Hosts the API in `api/`.
- **Azure Storage Account (Table Storage)**: Persists memberships, fields, slots, requests, and scheduling data.
- **Application Insights (recommended)**: Telemetry via the Functions worker integration.

---

Baseline prompt for future AI work on GameSwap / SportsScheduler (vNext)

You are helping me build and troubleshoot a multi-tenant youth sports GameSwap system with a React UI and an Azure Functions backend using Azure Table Storage. Your job is to propose and implement changes without introducing drift in table naming, partition keys, league scoping, API routes, field naming, or workflow state transitions. When you change behavior, update both backend and UI consistently. When I ask for full page/file replacements, give full replacements (not snippets).



Repo layout (single repo):
- UI (React + Vite): repo root
- API (Azure Functions isolated): `api/`

Scheduling workflow docs:
- See `docs/scheduling.md` for rule creation, exceptions, schedule generation, validation reruns, and SportsEngine export steps.


0) Repos and deployed apps (source of truth)

Single repo: SportSch (this repo).
Legacy repos are deprecated: gameswap-functions and Sports/softball-ui.

Deployments:
- Static Web App: sports (UI + integrated /api)
- API: Azure Functions isolated (from `api/` in this repo)

Prod calls use relative /api (SWA integrated API proxy). No custom SWA route rules.

1) Current UI file structure + behaviors (as implemented)

Canonical UI files and responsibilities:

src/lib/api.js

apiFetch() MUST:

attach credentials: "include"

attach header x-league-id from localStorage.activeLeagueId if present

parse error bodies safely
(This is already implemented; don???t regress it.) 

api

src/lib/useSession.js

useSession() calls GET /api/me

getInitialLeagueId(me):

prefers localStorage.activeLeagueId if it exists and is in me.Memberships

otherwise selects me.Memberships[0].LeagueId and persists it to localStorage.activeLeagueId
(This is canonical league persistence behavior.) 

useSession

src/TopNav.jsx

league selector dropdown persists to localStorage.activeLeagueId on change

shows memberships list: LeagueId (Role)
(This is the authoritative league picker UX.) 

TopNav

src/pages/OffersPage.jsx

shows slots, create slot, request slot

uses apiFetch from src/lib/api.js

uses Request workflow (POST requests)

loads fields via /api/leagues/{leagueId}/fields

IMPORTANT: page currently references f.Name in several places; we are normalizing to FieldName instead. 

OffersPage

src/pages/FieldsPage.jsx

lists fields via /api/leagues/{leagueId}/fields

IMPORTANT: page currently displays f.Name; normalize to f.FieldName. 

FieldsPage

src/pages/ManagePage.jsx

current ManagePage is minimal and uses DivisionsManager only (subtabs scaffold). 

ManagePage

2) Authentication and identity (current vs future)

Current:

Functions often run AuthorizationLevel.Anonymous but use EasyAuth identity when present.

Identity comes from X-MS-CLIENT-PRINCIPAL (base64 JSON) via IdentityUtil.GetMe(req).

Dev/test headers may exist (ex: x-user-id, x-user-email) but production relies on EasyAuth cookies.

Future:

Entra integration/hard enforcement later. Don???t block current work by requiring full Entra.

3) League scoping (canonical rule)

All league-scoped endpoints MUST use league context consistently.

Canonical transport:

UI sends x-league-id: <activeLeagueId> automatically via apiFetch() and persists league choice in localStorage.activeLeagueId.

Backend reads leagueId header-first, then query fallback ?leagueId=..., then optional route param fallback.

Backend rejects mismatch if both route leagueId and header leagueId exist and differ.

This is implemented via ApiGuards.RequireLeagueId(req, routeLeagueId?). Treat that as canonical.

4) Storage tables (canonical names)

Canonical table names (do not introduce new variants):

GameSwapMemberships

GameSwapFields

GameSwapSlots

GameSwapSlotRequests

5) PartitionKey/RowKey conventions (canonical keys)

Canonical PK formats:

Memberships:

Table: GameSwapMemberships

PK: <userId>

RK: <leagueId>

Columns: Role (string) (and optional metadata)

Fields:

Table: GameSwapFields

PK: FIELD|{leagueId}|{parkCode}

RK: <fieldIdSafeKey>

Canonical display name property: FieldName (string)

Optional columns: Address, Location, Notes, Surface, Lights, IsActive, timestamps

Slots:

Table: GameSwapSlots

PK: SLOT|{leagueId}|{division}

RK: deterministic <slotIdSafeKey> based on identity:

SafeKey($"{offeringTeamId}|{gameDate}|{start}|{end}|{fieldKey}")

Columns: LeagueId, Division, OfferingTeamId, GameDate, StartTime, EndTime, FieldName (or Field as legacy), GameType, Status, Notes, timestamps, plus confirmation fields.

Slot Requests:

Table: GameSwapSlotRequests

PK: SLOTREQ|{leagueId}|{division}|{slotId}

RK: <requestId> GUID

Columns: RequestingTeamId, RequestingEmail, Message, Status, RequestedAtUtc

Legacy compatibility:

Reads may temporarily fall back to legacy PKs (e.g., PartitionKey=division or PartitionKey="Fields") to avoid breaking old data, but all new writes must use canonical PKs. Any legacy support should be clearly labeled and considered temporary.

6) Workflow: Request ??? Approve (canonical)

We are implementing Request/Approve as the real swap workflow. Old ???Accept slot??? is deprecated.

State machine:

Create slot ??? Status=Open

Request slot ??? create request row Status=Pending, set slot Status=Pending

Approve one request ??? slot Status=Confirmed, store ConfirmedRequestId, ConfirmedTeamId; approved request Approved, all other requests under same slot partition Rejected

Cancel slot ??? Status=Cancelled (optionally reject pending requests)

Permissions (current):

Any league member may approve for now (intentionally permissive). We will restrict later.

7) API endpoints (stable contract; infer from existing functions)

Preferred endpoints:

GET /api/me

GET /api/slots?division=...&status=...

POST /api/slots

PATCH /api/slots/{division}/{slotId}/cancel

POST /api/slots/{division}/{slotId}/requests

GET /api/slots/{division}/{slotId}/requests

PATCH /api/slots/{division}/{slotId}/requests/{requestId}/approve

GET /api/leagues/{leagueId}/fields (route includes leagueId; backend should still accept header-first with mismatch protection)

GET /api/schedule/export?division=...&dateFrom=YYYY-MM-DD&dateTo=YYYY-MM-DD&status=...

Schedule export assumptions (CSV):
- Default status filter is Confirmed when ?status= is omitted.
- Status output maps SlotConfirmed -> Scheduled, SlotCancelled -> Cancelled, otherwise uses slot status.
- Venue is sourced from GameSwapFields DisplayName (fallback to ParkName > FieldName).
- Away team falls back to ConfirmedTeamId if AwayTeamId is empty.
- Availability slots (IsAvailability=true) are excluded.

Deprecated endpoints:

any ???Accept slot??? route. UI must not call it.

8) Canonical field naming: FieldName everywhere

Normalize everything to FieldName:

Backend fields responses should return FieldName

UI must display and select using FieldName (not Name)

During transition, backend may return both FieldName and Name alias, but the target is FieldName.

9) Debugging checklist for 403/empty data

When something returns 403 or empty:

Verify apiFetch() is used (not raw fetch). It must send credentials: include and x-league-id. 

api

Verify localStorage.activeLeagueId exists and matches a membership.

Call GET /api/me and confirm UserId and Memberships are real.

Confirm membership row exists in GameSwapMemberships:

PK = UserId

RK = LeagueId

Confirm you???re writing into canonical partitions:

FIELD|{leagueId}|{parkCode}, SLOT|{leagueId}|{division}, SLOTREQ|{leagueId}|{division}|{slotId}

10) Output expectations for future AI changes

When implementing changes:

Always state which repo(s) change

Name exact file paths

Prefer shared helpers (ApiGuards/IdentityUtil)

Provide full-file replacements when requested

Avoid creating new table names or new PK patterns unless explicitly directed
