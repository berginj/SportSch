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
- Success with pagination: { "data": { "items": [...], "continuationToken": string, "pageSize": number } }
- Failure: { "error": { "code": string, "message": string, "details"?: any } }

Note: Paginated responses use Azure Table Storage continuation tokens. Include the continuationToken in the next request to fetch the next page.

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

## Backend Architecture (authoritative)

### Three-layer architecture
The backend follows a strict three-layer architecture pattern:

**Layer 1: Azure Functions (HTTP endpoints)**
- Handle HTTP requests and responses
- Extract headers, query parameters, and request bodies
- Perform basic request validation (ApiGuards)
- Delegate all business logic to services
- Return standardized responses (ApiResponses)

**Layer 2: Services (business logic)**
- Contain all business logic and authorization rules
- Coordinate between multiple repositories
- Enforce domain constraints and validations
- Return domain objects or DTOs
- Examples: ISlotService, IAvailabilityService, IAuthorizationService

**Layer 3: Repositories (data access)**
- Abstract all Azure Table Storage operations
- Execute queries with filters and pagination
- Map TableEntity to/from domain objects
- Handle storage-specific concerns (OData filters, continuation tokens)
- Examples: ISlotRepository, IFieldRepository, IMembershipRepository

### Dependency injection
All services and repositories are registered in `api/Program.cs` with scoped lifetime:

```csharp
// Register repositories
services.AddScoped<ISlotRepository, SlotRepository>();
services.AddScoped<IFieldRepository, FieldRepository>();
services.AddScoped<IMembershipRepository, MembershipRepository>();

// Register services
services.AddScoped<ISlotService, SlotService>();
services.AddScoped<IAvailabilityService, AvailabilityService>();
services.AddScoped<IAuthorizationService, AuthorizationService>();
```

Functions receive services via constructor injection:

```csharp
public class GetSlots
{
    private readonly ISlotService _slotService;
    private readonly ILogger _log;

    public GetSlots(ISlotService slotService, ILoggerFactory loggerFactory)
    {
        _slotService = slotService;
        _log = loggerFactory.CreateLogger<GetSlots>();
    }

    [Function("GetSlots")]
    public async Task<HttpResponseData> Run([HttpTrigger(...)] HttpRequestData req)
    {
        // Extract context, validate, delegate to service
        var result = await _slotService.QuerySlotsAsync(request, context);
        return ApiResponses.Ok(req, result);
    }
}
```

### Pagination pattern
Repositories use Azure Table Storage continuation tokens for efficient pagination:

```csharp
public interface ISlotRepository
{
    Task<PaginationResult<TableEntity>> QuerySlotsAsync(
        SlotQueryFilter filter,
        string? continuationToken = null);
}

public class PaginationResult<T>
{
    public List<T> Items { get; set; }
    public string? ContinuationToken { get; set; }
    public int PageSize { get; set; }
}
```

### Error handling
Services throw `ApiGuards.HttpError` with structured error codes:

```csharp
if (!await _fieldRepo.FieldExistsAsync(leagueId, parkCode, fieldCode))
    throw new ApiGuards.HttpError(404, ErrorCodes.FIELD_NOT_FOUND,
        $"Field not found: {parkCode}/{fieldCode}");
```

Functions catch and convert to standard error responses:

```csharp
catch (ApiGuards.HttpError ex)
{
    return ApiResponses.FromHttpError(req, ex);
}
```

### Utility classes
**Storage/EntityMappers.cs**: Maps TableEntity to/from domain objects
**Storage/ODataFilterBuilder.cs**: Builds OData filter strings for queries
**Storage/FieldKeyUtil.cs**: Parses and validates field keys
**Storage/PaginationUtil.cs**: Helper for paginated queries
**Storage/ErrorCodes.cs**: Centralized error code constants

---

## Frontend Architecture (authoritative)

### Component composition pattern
Large page components are split into focused sub-components:

**Example: AdminPage.jsx**
- Main component manages state and data fetching (736 lines, reduced from 1,355)
- Sub-components handle UI rendering:
  - AccessRequestsSection.jsx (155 lines)
  - CoachAssignmentsSection.jsx (112 lines)
  - CsvImportSection.jsx (175 lines)
  - GlobalAdminSection.jsx (420 lines with 4 subsections)

**Benefits:**
- Improved readability and maintainability
- Easier testing of individual sections
- Better code organization
- Parallel development of features

### Custom hooks pattern
Extract reusable stateful logic into custom hooks:

**usePagination** - Handles paginated API responses with load-more pattern:
```javascript
export function usePagination(fetchFunction, initialPageSize = 50) {
  const [items, setItems] = useState([]);
  const [continuationToken, setContinuationToken] = useState(null);
  const [loading, setLoading] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [error, setError] = useState(null);

  const loadPage = useCallback(async (token = null, append = false) => {
    setLoading(true);
    try {
      const result = await fetchFunction(token, initialPageSize);
      const data = result?.data || result;
      const newItems = data?.items || data || [];
      const nextToken = data?.continuationToken || null;

      setItems(prev => append ? [...prev, ...newItems] : newItems);
      setContinuationToken(nextToken);
      setHasMore(!!nextToken);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [fetchFunction, initialPageSize]);

  const loadMore = useCallback(() => {
    if (continuationToken && !loading) loadPage(continuationToken, true);
  }, [continuationToken, loading, loadPage]);

  return { items, loading, error, hasMore, loadMore, reset, initialLoad };
}
```

**useKeyboardShortcuts** - Application-wide keyboard shortcuts:
```javascript
export function useKeyboardShortcuts(shortcuts) {
  useEffect(() => {
    function handleKeyDown(e) {
      // Skip if user is typing in an input field (unless Ctrl is pressed)
      if (['INPUT', 'TEXTAREA', 'SELECT'].includes(e.target.tagName) && !e.ctrlKey) {
        return;
      }

      // Handle single keys, combinations (Ctrl+K), and sequences (g h)
      for (const [key, handler] of Object.entries(shortcuts)) {
        if (matchesShortcut(e, key)) {
          e.preventDefault();
          handler();
        }
      }
    }

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [shortcuts]);
}
```

**useAccessRequests, useCoachAssignments, useCsvImport** - Domain-specific hooks that encapsulate business logic and state for admin sections.

### Form validation pattern
Use the validation library (`lib/validation.js`) for all form inputs:

```javascript
import { validateField, validateForm, schemas } from '../lib/validation';

// Validate single field
const error = validateField('email', 'invalid-email', schemas.email);
if (error) {
  setEmailError(error);
}

// Validate entire form
const errors = validateForm(formData, schemas.league);
if (Object.keys(errors).length > 0) {
  setFormErrors(errors);
  return;
}
```

**Available validators:**
- required, email, minLength, maxLength
- pattern, number, min, max
- date, time, custom

**Predefined schemas:** league, team, field, slot, user, division, event

### Keyboard shortcuts
Application-wide shortcuts for common navigation:

| Shortcut | Action |
|----------|--------|
| g h | Go to home |
| g c | Go to calendar |
| g m | Go to manager |
| g a | Go to admin |
| ? | Show shortcuts modal |

Implementation: Include KeyboardShortcutsModal in your layout and register shortcuts with useKeyboardShortcuts.

### Mobile-first design principles
**Touch targets:** All interactive elements must be at least 44px (2.75rem) in height for comfortable touch interaction.

**Responsive breakpoints:**
- Mobile: < 640px
- Tablet: 640px - 1024px
- Desktop: > 1024px

**Accessibility requirements:**
- ARIA labels on all interactive elements
- Skip links for keyboard navigation
- Focus indicators (2px blue ring with 2px offset)
- Sufficient color contrast (WCAG AA minimum)

**Loading states:** Use SkeletonLoader component for content placeholders
**Empty states:** Use EmptyState component with contextual messages
**Error states:** Use ErrorCard component with actionable error messages

### State management
**Local state:** Use useState for component-specific state
**Shared state:** Pass via props or use custom hooks
**Server state:** Use custom hooks (usePagination, useAccessRequests, etc.) that encapsulate fetch logic

**Avoid:**
- Global state libraries (Redux, Zustand) unless absolutely necessary
- Prop drilling more than 2 levels deep (extract to custom hook instead)

---

## Testing Guidelines

### Frontend tests (Vitest)
All new components and utilities must include test coverage.

**Test infrastructure:**
- Framework: Vitest with jsdom environment
- Component testing: @testing-library/react
- Assertions: @testing-library/jest-dom
- Setup: src/__tests__/setup.js (mocks fetch, localStorage, window)

**Running tests:**
```bash
npm test              # Watch mode
npm run test:coverage # Coverage report
npm run test:ci       # CI mode (no watch)
```

**Test organization:**
- Component tests: `src/__tests__/components/<ComponentName>.test.jsx`
- Hook tests: `src/__tests__/hooks/<hookName>.test.js`
- Utility tests: `src/__tests__/lib/<utilName>.test.js`

**Example component test:**
```javascript
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import LeaguePicker from '../../components/LeaguePicker';

describe('LeaguePicker', () => {
  it('calls onChange when selection changes', () => {
    const onChange = vi.fn();
    const leagues = [{ leagueId: 'ARL', leagueName: 'Arlington' }];

    render(<LeaguePicker memberships={leagues} value="" onChange={onChange} />);

    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: 'ARL' } });

    expect(onChange).toHaveBeenCalledWith('ARL');
  });
});
```

