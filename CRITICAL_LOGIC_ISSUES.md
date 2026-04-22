# Critical Logic Issues - Quick Reference
**Date:** 2026-04-22
**Source:** SCHEDULING_LOGIC_REVIEW.md
**Priority:** Issues requiring immediate attention

---

## 🔴 CRITICAL (Fix Immediately)

### Issue #1: Request/Slot Confirmation Not Atomic

**File:** `api/Services/RequestService.cs:188-211`

**Problem:**
```csharp
await _requestRepo.CreateRequestAsync(reqEntity);  // Step 1: Create approved request

try {
    await _slotRepo.UpdateSlotAsync(slot, slot.ETag);  // Step 2: Confirm slot
}
catch {
    // Best-effort rollback - MAY FAIL
    try { await _requestRepo.UpdateRequestAsync(reqEntity, ETag.All); } catch { }
}
```

If slot update fails (another team won race) but rollback fails, you get:
- ❌ Approved request for Team A
- ❌ Slot confirmed to Team B
- ❌ Data inconsistency

**Fix:**
```csharp
// Create request AFTER slot update succeeds
try {
    slot["Status"] = Constants.Status.SlotConfirmed;
    slot["ConfirmedTeamId"] = myTeamId;
    await _slotRepo.UpdateSlotAsync(slot, slot.ETag);
}
catch (RequestFailedException ex) when (ex.Status is 409 or 412) {
    throw new ApiGuards.HttpError(409, ErrorCodes.CONFLICT, "Slot was confirmed by another team.");
}

// Only create request if slot update succeeded
reqEntity["ApprovedUtc"] = DateTimeOffset.UtcNow;
await _requestRepo.CreateRequestAsync(reqEntity);
```

**Impact:** HIGH - Prevents data corruption during concurrent slot acceptance.

---

## 🟡 HIGH PRIORITY (Fix Soon)

### Issue #2: Incomplete Double-Booking Detection

**File:** `api/Services/RequestService.cs:366-376`

**Problem:**
Only checks **Confirmed** slots, not **Open** slots being accepted:

```csharp
var filter = new SlotQueryFilter {
    Status = Constants.Status.SlotConfirmed,  // ⚠️ Misses Open slots
    FromDate = gameDate,
    ToDate = gameDate
};
```

**Scenario:**
1. Team A has game confirmed at 3pm
2. Team A accepts Open slot at 3:30pm (overlaps)
3. Both requests succeed → Team A double-booked

**Fix:**
```csharp
var filter = new SlotQueryFilter {
    Statuses = new List<string> {
        Constants.Status.SlotConfirmed,
        Constants.Status.SlotOpen  // Include Open slots
    },
    FromDate = gameDate,
    ToDate = gameDate
};

// Also check if team is in PendingTeamId or being actively confirmed
```

**Impact:** MEDIUM - Teams can get double-booked if accepting quickly.

---

### Issue #3: UpdateSlot Missing Team Conflict Checks

**File:** `api/Functions/UpdateSlot.cs:150-163`

**Problem:**
Admin can move a confirmed game to a time when teams have other games.

```csharp
// Only checks field conflicts
var conflicts = await FindConflictsAsync(leagueId, normalizedFieldKey, ...);

// ❌ Doesn't check if HomeTeamId or AwayTeamId have conflicts at new time
```

**Scenario:**
1. Team A vs Team B at Field 1, 3pm
2. Team A has another game at 4pm
3. Admin moves game to Field 2, 4pm
4. Update succeeds → Team A double-booked

**Fix:**
```csharp
// Add after field conflict check
var homeTeamId = slot.GetString("HomeTeamId");
var awayTeamId = slot.GetString("AwayTeamId");

if (!string.IsNullOrWhiteSpace(homeTeamId)) {
    var homeConflict = await FindTeamConflictAsync(leagueId, homeTeamId, targetGameDate, startMin, endMin, slotId);
    if (homeConflict != null) {
        return ApiResponses.Error(..., "Home team has conflicting game at this time");
    }
}

if (!string.IsNullOrWhiteSpace(awayTeamId)) {
    // Same check for away team
}
```

**Impact:** MEDIUM - Admin operations can create double-bookings.

---

## 🟡 MEDIUM PRIORITY

### Issue #4 & #5: Error Message Exposure in Batch Operations

**Files:**
- `api/Functions/SeasonReset.cs:127, 133`
- `api/Functions/ImportSlots.cs:245`
- `api/Functions/ImportAvailabilitySlots.cs:238`

**Problem:**
Errors array includes `ex.Message`:

```csharp
errors.Add(new { category, error = ex.Message });  // ⚠️ Internal details
```

**Fix:**
```csharp
errors.Add(new { category, error = "Operation failed" });
```

