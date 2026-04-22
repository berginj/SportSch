# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**GameSwap / SportsScheduler** is an authenticated league operations tool for youth sports scheduling with:
- React 19 + Vite frontend (repo root)
- .NET 8.0 Azure Functions backend (`api/`)
- Azure Table Storage for all data persistence

### Product Scope
- Authenticated league-member tool only (no public browsing)
- `CalendarPage` is the canonical schedule surface
- Game acceptance = immediate confirmation
- Practice requests = commissioner-reviewed workflow
- External calendar subscriptions not in scope

## Essential Commands

### Frontend Development
```bash
npm run dev                    # Start dev server (Vite)
npm run build                  # Production build
npm run lint                   # ESLint

# Testing
npm run test                   # Vitest watch mode
npm run test:ui                # Vitest UI dashboard
npm run test:coverage          # Coverage report
npm run test:ci                # CI mode (no watch)

# E2E Testing
npm run test:e2e               # Playwright tests
npm run test:e2e:ui            # Interactive mode
npm run test:e2e:debug         # Debug mode

# API Client Generation
node scripts/generate-api-client.js [spec-url]
# Default: http://localhost:7071/api/swagger.json
```

### Backend Development
```bash
# Testing
dotnet test api/GameSwap.Tests/GameSwap.Tests.csproj

# Run specific test category (e.g., scheduling engine tests)
dotnet test --filter "FullyQualifiedName~Schedule"

# Build
dotnet build api/GameSwap_Functions.csproj
```

### Running Single Tests
```bash
# Frontend: Use Vitest's pattern matching
npm run test -- SeasonWizard.test.jsx

# Backend: Use --filter
dotnet test --filter "FullyQualifiedName~NotificationServiceTests"
```

## Architecture & Critical Patterns

### Three-Layer Backend Architecture
**Layer 1: Azure Functions** - HTTP endpoints, request validation, response formatting
**Layer 2: Services** - All business logic, authorization, domain rules
**Layer 3: Repositories** - Azure Table Storage abstraction, queries, pagination

**CRITICAL**: Never put business logic in Functions. Always delegate to Services.

Example structure:
```
api/
├── Functions/           # HTTP triggers only
├── Services/            # Business logic (ISlotService, INotificationService, etc.)
├── Repositories/        # Data access (ISlotRepository, IFieldRepository, etc.)
├── Storage/             # Utilities (ApiGuards, ApiResponses, ErrorCodes, EntityMappers)
├── Models/              # Domain entities
└── Program.cs           # Dependency injection setup
```

All services/repositories use **scoped lifetime** in DI container.

### Frontend Component Organization
```
src/
├── components/          # Reusable UI (CalendarView, NotificationBell, TopNav, etc.)
├── pages/               # Route components (CalendarPage, AdminPage, ManagePage, etc.)
├── manage/              # Admin features (SeasonWizard, SlotGeneratorManager, etc.)
├── lib/                 # Utilities & hooks (api.js, useSession.js, constants.js, etc.)
└── __tests__/           # Vitest tests
```

**State Management**: React hooks only (no Redux/Context)
- `useSession()` hook for global user/membership state
- URL hash-based navigation (`#home`, `#calendar`, `#manage`)
- League ID stored in localStorage and sent as `x-league-id` header

### API Communication Pattern
**Manual fetch wrapper** (`lib/api.js`):
- Auto-attaches `x-league-id` header from localStorage
- Auto-sets `Content-Type: application/json`
- Returns structured error objects with `code`, `status`, `requestId`
- Dev mode proxies `/api` to backend via Vite

**Standard Response Envelope**:
```javascript
// Success
{ "data": [...] }

// Success with pagination
{ "data": { "items": [...], "continuationToken": "...", "pageSize": 50 } }

// Error
{ "error": { "code": "SLOT_CONFLICT", "message": "...", "details": {...} } }
```

### League Scoping (Non-Negotiable)
- All league-scoped endpoints **require** `x-league-id` header
- UI stores selected league in `localStorage.gameswap_leagueId`
- Backend validates membership/authorization per league

