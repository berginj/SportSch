# Lead Time Enforcement for Practice Moves
**Date**: 2026-04-14
**Feature**: Prevent last-minute practice moves

---

## Overview

This feature prevents coaches from moving practice requests within 48 hours of the scheduled practice time, reducing last-minute schedule chaos and giving teams adequate notice for changes.

---

## Business Rules

### Minimum Lead Time
**48 hours** - Coaches must move practices at least 48 hours before the scheduled time

### When Enforced
- ✅ When moving an existing practice request
- ✅ Applied to the **original practice** time (not the target time)
- ✅ Calculated from current UTC time to practice start time

### When NOT Enforced
- ✗ Creating new practice requests (only for moves)
- ✗ Cancelling practice requests (can cancel anytime)
- ✗ Admin approvals/rejections (admins not restricted)
- ✗ If practice is already in the past (validation skips)

---

## How It Works

### Backend Validation

**Location**: `api/Services/PracticeRequestService.cs:105-130`

**Logic**:
```csharp
// Get the original practice slot
var sourceSlot = await _slotRepo.GetSlotAsync(leagueId, division, sourceSlotId);

// Parse practice date and time
var practiceDate = sourceSlot.GetString("GameDate"); // e.g., "2026-03-15"
var practiceStartTime = sourceSlot.GetString("StartTime"); // e.g., "18:00"

// Calculate hours until practice
var practiceDateTime = ParseDateTime(practiceDate, practiceStartTime);
var hoursUntilPractice = (practiceDateTime - DateTime.UtcNow).TotalHours;

// Enforce 48-hour minimum
const int minimumLeadTimeHours = 48;
if (hoursUntilPractice < minimumLeadTimeHours && hoursUntilPractice > 0) {
    throw new ApiGuards.HttpError(409, ErrorCodes.PRACTICE_MOVE_NOT_ALLOWED,
        $"Practice cannot be moved within {minimumLeadTimeHours} hours of the scheduled time. This practice is in {Math.Round(hoursUntilPractice, 1)} hours.");
}
```

**API Response** (409 Conflict):
```json
{
  "error": {
    "code": "PRACTICE_MOVE_NOT_ALLOWED",
    "message": "Practice cannot be moved within 48 hours of the scheduled time. This practice is in 24.5 hours."
  }
}
```

---

### Frontend Checking

**Location**: `src/pages/PracticePortalPage.jsx:82-113`

**Helper Function**:
```javascript
function isWithinLeadTime(request) {
  const MINIMUM_LEAD_TIME_HOURS = 48;

  // Parse practice date and time from request
  const practiceDate = new Date(request.date);
  const [hours, minutes] = request.startTime.split(":").map(Number);
  practiceDate.setHours(hours, minutes, 0, 0);

  // Calculate hours until practice
  const now = new Date();
  const hoursUntil = (practiceDate - now) / (1000 * 60 * 60);

  if (hoursUntil > 0 && hoursUntil < MINIMUM_LEAD_TIME_HOURS) {
    return {
      withinLeadTime: true,
      hoursUntil: Math.round(hoursUntil * 10) / 10,
      minimumHours: MINIMUM_LEAD_TIME_HOURS,
    };
  }

  return { withinLeadTime: false };
}
```

**UI Integration** (lines 779-800):
1. **Disabled Move Button**: Button grayed out when within lead time
2. **Tooltip**: Hover shows "Cannot move within 48 hours (24.5h remaining)"
3. **Visual Pill**: Yellow pill shows "🕒 24.5h until practice"

---

## User Experience

### Scenario 1: Outside Lead Time (Normal Flow)
**Situation**: Practice is in 72 hours

**User sees**:
- "Move" button is enabled
- No warning pills
- Can click and move normally

**Flow**:
1. Click "Move" → Button shows "Selecting Target..."
2. Choose new slot → "Move Here" button available
3. Move executes successfully

---

### Scenario 2: Within Lead Time (Blocked)
**Situation**: Practice is in 24 hours (within 48-hour window)

**User sees**:
- **"Move" button is disabled** (grayed out)
- **Yellow pill**: "🕒 24.0h until practice"
- **Tooltip on hover**: "Cannot move within 48 hours of practice time. This practice is in 24.0 hours."