**Coverage target:** 70%+ for new/refactored code

### Backend tests
Integration tests validate end-to-end workflows:
- Use in-memory Azure Storage Emulator (Azurite)
- Test complete workflows (create slot → create request → approve)
- Verify authorization rules and error handling

---

## Storage tables + keys (authoritative)

Canonical table names (do not introduce new variants):
- GameSwapMemberships
- GameSwapFields
- GameSwapSlots
- GameSwapSlotRequests
- GameSwapAccessRequests
- GameSwapLeagues
- GameSwapDivisions
- GameSwapTeams
- GameSwapUsers
- GameSwapEvents
- GameSwapScheduleRuns
- GameSwapFieldAvailabilityRules
- GameSwapFieldAvailabilityExceptions
- GameSwapFieldAvailabilityAllocations
- GameSwapLeagueInvites
- GameSwapTeamContacts
- GameSwapSeasons
- GameSwapSeasonDivisions
- GameSwapGlobalAdmins
- GameSwapLeagueBackups

PartitionKey/RowKey conventions (canonical):
- Memberships: PK = `<userId>`, RK = `<leagueId>`
- Fields: PK = `FIELD|{leagueId}|{parkCode}`, RK = `<fieldCode>` (display name is `DisplayName`, defaults to `ParkName > FieldName`)
- Slots: PK = `SLOT|{leagueId}|{division}`, RK = deterministic slot id (SafeKey of offeringTeamId + date + start + end + fieldKey)
- Slot Requests: PK = `SLOTREQ|{leagueId}|{division}|{slotId}`, RK = `<requestId GUID>`
- Access Requests: PK = `ACCESSREQ|{leagueId}`, RK = `<userId>`
- Availability Rules: PK = `AVAILRULE|{leagueId}|{fieldKey}`, RK = `<ruleId>`
- Availability Exceptions: PK = `AVAILRULEEX|{ruleId}`, RK = `<exceptionId>`
- Availability Allocations: PK = `ALLOC|{leagueId}|{scope}|{fieldKeySafe}`, RK = `<allocationId>` (fieldKeySafe replaces `/` with `|`)
- Users: PK = `USER`, RK = `<userId>`
- League Backups: PK = `LEAGUEBACKUP`, RK = `<leagueId>`