### Roles (Locked)
- **LeagueAdmin**: Full league management, scheduling, access approval
- **Coach**: Offer slots, accept games (may require team assignment)
- **Viewer**: Read-only access
- **Global admin**: `isGlobalAdmin` from `/api/me`, can create leagues, bypass league-scoped checks

### Constants Synchronization
Frontend (`src/lib/constants.js`) and backend (`api/Storage/Constants.cs` or similar) **MUST match exactly**:
- `SLOT_STATUS`: "Open", "Confirmed", "Cancelled", "Completed", "Postponed"
- `ROLE`: "LeagueAdmin", "Coach", "Viewer"
- `ERROR_CODES`: "UNAUTHENTICATED", "FORBIDDEN", "SLOT_CONFLICT", "DOUBLE_BOOKING", etc.

**Key Error Codes:**
- `UNAUTHENTICATED` (401) - not signed in
- `FORBIDDEN` (403) - insufficient permissions (use this, not ~~UNAUTHORIZED~~)
- `SLOT_CONFLICT` (409) - field/time overlap
- `DOUBLE_BOOKING` (409) - team has overlapping game
- `FIELD_INACTIVE` (400) - field exists but inactive
- `LEAD_TIME_VIOLATION` (409) - reschedule/move too close to game time

Mismatches cause sync bugs.

### Time Zone Convention (Locked)
All times are **US/Eastern (America/New_York)**:
- `gameDate`/`eventDate`: `YYYY-MM-DD`
- `startTime`/`endTime`: `HH:MM` (24-hour)
- API does **not** convert between zones
- Games must start and end within same calendar day (no midnight crossing)

### Lead Time Policies (Locked)
All reschedule and move operations enforce **72-hour minimum lead time**:
- Game reschedule requests: 72h before original game
- Practice move requests: 72h before original practice
- Error code: `LEAD_TIME_VIOLATION` (409)
- Provides adequate coordination time for teams and officials

### Pagination
Azure Table Storage continuation tokens:
```csharp
// Backend
public class PaginationResult<T> {
  public List<T> Items { get; set; }
  public string? ContinuationToken { get; set; }
  public int PageSize { get; set; }
}
```
Frontend passes `continuationToken` in subsequent requests to fetch next page.

### Authentication & Authorization
- **Frontend**: Azure Static Web Apps handles OAuth (AAD/Google)
- **Backend**: Functions check `x-ms-principal-id` header (set by Azure SWA)
- Membership validation via `IMembershipRepository`
- API keys for service accounts managed by `IApiKeyService`

### Error Handling
Services throw `ApiGuards.HttpError` with structured codes:
```csharp
throw new ApiGuards.HttpError(404, ErrorCodes.FIELD_NOT_FOUND, "Field not found");
```
Functions catch and convert to standardized responses via `ApiResponses.FromHttpError()`.

**Frontend Error Logging:**
Use `errorLogger.js` instead of `console.error`:
```javascript
import { logError } from '../lib/errorLogger';
logError('Operation failed', err, { context: 'details' });
```
- Development: logs to console
- Production: tracks to Application Insights only

**Global Error Boundary:**
- `src/components/ErrorBoundary.jsx` catches all React errors
- Prevents white screen crashes
- Shows user-friendly error UI with reload option

### Rate Limiting
Distributed rate limiting via **Redis-backed middleware**:
- Sliding window algorithm
- 100 requests/minute per user/IP
- Returns `429 Too Many Requests` + `Retry-After` header

### Notification System
**Two-tier architecture**:
1. **In-app notifications** (`INotificationService`) - Table Storage, read/unread tracking
2. **Email notifications** (`IEmailService`) - Queued to SendGrid, async processing

Supported events: slot created, request approved/denied, game reminder, coach onboarding, schedule published, etc.

### Audit & Telemetry
- **Audit logging** (`IAuditLogger`): Tracks all state-changing operations (user, timestamp, action, before/after)
- **Application Insights**: Frontend (Web SDK) + Backend (Worker Service)