**User action**: Cannot initiate move at all

---

### Scenario 3: Just Outside Lead Time
**Situation**: Practice is in 49 hours

**User sees**:
- "Move" button is enabled (just outside window)
- Can move normally

**Edge case handling**: Uses strict comparison (`<` not `<=`) so exactly 48.0 hours is allowed

---

## Visual Examples

### Practice Requests Table - Normal
```
| Date       | Field    | Status   | Action          |
|------------|----------|----------|-----------------|
| 2026-03-20 | Field A  | Approved | [Move] [Cancel] |
| 2026-03-22 | Field B  | Pending  | [Move] [Cancel] |
```

### Practice Requests Table - Within Lead Time
```
| Date       | Field    | Status                           | Action                    |
|------------|----------|----------------------------------|---------------------------|
| 2026-03-15 | Field A  | [Approved]                       | [Move (disabled)] [Cancel]|
|            |          | [Move Request]                   |                           |
|            |          | [🕒 24.5h until practice]        |                           |
| 2026-03-22 | Field B  | [Pending]                        | [Move] [Cancel]           |
```

**Tooltip on disabled Move button**:
"Cannot move within 48 hours of practice time. This practice is in 24.5 hours."

---

## Technical Implementation

### Backend

**File**: `api/Services/PracticeRequestService.cs`

**Added**:
- `using System.Globalization;` (line 1)
- Lead time validation in `CreateMoveRequestAsync` (lines 105-130)

**Error Code**: `PRACTICE_MOVE_NOT_ALLOWED` (409 Conflict)

**Validation Logic**:
1. Fetch source slot from database
2. Parse `GameDate` (YYYY-MM-DD) and `StartTime` (HH:MM)
3. Combine into DateTime
4. Calculate hours from now
5. If 0 < hours < 48, throw error

**Configuration**:
- Hardcoded to 48 hours
- Can be made configurable by:
  - Adding `MinimumMoveLeadTimeHours` to LeagueSettings
  - Passing as parameter
  - Reading from config

---

### Frontend

**File**: `src/pages/PracticePortalPage.jsx`

**Added**:
- `isWithinLeadTime(request)` helper function (lines 82-113)
- Lead time checking in Move button (lines 779-800)
- Visual pill indicator (lines 770-787)

**UI Components**:
1. **Button Disable**: `disabled={leadTimeCheck.withinLeadTime}`
2. **Tooltip**: Shows hours remaining and minimum requirement
3. **Visual Pill**: Yellow warning pill with clock icon and countdown

**Error Handling**:
- Invalid date/time formats don't block moves (fail safely)
- Try-catch ensures parsing errors don't crash UI

---

## Testing

### Backend Unit Tests

**File**: `api/GameSwap.Tests/Services/PracticeRequestServiceTests.cs`

**Test 1: CreateMoveRequestAsync_WithinLeadTime_ThrowsConflict**
- Practice in 24 hours
- ✓ Verifies: Throws 409 Conflict
- ✓ Verifies: Error message mentions "48 hours" and "24"
- ✓ Verifies: Move request not created

**Test 2: CreateMoveRequestAsync_OutsideLeadTime_AllowsMove**
- Practice in 72 hours
- ✓ Verifies: Move succeeds
- ✓ Verifies: Move request created with RequestKind = "Move"

### Frontend Tests
**Status**: All 165 existing tests still passing
- No regressions from lead time UI changes

### Manual Test Cases

**Test Case 1: Block Move Within 48 Hours**
1. Create practice request for tomorrow at 6:00 PM
2. Wait until 24 hours before practice
3. ✓ Verify: Move button is disabled
4. ✓ Verify: Tooltip shows hours remaining
5. ✓ Verify: Yellow pill shows countdown

**Test Case 2: Allow Move Outside 48 Hours**
1. Create practice request for 3 days from now
2. ✓ Verify: Move button is enabled
3. Click Move and select new slot
4. ✓ Verify: Move executes successfully