Legacy compatibility:
- Reads may fall back to legacy PKs when needed, but all new writes must use canonical PKs above.

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
    "homeLeagueId": "ARL",
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
| GET | /league/backup | `Functions/LeagueBackupFunctions.cs` | Get backup summary (requires `x-league-id`, LeagueAdmin). |
| POST | /league/backup | `Functions/LeagueBackupFunctions.cs` | Save/overwrite league backup (requires `x-league-id`, LeagueAdmin). |
| POST | /league/backup/restore | `Functions/LeagueBackupFunctions.cs` | Restore fields/divisions/season from backup (requires `x-league-id`, LeagueAdmin). |
| GET | /admin/leagues | `Functions/LeaguesFunctions.cs` | Global admin list of leagues. |
| POST | /admin/leagues | `Functions/LeaguesFunctions.cs` | Global admin create league. |
| GET | /global/leagues | `Functions/LeaguesFunctions.cs` | Global admin list of leagues (alt route). |
| POST | /global/leagues | `Functions/LeaguesFunctions.cs` | Global admin create league (alt route). |
| PATCH | /admin/leagues/{leagueId}/season | `Functions/LeaguesFunctions.cs` | League/global admin update season settings. |
| PATCH | /league/season | `Functions/LeaguesFunctions.cs` | League/global admin update season settings (header-scoped). |
| PATCH | /global/leagues/{leagueId}/season | `Functions/LeaguesFunctions.cs` | Global admin update season settings (alt route). |
| DELETE | /global/leagues/{leagueId} | `Functions/LeaguesFunctions.cs` | Global admin delete league (data wipe). |
| GET | /admin/globaladmins | `Functions/GlobalAdminsFunctions.cs` | Global admin list. |
| POST | /admin/globaladmins | `Functions/GlobalAdminsFunctions.cs` | Add global admin. |
| DELETE | /admin/globaladmins/{userId} | `Functions/GlobalAdminsFunctions.cs` | Remove global admin. |
| GET | /globaladmins | `Functions/GlobalAdminsFunctions.cs` | Global admin list (alt route). |
| POST | /globaladmins | `Functions/GlobalAdminsFunctions.cs` | Add global admin (alt route). |
| DELETE | /globaladmins/{userId} | `Functions/GlobalAdminsFunctions.cs` | Remove global admin (alt route). |
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
| POST | /invites | `Functions/LeagueInvitesFunctions.cs` | Create invite (alt route; requires `x-league-id`, LeagueAdmin). |
| GET | /admin/users | `Functions/AdminUsersFunctions.cs` | Global admin list users. |
| POST | /admin/users | `Functions/AdminUsersFunctions.cs` | Global admin upsert user profile + home role. |
| GET | /users | `Functions/AdminUsersFunctions.cs` | Global admin list users (alt route). |
| POST | /users | `Functions/AdminUsersFunctions.cs` | Global admin upsert user profile + home role (alt route). |
| POST | /invites/accept | `Functions/LeagueInvitesFunctions.cs` | Accept invite. |
| GET | /memberships | `Functions/MembershipsFunctions.cs` | List memberships (requires `x-league-id`, LeagueAdmin). Global admins can use `all=true` to list across leagues. |
| PATCH | /memberships/{userId} | `Functions/MembershipsFunctions.cs` | Update membership (requires `x-league-id`, LeagueAdmin). |
| GET | /divisions | `Functions/DivisionsFunctions.cs` | List divisions (requires `x-league-id`). |
| POST | /divisions | `Functions/DivisionsFunctions.cs` | Create division (requires `x-league-id`, LeagueAdmin). |
| PATCH | /divisions/{code} | `Functions/DivisionsFunctions.cs` | Update division (requires `x-league-id`, LeagueAdmin). |
| GET | /divisions/{code}/season | `Functions/DivisionsFunctions.cs` | Get division season overrides (requires `x-league-id`, LeagueAdmin). |
| PATCH | /divisions/{code}/season | `Functions/DivisionsFunctions.cs` | Update division season overrides (requires `x-league-id`, LeagueAdmin). |
| GET | /divisions/templates | `Functions/DivisionsFunctions.cs` | List division templates (requires `x-league-id`). |
| PATCH | /divisions/templates | `Functions/DivisionsFunctions.cs` | Update division templates (requires `x-league-id`, LeagueAdmin). |
| GET | /teams | `Functions/TeamsFunctions.cs` | List teams (requires `x-league-id`). |
| POST | /teams | `Functions/TeamsFunctions.cs` | Create team (requires `x-league-id`, LeagueAdmin). |
| PATCH | /teams/{division}/{teamId} | `Functions/TeamsFunctions.cs` | Update team (requires `x-league-id`, LeagueAdmin). |
| DELETE | /teams/{division}/{teamId} | `Functions/TeamsFunctions.cs` | Delete team (requires `x-league-id`, LeagueAdmin). |
| GET | /fields | `Functions/FieldsFunctions.cs` | List fields (requires `x-league-id`). |
| POST | /fields | `Functions/FieldsFunctions.cs` | Create field (requires `x-league-id`, LeagueAdmin). |
| PATCH | /fields/{parkCode}/{fieldCode} | `Functions/FieldsFunctions.cs` | Update field address details (requires `x-league-id`, LeagueAdmin). |
| DELETE | /fields/{parkCode}/{fieldCode} | `Functions/FieldsFunctions.cs` | Delete field (requires `x-league-id`, LeagueAdmin). |
| POST | /import/fields | `Functions/ImportFields.cs` | CSV field import (requires `x-league-id`, LeagueAdmin). |
| POST | /import/teams | `Functions/ImportTeams.cs` | CSV teams import (requires `x-league-id`, LeagueAdmin). |
| POST | /import/slots | `Functions/ImportSlots.cs` | CSV slot import (requires `x-league-id`, LeagueAdmin). |
| POST | /import/availability-slots | `Functions/ImportAvailabilitySlots.cs` | CSV availability slot import (requires `x-league-id`, LeagueAdmin). |
| POST | /import/availability-allocations | `Functions/AvailabilityAllocationsFunctions.cs` | CSV availability allocation import (requires `x-league-id`, LeagueAdmin). |
| GET | /slots | `Functions/GetSlots.cs` | List slots (requires `x-league-id`). |
| POST | /slots | `Functions/CreateSlot.cs` | Create slot (requires `x-league-id`, Coach or LeagueAdmin). |
| PATCH | /slots/{division}/{slotId}/cancel | `Functions/CancelSlot.cs` | Cancel slot (requires `x-league-id`, LeagueAdmin). |
| GET | /slots/{division}/{slotId}/requests | `Functions/GetSlotRequests.cs` | List requests for slot (requires `x-league-id`). |
| POST | /slots/{division}/{slotId}/requests | `Functions/CreateSlotRequest.cs` | Request slot (requires `x-league-id`, Coach). |
| PATCH | /slots/{division}/{slotId}/requests/{requestId}/approve | `Functions/ApproveSlotRequest.cs` | Approve slot request (requires `x-league-id`, offering coach, LeagueAdmin, or global admin). |
| GET | /availability/allocations | `Functions/AvailabilityAllocationsFunctions.cs` | List availability allocations (requires `x-league-id`, LeagueAdmin). |
| POST | /availability/allocations/clear | `Functions/AvailabilityAllocationsFunctions.cs` | Clear allocations (requires `x-league-id`, LeagueAdmin). |
| POST | /availability/allocations/slots/preview | `Functions/AvailabilityAllocationSlotsFunctions.cs` | Preview slots generated from allocations (requires `x-league-id`, LeagueAdmin). |
| POST | /availability/allocations/slots/apply | `Functions/AvailabilityAllocationSlotsFunctions.cs` | Generate slots from allocations (requires `x-league-id`, LeagueAdmin). |
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
| POST | /schedule/wizard/preview | `Functions/ScheduleWizardFunctions.cs` | Preview wizard-built season schedule (requires `x-league-id`, LeagueAdmin). |
| POST | /schedule/wizard/apply | `Functions/ScheduleWizardFunctions.cs` | Apply wizard-built season schedule (requires `x-league-id`, LeagueAdmin). |
| POST | /schedule/slots/preview | `Functions/SlotGenerationFunctions.cs` | Preview generated availability slots (requires `x-league-id`, LeagueAdmin). |
| POST | /schedule/slots/apply | `Functions/SlotGenerationFunctions.cs` | Generate availability slots (requires `x-league-id`, LeagueAdmin). |
| POST | /availability-slots/clear | `Functions/ClearAvailabilitySlots.cs` | Delete availability slots for a division/date range (requires `x-league-id`, LeagueAdmin). |
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