### Testing Architecture
**Frontend (Vitest + React Testing Library)**:
- Test setup in `src/__tests__/setup.js` (mocks fetch, localStorage, matchMedia)
- **CRITICAL**: `SeasonWizard.test.jsx` used in CI/CD guard tests
- Coverage via `vitest --coverage` (v8 provider)

**Backend (xUnit)**:
- Critical path: Scheduling engine tests (`ScheduleEngineTests.cs`, `ScheduleValidationV2Tests.cs`)
- Service tests, function contract tests, integration tests
- Base class: `Integration/IntegrationTestBase.cs`

**E2E (Playwright)**:
- Config: `playwright.config.js`
- Tests: `e2e/auth.spec.js`, `e2e/calendar.spec.js`

## Source of Truth Documents

**API/Storage/Behavioral Contracts** (authoritative - update when behavior changes):
- `docs/contract.md` - API/UI/storage contract
- `docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md` - Game slot workflow
- `docs/PRACTICE_REQUESTS_AND_CLAIMS_BEHAVIORAL_CONTRACT.md` - Practice workflow
- `docs/SCHEDULING_ENGINE_BEHAVIORAL_CONTRACT.md` - Scheduling engine behavior
- `docs/scheduling.md` - Scheduling operations & exports

**Planning/Research Docs** (NOT current contract):
- `docs/ui-improvement-plan.md`
- `USER_PERSONAS.md`, `UX_IMPROVEMENTS_BY_PERSONA.md`, `UI_IMPROVEMENTS.md`

## Critical Workflows

### Commit Workflow
When behavior changes, update **backend, frontend, AND contract docs together** in same commit.

### Division Data Shape
Division DTOs use `code`, `name`, `isActive`:
```javascript
{ code: "10U", name: "10U Girls", isActive: true }
```

### Field Storage Keys
Canonical keys:
- Fields: `FIELD|{leagueId}|{parkCode}`
- Slots: `SLOT|{leagueId}|{division}`
- Slot requests: `SLOTREQ|{leagueId}|{division}|{slotId}`

Use `FieldName` as storage field; `displayName` is UI-facing composite label.

### Practice Portal
One-off enablement is **division-scoped** (not league-wide).

## Development Notes

### Avoid Over-Engineering
- Only make changes directly requested or clearly necessary
- Don't add features, refactors, or "improvements" beyond scope
- Don't add error handling for scenarios that can't happen
- Three similar lines of code > premature abstraction

### Code Quality
- Prefer editing existing files over creating new ones
- Don't create markdown/documentation files unless explicitly requested
- Use specialized tools (Read, Edit, Write) instead of bash commands for file ops
- NEVER use bash echo to communicate with user

### Lazy Loading
Frontend uses React.lazy + Suspense for code splitting:
```javascript
const CalendarPage = lazy(() => import("./pages/CalendarPage"))

<Suspense fallback={<StatusCard title="Loading" />}>
  {tab === "calendar" && <CalendarPage />}
</Suspense>
```

### OpenAPI Integration
Backend Functions use OpenAPI attributes:
```csharp
[OpenApiOperation(operationId: "GetSlots", tags: new[] { "Slots" })]
[OpenApiParameter(name: "division", In = ParameterLocation.Query, ...)]
[OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, ...)]
```

Regenerate client with: `node scripts/generate-api-client.js`

## Environment & Deployment

- **Platform**: Windows (win32)
- **Node**: >=22.12.0
- **Deployment**: Azure Static Web Apps (frontend + Functions backend auto-deploy)
- **Storage**: Azure Table Storage
- **Cache**: Redis (rate limiting)
- **Email**: SendGrid
- **Monitoring**: Application Insights

## Git Workflow

- Main branch: `main`
- Commit messages should reflect **why** not just **what**
- Include co-authorship: `Co-Authored-By: Claude Sonnet 4.5 (1M context) <noreply@anthropic.com>`