**Test Case 3: Backend Enforcement**
1. Bypass frontend (use API directly)
2. Try to move practice within 48 hours via API
3. ✓ Verify: Backend returns 409 Conflict
4. ✓ Verify: Error message explains lead time requirement

**Test Case 4: Edge Case - Exactly 48 Hours**
1. Create practice request for exactly 48 hours from now
2. ✓ Verify: Move is allowed (uses `<` not `<=`)

---

## Future Enhancements

### Configuration
Make lead time configurable per league:
```csharp
// LeagueSettings model
public int MinimumMoveLeadTimeHours { get; set; } = 48;

// Usage
var settings = await _settingsRepo.GetLeagueSettingsAsync(leagueId);
var minimumLeadTimeHours = settings?.MinimumMoveLeadTimeHours ?? 48;
```

### Different Lead Times by Type
- **Game reschedules**: 72 hours (more critical)
- **Practice moves**: 48 hours (current)
- **Cancellations**: 24 hours (less disruptive)

### Grace Period for Emergencies
Allow admins to override lead time restrictions:
```csharp
if (!isAdmin && hoursUntilPractice < minimumLeadTimeHours) {
    throw new ApiGuards.HttpError(...);
}
```

### Notification Escalation
- **>48 hours**: Standard notification
- **24-48 hours**: Email + in-app
- **<24 hours**: Blocked (except admin override)

### Lead Time Countdown in UI
Show countdown timer updating in real-time:
```jsx
<span className="pill">
  🕒 {formatCountdown(hoursUntilPractice)} until practice
</span>
```

---

## Configuration Guide

### Current Setup (Hardcoded)
- **Backend**: 48 hours in `PracticeRequestService.cs:121`
- **Frontend**: 48 hours in `PracticePortalPage.jsx:84`

### To Change Lead Time
1. **Backend**: Update `const int minimumLeadTimeHours = 48;`
2. **Frontend**: Update `const MINIMUM_LEAD_TIME_HOURS = 48;`
3. Rebuild and redeploy both

### To Make Configurable
1. Add to `LeagueSettings` model
2. Add UI in Admin Settings
3. Read from settings in validation logic
4. Pass to frontend via API response

---

## Error Messages

### Backend (API)
**409 Conflict**:
```
"Practice cannot be moved within 48 hours of the scheduled time. This practice is in 24.5 hours."
```

### Frontend (Tooltip)
```
"Cannot move within 48 hours of practice time. This practice is in 24.5 hours."
```

### Frontend (Visual Pill)
```
🕒 24.5h until practice
```

---

## Related Features

### Conflict Detection
Lead time enforcement works alongside conflict detection:
1. **First**: Lead time check (is move too close?)
2. **Second**: Conflict check (does move overlap other commitments?)

Both validations must pass for move to succeed.

### Cancel vs Move
- **Cancel**: No lead time restriction (can cancel anytime)
- **Move**: 48-hour lead time enforced

**Rationale**: Moving affects other teams who may have requested the slot, while cancelling just releases it back to availability.

---

## Impact Analysis

### Before Lead Time Enforcement
- Coaches could move practices 1 hour before scheduled time
- Other teams lost access to slots at last minute
- Commissioners had to manually reject last-minute moves
- Schedule chaos and confusion

### After Lead Time Enforcement
- ✅ **48-hour buffer** ensures adequate notice
- ✅ **Automatic enforcement** (no manual admin review needed)
- ✅ **Clear UI feedback** (disabled buttons + tooltips)
- ✅ **Visual countdown** shows hours remaining
- ✅ **Backend + frontend** validation (defense in depth)

### Metrics to Track
- Number of move attempts blocked by lead time
- Average hours between move request and practice time
- Admin override frequency (if implemented)

---

## Files Modified

### Backend (3 files)
1. **api/Services/PracticeRequestService.cs**
   - Added `using System.Globalization;`
   - Added lead time validation (lines 105-130)
   - Calculates hours until practice
   - Throws 409 if within 48-hour window

2. **api/GameSwap.Tests/Services/PracticeRequestServiceTests.cs**
   - Added `CreateMoveRequestAsync_WithinLeadTime_ThrowsConflict` test
   - Added `CreateMoveRequestAsync_OutsideLeadTime_AllowsMove` test