### League backup (league-scoped)
Backups capture fields, divisions, and league season settings for restore.

### GET /league/backup
Requires: LeagueAdmin or global admin.

Query (optional):
- `includeSnapshot` = `1` or `true` to include the full snapshot JSON.

Response
```json
{
  "data": {
    "exists": true,
    "backup": {
      "leagueId": "ARL",
      "savedUtc": "2026-02-01T12:00:00.0000000Z",
      "savedBy": "admin@example.com",
      "fieldsCount": 12,
      "divisionsCount": 6,
      "season": {
        "springStart": "2026-03-01",
        "springEnd": "2026-06-30",
        "fallStart": "2026-08-15",
        "fallEnd": "2026-11-01",
        "gameLengthMinutes": 120,
        "blackouts": []
      }
    }
  }
}
```

### POST /league/backup
Requires: LeagueAdmin or global admin.

Notes
- Overwrites the previous backup for the league (one backup per league).

### POST /league/backup/restore
Requires: LeagueAdmin or global admin.

Notes
- Overwrites fields, divisions, and league season settings with the saved snapshot.
- Does not modify slots, events, teams, or access requests.

Response
```json
{
  "data": {
    "restored": true,
    "fieldsRestored": 12,
    "divisionsRestored": 6
  }
}
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

### Admin: GET /admin/users
Requires: global admin.  
Query: `search` (optional; matches userId, email, homeLeagueId)

Response
```json
{
  "data": [
    {
      "userId": "aad|...",
      "email": "admin@example.com",
      "homeLeagueId": "ARL",
      "homeLeagueRole": "LeagueAdmin",
      "updatedUtc": "2026-01-05T12:00:00Z"
    }
  ]
}
```

### Admin: POST /admin/users
Requires: global admin.

Body
```json
{
  "userId": "aad|...",
  "email": "admin@example.com",
  "homeLeagueId": "ARL",
  "role": "LeagueAdmin"
}
```

Notes
- `role` is applied to the `homeLeagueId` membership.

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
Query (optional):
- `status` - Filter by status (default: Pending)
- `continuationToken` - Pagination token from previous response
- `pageSize` - Number of items per page (default: 50)

Global admin: list across all leagues
Query: `all=true` (requires global admin; ignores league header scope)

Response supports pagination for large result sets. See standard pagination format in "Standard response envelope" section.

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

### POST /invites (league-scoped)
Header: x-league-id
Requires: LeagueAdmin or global admin.

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

### Admin: GET /memberships?all=true
Requires: global admin.
Query (optional):
- `search` - Filter by userId, email, or leagueId
- `leagueId` - Filter by specific league
- `role` - Filter by role (LeagueAdmin, Coach, Viewer)
- `continuationToken` - Pagination token from previous response
- `pageSize` - Number of items per page (default: 50)

Response (paginated when large result sets)
```json
{
  "data": {
    "items": [
      { "userId": "...", "email": "...", "leagueId": "ARL", "role": "LeagueAdmin" },
      { "userId": "...", "email": "...", "leagueId": "ARL", "role": "Coach", "team": { "division": "10U", "teamId": "TIGERS" } }
    ],
    "continuationToken": "xyz789...",
    "pageSize": 50
  }
}
```

Note: For small result sets (< pageSize), response may return simple array format for backward compatibility.

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

### GET /divisions/{code}/season (league-scoped)
Requires: LeagueAdmin or global admin.

Response
```json
{
  "data": {
    "season": {
      "springStart": "2026-03-01",
      "springEnd": "2026-06-30",
      "fallStart": "2026-08-15",
      "fallEnd": "2026-11-01",
      "gameLengthMinutes": 120,
      "blackouts": []
    }
  }
}
```

### PATCH /divisions/{code}/season (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{
  "season": {
    "springStart": "2026-03-01",
    "springEnd": "2026-06-30",
    "fallStart": "2026-08-15",
    "fallEnd": "2026-11-01",
    "gameLengthMinutes": 120,
    "blackouts": []
  }
}
```

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