**Impact:** LOW - Admin endpoints only, but still information disclosure.

---

### Issue #6: Missing Game Reschedule Notification

**File:** `api/Services/GameRescheduleRequestService.cs:196`

**Problem:**
```csharp
// TODO: Trigger opponent notification
```

**Impact:** UX - opponent team doesn't know about reschedule request.

**Fix:**
```csharp
await _notificationService.CreateNotificationAsync(
    opponentUserId,
    leagueId,
    "RescheduleRequested",
    $"{requestingTeamId} wants to reschedule to {proposedDate} at {proposedTime}",
    "#notifications",
    originalSlotId,
    "GameReschedule");
```

---

### Issue #8: Inconsistent Lead Time (48h vs 72h)

**Files:**
- `api/Services/PracticeRequestService.cs:121` → **48 hours**
- `api/Services/GameRescheduleRequestService.cs:19` → **72 hours**

**Problem:**
No documented reason for different lead times.

**Fix:**
Either:
1. Document business justification in contract
2. Standardize both to 72 hours

---

## 🟢 LOW PRIORITY (Polish)

### Issue #10: Missing State Transition Validation

**File:** `api/Functions/SlotStatusFunctions.cs:108-116`

**Problem:**
Any status → any status allowed. Invalid transitions possible:
- Cancelled → Confirmed
- Completed → Open

**Fix:** Add state machine validation (see SCHEDULING_LOGIC_REVIEW.md).

---

### Issue #11: Conflict Check Includes Completed Games

**File:** `api/Repositories/SlotRepository.cs:103-105`

**Problem:**
Only skips Cancelled, not Completed/Postponed. Historical games block new slots.

**Fix:**
```csharp
var skipStatuses = new[] {
    Constants.Status.SlotCancelled,
    Constants.Status.SlotCompleted,
    Constants.Status.SlotPostponed
};
if (skipStatuses.Contains(status)) continue;
```

---

## 📊 SUMMARY TABLE

| # | Issue | Severity | File | Impact |
|---|-------|----------|------|--------|
| 1 | Request/slot confirmation not atomic | 🔴 Critical | RequestService.cs:188-211 | Data corruption |
| 2 | Incomplete double-booking check | 🟡 High | RequestService.cs:366 | Teams double-booked |
| 3 | UpdateSlot missing team checks | 🟡 High | UpdateSlot.cs:150 | Admin double-bookings |
| 4 | SeasonReset error exposure | 🟡 Medium | SeasonReset.cs:127,133 | Info disclosure |
| 5 | Import error exposure | 🟡 Medium | ImportSlots.cs:245 | Info disclosure |
| 6 | Missing reschedule notification | 🟡 Medium | GameRescheduleRequestService.cs:196 | Poor UX |
| 8 | Inconsistent lead times | 🟡 Medium | PracticeRequestService:121, GameRescheduleRequestService:19 | Confusion |
| 10 | No state transition validation | 🟢 Low | SlotStatusFunctions.cs:108 | Invalid states |
| 11 | Completed games block conflicts | 🟢 Low | SlotRepository.cs:103 | False positives |

---

## 🚀 RECOMMENDED FIX ORDER

1. **Issue #1** (Critical) - Request/slot atomicity
2. **Issue #2** (High) - Double-booking detection enhancement
3. **Issue #3** (High) - UpdateSlot team conflict checks
4. **Issue #6** (Medium) - Reschedule notification
5. **Issues #4 & #5** (Medium) - Sanitize batch error messages
6. **Issue #8** (Medium) - Standardize lead times
7. **Issues #10 & #11** (Low) - Polish improvements

---

## 🧪 TESTING CHECKLIST

After fixes, test these scenarios:

### Race Conditions
- [ ] Two teams accepting same slot simultaneously → only one succeeds
- [ ] Two coaches creating overlapping slots → only one succeeds
- [ ] Request/slot creation atomic → no orphaned requests

### Double-Booking
- [ ] Team can't accept overlapping Open slots
- [ ] Team can't have overlapping Confirmed games
- [ ] Admin moving game checks team availability

### State Transitions
- [ ] Invalid transitions rejected (Cancelled → Confirmed)
- [ ] Confirmed requires ConfirmedTeamId

### Edge Cases
- [ ] Conflict detection skips historical games
- [ ] Idempotent cancellation sends notifications
- [ ] Lead time validation consistent

---

## 📖 REFERENCE

Full analysis: `SCHEDULING_LOGIC_REVIEW.md`

Behavioral contracts:
- `docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md`
- `docs/PRACTICE_REQUESTS_AND_CLAIMS_BEHAVIORAL_CONTRACT.md`
- `docs/SCHEDULING_ENGINE_BEHAVIORAL_CONTRACT.md`