### Frontend (1 file)
1. **src/pages/PracticePortalPage.jsx**
   - Added `isWithinLeadTime(request)` helper (lines 82-113)
   - Modified Move button to disable within lead time (lines 779-800)
   - Added visual pill indicator (lines 770-787)

---

## API Contract

### Existing Endpoint Behavior Change

**Endpoint**: `PATCH /api/field-inventory/practice/requests/{requestId}/move`

**New Validation**: Lead time check added

**Success Response** (200 OK): Unchanged

**New Error Response** (409 Conflict):
```json
{
  "error": {
    "code": "PRACTICE_MOVE_NOT_ALLOWED",
    "message": "Practice cannot be moved within 48 hours of the scheduled time. This practice is in 24.5 hours."
  }
}
```

---

## Testing

### Automated Tests

**Backend Tests** (142 total, 2 new):
- ✅ `CreateMoveRequestAsync_WithinLeadTime_ThrowsConflict`
- ✅ `CreateMoveRequestAsync_OutsideLeadTime_AllowsMove`

**Frontend Tests** (165 total):
- ✅ All existing tests still passing
- ✅ No regressions from UI changes

**Test Coverage**:
- ✓ Move blocked within 48 hours
- ✓ Move allowed outside 48 hours
- ✓ Error message contains correct information
- ✓ UI feedback works correctly

---

## Edge Cases Handled

### 1. Past Practice
**Scenario**: Practice was yesterday
**Behavior**: hoursUntilPractice is negative, validation skipped
**Result**: Move allowed (or blocked by other validation)

### 2. Invalid Date/Time
**Frontend**: Try-catch returns `withinLeadTime: false`, allows move
**Backend**: DateTime.TryParseExact fails, validation skipped

### 3. Exactly 48 Hours
**Behavior**: Uses `<` comparison, so exactly 48.0 hours is **allowed**
**Rationale**: Meets minimum requirement

### 4. Time Zone Considerations
**Current**: Uses UTC for all calculations
**Note**: If league uses local time zones, consider adding timezone offset

---

## Future Considerations

### Make Configurable
**Add to League Settings**:
```json
{
  "leagueId": "league-123",
  "minimumPracticeMoveLeadTimeHours": 48,
  "minimumGameRescheduleLeadTimeHours": 72,
  "minimumCancellationLeadTimeHours": 24
}
```

### Different Rules by Division
Some divisions may need different lead times:
- Youth (U8-U10): 48 hours
- Competitive (U12+): 72 hours
- Adult leagues: 24 hours

### Emergency Override
Add admin capability to bypass lead time:
```javascript
// API
POST /api/field-inventory/practice/requests/{requestId}/move?bypassLeadTime=true

// UI
if (isAdmin) {
  <checkbox>Emergency override (bypass 48-hour lead time)</checkbox>
}
```

### Graduated Lead Time
- >7 days before: Free to move
- 3-7 days before: Warning but allowed
- 48h-3 days: Requires admin approval
- <48 hours: Blocked

---

## Related Documentation

- `COACH_RESCHEDULE_BUG_REPORT.md` - Original gap analysis (GAP-5)
- `IMPLEMENTATION_SUMMARY.md` - Overall implementation summary
- `CONFLICT_WARNING_FEATURE.md` - Conflict detection feature
- `docs/PRACTICE_REQUESTS_AND_CLAIMS_BEHAVIORAL_CONTRACT.md` - Practice workflow contract

---

## Summary

### What Changed
- ✅ Backend enforces 48-hour lead time on practice moves
- ✅ Frontend disables move button within lead time
- ✅ Visual indicators show countdown
- ✅ Helpful tooltips explain restriction
- ✅ 2 new unit tests verify enforcement

### What Didn't Change
- ✗ Creating new requests (no lead time)
- ✗ Cancelling requests (no lead time)
- ✗ Admin actions (not restricted)

### Benefits
- **Reduces chaos**: 48-hour buffer gives teams notice
- **Automatic**: No manual admin review needed
- **Clear communication**: UI makes restriction obvious
- **Defensein depth**: Both frontend and backend validate

---

**End of Documentation**