Notes
- `displayName` defaults to `"{ParkName} > {FieldName}"` when not supplied.

### POST /fields (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
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
```

### PATCH /fields/{parkCode}/{fieldCode} (league-scoped)
Requires: LeagueAdmin or global admin.

### DELETE /fields/{parkCode}/{fieldCode} (league-scoped)
Requires: LeagueAdmin or global admin.

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
- Response may include `warnings` for duplicate parkName+fieldName matches (the importer will reuse the existing fieldKey).

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

Slots are **open game offers/requests** placed on the calendar by a coach (or LeagueAdmin). Another coach can see an open slot and **request** it (via `POST /slots/{division}/{slotId}/requests`). Creating a request moves the slot to **Pending** until approved.

Slot status strings
- Open
- Pending (has one or more requests awaiting approval)
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
- Response may include `warnings` for skipped rows that overlap existing field/time slots or duplicates in the CSV.

### POST /import/availability-slots (league-scoped)
Requires: LeagueAdmin or global admin.

Body: raw CSV (`Content-Type: text/csv`)

Required columns:
- `division`
- `gameDate` (YYYY-MM-DD)
- `startTime` (HH:MM)
- `endTime` (HH:MM)
- `fieldKey` (format `parkCode/fieldCode`)

Optional columns:
- `notes`

Import behavior:
- Creates availability slots (`IsAvailability=true`, `Status=Open`) for the specified league/division.
- `fieldKey` must match an imported field.
- Response may include `warnings` for skipped rows that overlap existing field/time slots or duplicates in the CSV.

### POST /availability-slots/clear (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{
  "division": "10U",
  "dateFrom": "2026-03-01",
  "dateTo": "2026-06-30",
  "fieldKey": "gunston/turf"
}
```

Notes
- `fieldKey` is optional; when omitted, clears all availability for the division/date range.
- Only availability slots with Status=Open are deleted.

### GET /slots (league-scoped)
Query (all optional):
- `division` - Filter by division code
- `status` - Comma-separated list (e.g., "Open,Confirmed")
- `dateFrom` - Start date filter (YYYY-MM-DD)
- `dateTo` - End date filter (YYYY-MM-DD)
- `continuationToken` - Pagination token from previous response
- `pageSize` - Number of items per page (default: 50, max: 100)

Requires: member (Viewer allowed).

Visibility:
- Confirmed slots are visible to all league members (including Viewer).

Default behavior:
- If `status` is omitted, the API returns **Open + Confirmed** slots.
- To see cancelled slots, pass `status=Cancelled`.

Response (non-paginated, for backward compatibility when pageSize not specified)
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

Response (paginated, when pageSize is specified)
```json
{
  "data": {
    "items": [
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
    ],
    "continuationToken": "abc123...",
    "pageSize": 50
  }
}
```

