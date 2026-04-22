# Documentation Update Checklist
**Date:** 2026-04-22
**Purpose:** Track which docs need updates to reflect implemented changes

---

## ❌ DOCUMENTATION NOT YET UPDATED

The following documents need updates to reflect the code changes we implemented:

---

## 1. docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md

### ⚠️ NEEDS UPDATE: Section 6.2 (Accept Slot)

**What changed:**
- Request/slot confirmation is now atomic (slot updated FIRST, request created SECOND)
- Double-booking check now includes Open slots, not just Confirmed
- All team ID fields checked (Home, Away, Offering, Confirmed)

**Current text (line 85-90):**
```markdown
On success, current canonical behavior is immediate confirm:

- create request row with status `Approved`,
- set slot status to `Confirmed`,
- set `ConfirmedTeamId` and `ConfirmedRequestId`,
- best-effort deny other pending requests for the same slot.
```

**Should be updated to:**
```markdown
On success, current canonical behavior is immediate confirm:

- **Atomicity guarantee**: Slot MUST be updated to Confirmed status BEFORE request is created
- If slot update fails (concurrent acceptance), request is NOT created (no orphaned requests)
- create request row with status `Approved` (only after slot confirmed)
- set slot status to `Confirmed`,
- set `ConfirmedTeamId` and `ConfirmedRequestId`,
- best-effort deny other pending requests for the same slot.

Double-booking prevention:
- Checks both Confirmed AND Open slots for team conflicts
- Validates all team identifier fields (HomeTeamId, AwayTeamId, OfferingTeamId, ConfirmedTeamId)
- Prevents rapid acceptance of overlapping Open slots
```

**Location:** Lines 85-90

---

### ⚠️ NEEDS UPDATE: Section 6.6 (Update Slot)

**What changed:**
- UpdateSlot now validates team double-booking, not just field conflicts

**Current text (line 118-127):**
```markdown
`PATCH /slots/{division}/{slotId}` MUST:

- be admin/global only,
- reject edits to `Cancelled` slots,
- validate date/time/field values,
- reject conflicts with non-cancelled overlapping slots on the same field,
- persist normalized field metadata (`ParkName`, `FieldName`, `DisplayName`).
```

**Should be updated to:**
```markdown
`PATCH /slots/{division}/{slotId}` MUST:

- be admin/global only,
- reject edits to `Cancelled` slots,
- validate date/time/field values,
- reject conflicts with non-cancelled overlapping slots on the same field,
- **reject team double-booking conflicts** (checks if involved teams have other games at new time),
- persist normalized field metadata (`ParkName`, `FieldName`, `DisplayName`).

Team conflict validation:
- Checks HomeTeamId, AwayTeamId, and ConfirmedTeamId for conflicts
- Queries both Confirmed and Open slots across all divisions
- Returns DOUBLE_BOOKING error if team conflict detected
```

**Location:** Lines 118-127

---

## 2. docs/PRACTICE_REQUESTS_AND_CLAIMS_BEHAVIORAL_CONTRACT.md

### ⚠️ NEEDS UPDATE: Section 8.3 (Move Request)

**What changed:**
- Lead time changed from 48 hours to 72 hours
- Error code changed from PRACTICE_MOVE_NOT_ALLOWED to LEAD_TIME_VIOLATION

**Current text (line 115-122):**
```markdown
### 8.3 Move request

`PATCH /api/field-inventory/practice/requests/{requestId}/move` must:

- require an active source request
- create a replacement request against another normalized or ready block
- preserve the source request until the replacement request is approved or auto-approved
- reject moves into the same slot or an unavailable slot
```

**Should be updated to:**
```markdown
### 8.3 Move request

`PATCH /api/field-inventory/practice/requests/{requestId}/move` must:

- require an active source request
- create a replacement request against another normalized or ready block
- preserve the source request until the replacement request is approved or auto-approved
- reject moves into the same slot or an unavailable slot
- **enforce 72-hour lead time** (cannot move within 72 hours of scheduled practice)
- **return LEAD_TIME_VIOLATION error code** if lead time constraint violated
```

**Location:** Lines 115-122

---

## 3. docs/contract.md

### ⚠️ NEEDS NEW SECTION: Lead Time Policies

**What to add:**

**Location:** After "Time conventions" section (after line 71)

