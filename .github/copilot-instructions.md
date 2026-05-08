# Copilot Instructions

## Build, Test, Lint

```bash
# Frontend
npm run build                  # Vite production build
npm run lint                   # ESLint
npm run test                   # Vitest (watch mode)
npm run test:ci                # Vitest (CI, no watch, with coverage)
npm run test -- MyComponent    # Single test by filename pattern

# Backend
dotnet build api/GameSwap_Functions.csproj
dotnet test api/GameSwap.Tests/GameSwap.Tests.csproj
dotnet test --filter "FullyQualifiedName~SlotServiceTests"  # Single test class/method

# E2E
npm run test:e2e               # Playwright
```

## Architecture

**React 19 + Vite** frontend at repo root → **.NET 8 Azure Functions** backend in `api/` → **Azure Table Storage**.

### Backend: Three-Layer Rule

```
Functions/      → HTTP triggers only: parse request, call service, format response
Services/       → ALL business logic, authorization, domain rules
Repositories/   → Azure Table Storage queries, pagination, entity mapping
```

Never put business logic in Functions. Services throw `ApiGuards.HttpError`; Functions catch via `ApiResponses.FromHttpError()`.

### Frontend: Hooks-Only State

- `useSession()` for user/membership state — no Redux, no Context
- URL hash navigation: `#home`, `#calendar`, `#manage`, `#admin`, `#umpire`
- `React.lazy` + `Suspense` for all page components
- `lib/api.js` (`apiFetch`) auto-attaches `x-league-id` header and credentials

## Key Conventions

### League Scoping (Non-Negotiable)

Every league-scoped API requires `x-league-id` header. The UI reads it from `localStorage.gameswap_leagueId`. If a route param and header disagree, reject the request.

### Azure Table Storage Keys

| Table | PartitionKey | RowKey |
|-------|-------------|--------|
| GameSwapMemberships | `{userId}` | `{leagueId}` |
| GameSwapFields | `FIELD\|{leagueId}\|{parkCode}` | `{fieldCode}` |
| GameSwapSlots | `SLOT\|{leagueId}\|{division}` | `SafeKey(...)` |
| GameSwapSlotRequests | `SLOTREQ\|{leagueId}\|{division}\|{slotId}` | `{requestId}` |
| GameSwapUmpireProfiles | `UMPIRE\|{leagueId}` | `{umpireUserId}` |
| GameSwapGameUmpireAssignments | `UMPASSIGN\|{leagueId}\|{division}\|{slotId}` | `{assignmentId}` |

Always use canonical PK prefixes. Do not add legacy read fallbacks.

### Response Envelope

```json
{ "data": [...] }
{ "data": { "items": [], "continuationToken": "...", "pageSize": 50 } }
{ "error": { "code": "SLOT_CONFLICT", "message": "...", "details": {} } }
```

### Error Codes (Frontend ↔ Backend Must Match)

| Code | HTTP | Meaning |
|------|------|---------|
| `UNAUTHENTICATED` | 401 | Not signed in |
| `FORBIDDEN` | 403 | Insufficient permissions (not ~~UNAUTHORIZED~~) |
| `SLOT_CONFLICT` | 409 | Field/time overlap |
| `DOUBLE_BOOKING` | 409 | Team has overlapping game |
| `LEAD_TIME_VIOLATION` | 409 | Reschedule/move < 72h before game |

Constants in `src/lib/constants.js` and `api/Storage/Constants.cs` must stay in sync.

### Authentication

- Azure Static Web Apps handles OAuth (AAD/Google)
- Backend reads `x-ms-client-principal` header (base64 JSON with userId, userDetails, claims)
- Dev-only fallback: `x-user-id`/`x-user-email` headers when `AZURE_FUNCTIONS_ENVIRONMENT=Development` AND localhost
- In tests, use `BuildClientPrincipal(userId)` to create the base64 header

### Time Convention

All times US/Eastern. Dates as `YYYY-MM-DD`, times as `HH:MM` (24h). No timezone conversion in the API. No midnight-crossing games.

### Roles

`LeagueAdmin`, `Coach`, `Viewer`, `Umpire`. Global admins identified by `isGlobalAdmin` from `/api/me`.

### Game Slot Workflow

Open → Confirmed (immediate accept, no pending state) → Cancelled. Acceptance creates a request with `Status=Approved`, sets slot `Status=Confirmed` with `ConfirmedRequestId` and `ConfirmedTeamId`.

### Notifications

Fire-and-forget `Task.Run` pattern. Email via SendGrid (`IEmailService`) + in-app via Table Storage (`INotificationService`). Failures logged as warnings, never block the response.

### Field Naming

Use `FieldName` in storage. Never reintroduce `Name` aliases.

### Division Shape

```json
{ "code": "10U", "name": "10U Girls", "isActive": true }
```

## Behavioral Contracts (Source of Truth)

Update these when behavior changes — backend, frontend, and contract docs in the same commit:

- `docs/contract.md` — API/UI/storage contract
- `docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md` — Game slot workflow
- `docs/PRACTICE_REQUESTS_AND_CLAIMS_BEHAVIORAL_CONTRACT.md` — Practice workflow
- `docs/SCHEDULING_ENGINE_BEHAVIORAL_CONTRACT.md` — Scheduling engine
- `docs/UMPIRE_ASSIGNMENT_BEHAVIORAL_CONTRACT.md` — Umpire assignment workflow

## Testing Patterns

**Backend (xUnit + Moq):** All services/repos are interface-based and mockable. Test helpers that create HTTP requests must include `x-ms-client-principal` header (not just `x-user-id`).

**Frontend (Vitest + React Testing Library):** Test setup in `src/__tests__/setup.js` mocks fetch, localStorage, matchMedia. Use `errorLogger.js` instead of `console.error`.

## Deployment

Azure Static Web Apps auto-deploys frontend + Functions backend. CI workflow: `.github/workflows/azure-static-web-apps-green-meadow-08081740f.yml`.