To fetch the next page, include the continuationToken in the next request:
```
GET /slots?division=10U&continuationToken=abc123...&pageSize=50
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
Approval endpoint.

Approval semantics:
- A request created via `POST /slots/{division}/{slotId}/requests` is `Pending`.
- Approving sets the slot to `Confirmed`, marks the approved request `Approved`, and rejects other pending requests for that slot.
- If the slot is already confirmed for the given requestId, this endpoint is idempotent and returns ok.
- If the slot is confirmed for a different requestId, it returns 409 conflict.

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

## 8aa) Field availability allocations (admin)

Allocations represent time blocks reserved for the league or for a specific division. Allocations are turned into availability slots for scheduling.

### POST /import/availability-allocations (league-scoped)
Requires: LeagueAdmin or global admin.

Body: raw CSV (`Content-Type: text/csv`)

Required columns:
- `fieldKey` (format `parkCode/fieldCode`)
- `dateFrom` (YYYY-MM-DD)
- `dateTo` (YYYY-MM-DD)
- `startTime` (HH:MM)
- `endTime` (HH:MM)

Optional columns:
- `division` (blank or `LEAGUE` = league-wide allocation)
- `daysOfWeek` (comma-separated, e.g. `Mon,Wed`)
- `notes`
- `isActive` (true/false)

Import behavior:
- Creates allocations (each CSV row is a new allocation id).
- `division` values are treated as allocation scope; `LEAGUE` applies to all divisions.
- Response may include `warnings` for duplicate allocations (skipped) or overlaps with existing non-availability slots.

### GET /availability/allocations (league-scoped)
Requires: LeagueAdmin or global admin.
Query: `division` (scope filter), `fieldKey` (optional)

### POST /availability/allocations/clear (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{
  "scope": "LEAGUE",
  "dateFrom": "2026-03-01",
  "dateTo": "2026-06-30",
  "fieldKey": "gunston/turf"
}
```

Notes
- `scope` should be `LEAGUE` or a division code.
- `fieldKey` is optional; when omitted, clears allocations for all fields within the scope/date range.

### POST /availability/allocations/slots/preview (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{
  "division": "10U",
  "dateFrom": "2026-03-01",
  "dateTo": "2026-06-30",
  "fieldKey": "gunston/turf"
}
```

Response
```json
{
  "data": {
    "slots": [
      { "gameDate": "2026-03-03", "startTime": "17:00", "endTime": "19:00", "fieldKey": "gunston/turf", "division": "10U" }
    ],
    "conflicts": []
  }
}
```

### POST /availability/allocations/slots/apply (league-scoped)
Requires: LeagueAdmin or global admin.

Body: same as preview.

Response
```json
{
  "data": {
    "created": [
      { "gameDate": "2026-03-03", "startTime": "17:00", "endTime": "19:00", "fieldKey": "gunston/turf", "division": "10U" }
    ],
    "conflicts": []
  }
}
```

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

Notes
- Saving league season settings also propagates the season defaults to all divisions in the league.

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

### POST /schedule/wizard/preview (league-scoped)
Requires: LeagueAdmin or global admin.

Body
```json
{
  "division": "10U",
  "seasonStart": "2026-03-01",
  "seasonEnd": "2026-06-30",
  "poolStart": "2026-06-22",
  "poolEnd": "2026-06-28",
  "bracketStart": "2026-06-29",
  "bracketEnd": "2026-07-05",
  "minGamesPerTeam": 8,
  "poolGamesPerTeam": 2,
  "preferredWeeknights": ["Mon", "Wed"],
  "maxGamesPerWeek": 2,
  "noDoubleHeaders": true,
  "balanceHomeAway": true
}
```

Response
```json
{
  "data": {
    "summary": {
      "regularSeason": { "phase": "Regular Season", "slotsTotal": 20, "slotsAssigned": 18, "matchupsTotal": 24, "matchupsAssigned": 18, "unassignedSlots": 2, "unassignedMatchups": 6 },
      "poolPlay": { "phase": "Pool Play", "slotsTotal": 6, "slotsAssigned": 6, "matchupsTotal": 6, "matchupsAssigned": 6, "unassignedSlots": 0, "unassignedMatchups": 0 },
      "bracket": { "phase": "Bracket", "slotsTotal": 3, "slotsAssigned": 3, "matchupsTotal": 3, "matchupsAssigned": 3, "unassignedSlots": 0, "unassignedMatchups": 0 },
      "totalSlots": 29,
      "totalAssigned": 27
    },
    "assignments": [
      { "phase": "Regular Season", "slotId": "slot_1", "gameDate": "2026-04-10", "startTime": "18:00", "endTime": "19:30", "fieldKey": "gunston/turf", "homeTeamId": "TIGERS", "awayTeamId": "EAGLES", "isExternalOffer": false }
    ],
    "unassignedSlots": [],
    "unassignedMatchups": [],
    "warnings": []
  }
}
```

### POST /schedule/wizard/apply (league-scoped)
Requires: LeagueAdmin or global admin.

Body: same as preview.

Scheduler export formats
- Internal CSV: division, gameDate, startTime, endTime, fieldKey, homeTeamId, awayTeamId, isExternalOffer
- SportsEngine CSV template (`docs/sportsenginetemplate.csv`): Event Type, Date, Start Time, End Time, Duration (minutes), Home Team, Away Team, Venue, Status (other event-only columns left blank)

Validation + apply rules
- `/schedule/preview` returns `failures` when validation warnings exist.
- `/schedule/apply` fails with `SCHEDULE_VALIDATION_FAILED` if validation issues exist.
- `/schedule/validate` runs validations against scheduled games (non-availability slots only).

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

---

---

## Development Best Practices

### Backend development guidelines

**When creating new endpoints:**
1. Create service interface in `api/Services/I<ServiceName>.cs`
2. Implement service in `api/Services/<ServiceName>.cs`
3. Create repository interface in `api/Repositories/I<RepositoryName>.cs` (if needed)
4. Implement repository in `api/Repositories/<RepositoryName>.cs` (if needed)
5. Register services in `api/Program.cs`
6. Create Azure Function that delegates to service
7. Add endpoint to contract.md endpoint index

**Authorization checks:**
- Use `IAuthorizationService` for all permission checks
- Never inline authorization logic in functions
- Common patterns: `RequireNotViewerAsync()`, `CanCreateSlotAsync()`, `CanApproveRequestAsync()`

**Error handling:**
- Throw `ApiGuards.HttpError` with error codes from `ErrorCodes.cs`
- Never return generic "Internal error" messages without logging details
- Use structured logging with correlation IDs

**Testing:**
- Write integration tests for complex workflows
- Test authorization rules thoroughly
- Verify error responses include proper error codes

### Frontend development guidelines

**When creating new pages:**
1. Start with a single page component (don't prematurely split)
2. If the component exceeds ~300 lines, extract sub-components
3. Extract stateful logic into custom hooks when reused 2+ times
4. Use predefined validation schemas from `lib/validation.js`
5. Add keyboard shortcuts for common actions
6. Include loading, empty, and error states

**Component structure:**
```jsx
// Page component (state management)
export default function MyPage() {
  const [data, setData] = useState([]);
  const { items, loading, hasMore, loadMore } = usePagination(fetchData);

  return (
    <div>
      <MySubSection data={data} onUpdate={handleUpdate} />
      <Pagination hasMore={hasMore} loading={loading} onLoadMore={loadMore} />
    </div>
  );
}