**New section:**
```markdown
### Lead time policies (locked)

All game reschedule and practice move operations enforce a minimum **72-hour lead time**:

- Game reschedule requests: 72 hours minimum before original game time
- Practice move requests: 72 hours minimum before original practice time
- Error code returned: `LEAD_TIME_VIOLATION`
- Rationale: Provides adequate coordination time for both teams and officials

This policy is consistently enforced across all reschedule/move operations.
```

---

### ⚠️ NEEDS UPDATE: Error codes section (line 58-64)

**What changed:**
- Added new error codes: FIELD_INACTIVE, LEAD_TIME_VIOLATION
- Deprecated UNAUTHORIZED in favor of FORBIDDEN

**Current text (line 58-64):**
```markdown
### Error codes (recommended)
- BAD_REQUEST (400)
- UNAUTHENTICATED (401)
- FORBIDDEN (403)
- NOT_FOUND (404)
- CONFLICT (409)
- INTERNAL (500)
```

**Should be updated to:**
```markdown
### Error codes (recommended)

**Authentication & Authorization:**
- UNAUTHENTICATED (401) - not signed in
- FORBIDDEN (403) - signed in but insufficient permissions
- ~~UNAUTHORIZED~~ (deprecated - use FORBIDDEN for 403 errors)

**Resource Errors:**
- NOT_FOUND (404) - resource not found
- FIELD_NOT_FOUND (404) - specific field not found
- FIELD_INACTIVE (400) - field exists but is inactive
- SLOT_NOT_FOUND (404) - specific slot not found

**Conflict Errors:**
- CONFLICT (409) - general conflict
- SLOT_CONFLICT (409) - field/time overlap
- DOUBLE_BOOKING (409) - team has overlapping game
- LEAD_TIME_VIOLATION (409) - operation too close to game/practice time

**Validation Errors:**
- BAD_REQUEST (400) - invalid request
- MISSING_REQUIRED_FIELD (400) - required field missing
- INVALID_DATE (400) - invalid date format
- INVALID_TIME_RANGE (400) - invalid time range

**Server Errors:**
- INTERNAL_ERROR (500) - internal server error

For complete list, see: `api/Storage/ErrorCodes.cs` and `src/lib/constants.js`
```

---

## 4. CLAUDE.md

### ⚠️ NEEDS UPDATE: Add New Utilities Section

**What to add:**

**Location:** After "Critical Workflows" section (after line 126)

**New section:**
```markdown
## New Utilities & Infrastructure

### Error Handling (Added 2026-04-22)
- **ErrorBoundary Component** (`src/components/ErrorBoundary.jsx`) - Global React error boundary to prevent white screen crashes
- **Error Logger** (`src/lib/errorLogger.js`) - Structured error logging utility
  - `logError(message, error, context)` - Logs to console in dev, Application Insights in all environments
  - `logWarning(message, context)` - For non-critical issues
- **trackException** (`src/lib/telemetry.js`) - Application Insights exception tracking

**Usage:**
```javascript
import { logError } from '../lib/errorLogger';

try {
  await riskyOperation();
} catch (err) {
  logError('Operation failed', err, { userId, operationName: 'riskyOp' });
}
```

### Conflict Detection Enhancements (Added 2026-04-22)
- **Slot creation** includes post-create conflict verification (prevents race conditions)
- **Double-booking prevention** checks both Confirmed AND Open slots
- **Team conflict validation** checks all team fields (Home, Away, Offering, Confirmed)
- **UpdateSlot** now validates team availability when admin moves games

### Lead Time Policy (Updated 2026-04-22)
- **Standard lead time:** 72 hours for all reschedule/move operations
- **Error code:** LEAD_TIME_VIOLATION
- Applies to both game reschedules and practice moves (was 48h for practice, now 72h)
```

---

## 5. README.md

### ⚠️ NEEDS UPDATE: Development section (if exists)

**Check if README mentions:**
- Error logging approach
- Testing critical paths
- Lead time policies

**If not present, no update needed** (CLAUDE.md is the developer guide).

---

## 6. docs/SCHEDULING_ENGINE_BEHAVIORAL_CONTRACT.md

### ✅ NO UPDATE NEEDED

**Reason:** Our changes didn't affect scheduling engine behavior, only slot acceptance and conflict detection.

---

## 7. docs/PRACTICE_AVAILABILITY_CONTRACT.md

### ⚠️ NEEDS CHECK

**What to verify:**
- Does it mention lead time policies?
- If yes, update to 72h

<function_calls>
<invoke name="Read">
<parameter name="file_path">C:\Users\berginjohn\App\SportSch\docs\PRACTICE_AVAILABILITY_CONTRACT.md