// Sub-component (presentation only)
function MySubSection({ data, onUpdate }) {
  return <div>...</div>;
}
```

**API integration:**
- Use `apiFetch()` from `lib/api.js` for all API calls
- Let `apiFetch` handle league-id header injection
- Handle errors with user-friendly messages (not raw API errors)
- Use `usePagination` hook for paginated endpoints

**Styling:**
- Use Tailwind utility classes
- Follow mobile-first approach (design for mobile, enhance for desktop)
- Maintain 44px minimum touch target size
- Use semantic color classes (`text-error`, `bg-success`, etc.)

**Accessibility:**
- Include ARIA labels on all interactive elements
- Test keyboard navigation (Tab, Enter, Space, Escape)
- Ensure sufficient color contrast
- Provide skip links for main content

### Quality assurance checklist

Before submitting code for review:
- [ ] Backend: All endpoints registered in contract.md
- [ ] Backend: Services and repositories use interfaces
- [ ] Backend: Authorization checks use IAuthorizationService
- [ ] Backend: Error codes used from ErrorCodes.cs
- [ ] Frontend: Component split if >300 lines
- [ ] Frontend: Form validation uses validation library
- [ ] Frontend: Paginated lists use usePagination hook
- [ ] Frontend: Mobile responsive (test at 375px width)
- [ ] Frontend: Keyboard navigation works
- [ ] Tests: New code has 70%+ coverage
- [ ] Tests: Authorization rules tested
- [ ] UI: Loading/empty/error states included
- [ ] UI: User-friendly error messages
- [ ] Docs: contract.md updated if API changed

---

## 10) Permissions matrix (summary)

Legend: R = read, W = write/modify, A = approve/deny/admin action.

| Area | Viewer | Coach | LeagueAdmin | GlobalAdmin |
| --- | --- | --- | --- | --- |
| Access requests (self) | W | W | W | W |
| Access requests (admin) | - | - | A | A |
| Memberships list/update | - | - | W | W |
| Divisions | R | R | W | W |
| Fields | R | R | W | W |
| League backups | - | - | W | W |
| Availability rules/exceptions | - | - | W | W |
| Slots list | R | R | R | R |
| Create slots | - | W | W | W |
| Slot requests | - | W | W | W |
| Approve slot requests | - | W (own slot) | W | W |
| Schedule preview/apply | - | - | W | W |
| Schedule validate | - | - | W | W |
| Events | R | R | W | W |
| Users (home league) | - | - | - | W |
