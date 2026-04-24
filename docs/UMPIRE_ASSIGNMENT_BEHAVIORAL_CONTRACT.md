# Umpire Assignment Behavioral Contract
**Version:** 1.0
**Date:** 2026-04-24
**Status:** Current Implementation

---

## 1. PURPOSE

This contract defines the authoritative behavior for umpire (game official) assignment management within the SportSch platform. It specifies roster management, assignment lifecycle, conflict detection, and game change propagation rules.

---

## 2. SCOPE

This module extends the existing game scheduling system to manage game officials:
- Umpire roster and profile management
- Assignment of umpires to games with conflict prevention
- Umpire self-service portal (view, accept, decline assignments)
- Coach coordination (view umpire contact for games)
- Automatic propagation of game changes to umpire assignments

---

## 3. USER ROLES

### 3.1 Umpire Role

**Definition:** `ROLE.UMPIRE` - Game officials who are assigned to officiate games

**Permissions:**
- View own assignments (pending, upcoming, past)
- Accept or decline own assignments
- Set availability windows and blackout dates
- Update own profile (limited fields: phone, photo)
- Cannot view other umpires' data
- Cannot assign themselves to games
- Cannot view games they're not assigned to

**Portal:** Dedicated `#umpire` tab with dashboard and assignment management

---

## 4. UMPIRE PROFILE LIFECYCLE

### 4.1 Create Umpire Profile

`POST /api/umpires` MUST:
- Require LeagueAdmin authorization
- Validate required fields: name, email, phone
- Generate unique `umpireUserId` (GUID)
- Set `IsActive = true` by default
- Optional fields: certificationLevel, yearsExperience, notes

**Initial State:**
- No assignments
- No availability rules (Phase 2 feature)
- Profile accessible immediately for assignment

### 4.2 Update Umpire Profile

`PATCH /api/umpires/{umpireUserId}` MUST:
- Allow LeagueAdmin (all fields) OR self (limited fields)
- Umpires can update: phone, photo
- Only admins can update: certificationLevel, yearsExperience, notes
- Email is immutable after creation

### 4.3 Deactivate Umpire

`DELETE /api/umpires/{umpireUserId}` (soft delete) MUST:
- Require LeagueAdmin authorization
- Set `IsActive = false`
- If `reassignFutureGames = true`:
  - Cancel all future assignments (GameDate >= today)
  - Set assignment status to "Cancelled"
  - Set decline reason: "Umpire deactivated by admin"
  - Notify admin of games returned to unassigned queue
- Inactive umpires cannot be assigned to new games

---

## 5. ASSIGNMENT LIFECYCLE

### 5.1 Assignment States

**Assigned:**
- Admin created assignment
- Umpire notified but hasn't responded
- Game shows "Pending confirmation" to coaches

**Accepted:**
- Umpire confirmed they will officiate
- Admin notified of acceptance
- Game shows "Confirmed" to coaches
- Umpire contact visible to coaches

**Declined:**
- Umpire cannot make the game
- Admin notified with optional decline reason
- Game returns to unassigned queue
- Can be reassigned to different umpire

**Cancelled:**
- Assignment removed by admin OR game cancelled
- Umpire notified of removal
- No further action required

### 5.2 Assign Umpire to Game

`POST /api/games/{division}/{slotId}/umpire-assignments` MUST:

**Pre-conditions:**
- Umpire exists and `IsActive = true`
- Game exists
- User is LeagueAdmin

**Validation:**
1. Check if umpire already assigned to this game (prevent duplicates)
2. **CRITICAL: Check for time conflicts** (double-booking prevention)
   - Query umpire's assignments for same date
   - Skip Declined and Cancelled assignments
   - Check time overlap using `TimeUtil.Overlaps()`
   - Return 409 UMPIRE_CONFLICT if overlap detected

**On Success:**
- Create GameUmpireAssignment with status "Assigned"
- Denormalize game details (date, time, field, teams) into assignment
- Send in-app notification to umpire
- Send email notification to umpire (if sendNotification = true)
- Return created assignment

**Conflict Detection Logic:**
```
For each assignment in umpire's same-day assignments:
    Skip if assignment.status == "Declined" or "Cancelled"
    Skip if assignment.slotId == currentSlotId (for reassignment)
    If TimeUtil.Overlaps(newStart, newEnd, assignment.Start, assignment.End):
        CONFLICT - throw 409 error
```

### 5.3 Umpire Accept Assignment

`PATCH /api/umpire-assignments/{assignmentId}/status` with `status = "Accepted"` MUST:

**Authorization:**
- Umpire (self only) - assignment.UmpireUserId == userId
- OR LeagueAdmin

**State Transition:**
- Assigned → Accepted ✅
- Accepted → Accepted ✅ (idempotent)
- Declined → Accepted ❌ (invalid, use reassignment)
- Cancelled → Accepted ❌ (invalid)

**On Success:**
- Update status to "Accepted"
- Set `ResponseUtc = now`
- Notify all LeagueAdmins (in-app notification)
- Message: "{UmpireName} accepted {game} assignment"

### 5.4 Umpire Decline Assignment

`PATCH /api/umpire-assignments/{assignmentId}/status` with `status = "Declined"` MUST:

**Authorization:**
- Umpire (self only) OR LeagueAdmin

**State Transition:**
- Assigned → Declined ✅
- Declined → Declined ✅ (idempotent)
- Accepted → Declined ❌ (should use admin cancel instead)
- Cancelled → Declined ❌ (invalid)

**On Success:**
- Update status to "Declined"
- Set `ResponseUtc = now`
- Save `DeclineReason` (optional)
- Notify all LeagueAdmins (in-app + email)
- Message includes decline reason if provided
- Game returns to unassigned queue

**Game Returns to Unassigned:**
- Unassigned games query includes games with all assignments Declined or Cancelled
- Admin sees game in unassigned games list
- Admin can reassign to different umpire

### 5.5 Remove Assignment (Admin)

`DELETE /api/umpire-assignments/{assignmentId}` MUST:

**Authorization:**
- LeagueAdmin only

**Action:**
- Delete assignment record
- Notify umpire (in-app): "You have been unassigned from {game}"
- Game returns to unassigned queue

**Use Cases:**
- Admin made assignment error
- Need to reassign to different umpire
- Umpire no longer available (admin confirmed externally)

---

## 6. CONFLICT DETECTION

### 6.1 Double-Booking Prevention

**Rule:** An umpire MUST NOT be assigned to overlapping games on the same date.

**Implementation:**
- Query all umpire assignments for target game date
- Filter to active statuses (Assigned, Accepted)
- Check time overlap for each assignment
- Block assignment if ANY overlap detected

**Time Overlap Logic:**
```
Overlaps = (newStartMin < existingEndMin) AND (newEndMin > existingStartMin)

Example:
- Existing: 3:00pm-5:00pm (900-1020 minutes)
- New: 3:30pm-5:30pm (930-1050 minutes)
- Overlaps: (930 < 1020) AND (1050 > 900) = TRUE → CONFLICT
```

**Touching Boundaries (No Conflict):**
- Existing: 3:00pm-5:00pm (900-1020)
- New: 5:00pm-7:00pm (1020-1140)
- Overlaps: (1020 < 1140) AND (1140 > 900) = (1020 < 1140) AND (1140 > 900)
  - First check: TRUE
  - Second check: (1140 > 900) = TRUE
  - BUT: StartMin (1020) is NOT < EndMin (1140) of other... wait let me recalculate:
  - (1020 < 1140) = TRUE
  - (1140 > 1020) = TRUE
  - So this WOULD overlap with the formula...

Actually the formula is: `startA < endB AND endA > startB`
- Game A: start=900, end=1020
- Game B: start=1020, end=1140
- Check: (900 < 1140) AND (1020 > 1020)
- = TRUE AND FALSE = FALSE → No overlap ✅

Touching boundaries correctly identified as non-overlapping.

### 6.2 Conflict Scenarios

**Blocked:**
- ❌ Umpire assigned 3pm-5pm, trying to assign 4pm-6pm (overlaps)
- ❌ Umpire assigned 2pm-4pm, trying to assign 3:30pm-5pm (overlaps)
- ❌ Umpire assigned 3pm-5pm, trying to assign 3pm-5pm (exact same time)

**Allowed:**
- ✅ Umpire assigned 3pm-5pm, trying to assign 5pm-7pm (touching boundaries)
- ✅ Umpire assigned 3pm-5pm (Declined), trying to assign 4pm-6pm (declined ignored)
- ✅ Umpire assigned 3pm-5pm (Cancelled), trying to assign 4pm-6pm (cancelled ignored)
- ✅ Umpire assigned 3pm-5pm on June 15, trying to assign 3pm-5pm on June 16 (different days)

### 6.3 Reassignment Flow

**Scenario:** Admin wants to change umpire for a game

**Steps:**
1. Remove existing assignment (DELETE)
2. Create new assignment (POST)
3. Each operation independently validated

**Alternative:** Direct reassignment could be added in Phase 2

---

## 7. GAME CHANGE PROPAGATION

### 7.1 Game Cancelled

**Trigger:** Admin cancels game via `SlotService.CancelSlotAsync`

**Automatic Actions:**
1. Query all assignments for game (any status)
2. For each active assignment (not already Cancelled):
   - Set status = "Cancelled"
   - Set decline reason = "Game cancelled by league"
   - Update denormalized fields
3. Send notification to each umpire:
   - In-app: "Game cancelled: {game}. Your assignment removed."
   - Email: Professional cancellation email with game details
4. Fire-and-forget pattern (doesn't block game cancellation)

**Result:**
- Umpire sees cancelled assignment in portal
- Game removed from umpire's upcoming schedule
- Admin doesn't need to manually unassign

### 7.2 Game Rescheduled

**Trigger:** Admin updates game date/time/field via `UpdateSlot.cs`

**Automatic Actions:**
1. Detect what changed (date, time, field)
2. Query all assignments for game
3. For each active assignment:
   a. **If date or time changed:**
      - Check for conflicts at new time via `CheckUmpireConflictsAsync`
      - **If conflict detected:**
        - Set status = "Cancelled"
        - Set decline reason = "Game rescheduled to {newDate} at {newTime} when umpire has conflicting assignment"
        - Notify umpire of unassignment due to conflict
        - Game returns to unassigned queue
      - **If no conflict:**
        - Update denormalized fields (GameDate, StartTime, etc.)
        - Send "Game Changed" email to umpire with old vs new comparison
        - Assignment status remains (Assigned or Accepted)
   b. **If only field changed:**
      - Update FieldKey and FieldDisplayName
      - Send notification (no conflict check needed)
4. Fire-and-forget pattern

**Result:**
- Umpire assignments automatically updated
- Umpire notified of changes
- Conflicts prevent umpire from being double-booked
- Game may need reassignment if umpire conflict

**Design Decision:** Game reschedule takes priority over preserving umpire assignment. If conflict, unassign and let admin reassign.

---

## 8. DENORMALIZATION STRATEGY

### 8.1 Why Denormalize

**GameUmpireAssignment stores game details:**
- GameDate, StartTime, EndTime, StartMin, EndMin
- FieldKey, FieldDisplayName
- HomeTeamId, AwayTeamId
- Division

**Rationale:**
- Enables fast umpire-scoped queries (umpire's assignments without joining to Slots table)
- Umpire portal can load assignments without N+1 queries
- Trade-off: Slight duplication vs query performance (worth it for umpire UX)

### 8.2 Update Propagation

**When game changes, denormalized fields MUST be updated:**
- Game reschedule → Update assignment GameDate, StartTime, FieldKey
- Game cancelled → Set assignment status to Cancelled (details preserved for history)

**Consistency Guarantee:**
- Game slot is source of truth
- Assignment updates happen in fire-and-forget background task
- If update fails, umpire may see stale data temporarily
- Acceptable trade-off: Doesn't block game operations

---

## 9. NOTIFICATION MATRIX

| Trigger | Recipient | Channel | Timing | Message |
|---------|-----------|---------|--------|---------|
| Umpire assigned | Umpire | Email + In-app | Immediate | "You've been assigned to {game}. Please respond." |
| Umpire accepts | Admin | In-app | Immediate | "{Umpire} accepted {game} assignment" |
| Umpire declines | Admin | In-app | Immediate | "{Umpire} declined {game}. Reason: {reason}" |
| Game rescheduled (no conflict) | Umpire | Email + In-app | Immediate | "Game rescheduled. OLD: {old}. NEW: {new}" |
| Game rescheduled (conflict) | Umpire | In-app | Immediate | "Unassigned due to reschedule conflict" |
| Game cancelled | Umpire | Email + In-app | Immediate | "Game cancelled. Assignment removed." |
| Assignment removed | Umpire | In-app | Immediate | "You have been unassigned from {game}" |

**MVP Scope:**
- Email via SendGrid (existing integration)
- In-app via existing notification system
- SMS deferred to Phase 2

---

## 10. MVP vs PHASE 2

### 10.1 MVP Scope (Current Implementation)

**Included:**
- ✅ Single umpire per game
- ✅ Manual assignment by admin
- ✅ Conflict detection (prevents double-booking)
- ✅ Email + in-app notifications
- ✅ Accept/decline workflow
- ✅ Coach sees umpire contact
- ✅ Game change propagation
- ✅ Unassigned games tracking

**Excluded from MVP:**
- ❌ Multiple umpires per game (Phase 2)
- ❌ Position-specific assignment (Home Plate, Field, Base)
- ❌ Time-specific availability (Phase 2)
- ❌ Auto-suggest umpires (Phase 2)
- ❌ SMS notifications (Phase 2)
- ❌ Bulk assignment (Phase 2)
- ❌ Travel distance calculations (Phase 2)

### 10.2 Phase 2 Features

**When to build:** After 1 season of MVP usage, if:
- >80% umpire portal adoption
- >90% games have umpire assigned
- User requests for advanced features

---

## 11. DATA INTEGRITY INVARIANTS

### 11.1 Active Umpires Only

**Rule:** Only umpires with `IsActive = true` can be assigned to games.

**Enforcement:**
- AssignUmpireToGameAsync checks IsActive, throws 400 UMPIRE_INACTIVE if false
- Deactivation can auto-cancel future assignments

### 11.2 No Double-Booking

**Rule:** An umpire MUST NOT have overlapping assignments on the same date.

**Enforcement:**
- CheckUmpireConflictsAsync called before every assignment
- Returns 409 UMPIRE_CONFLICT if overlap detected
- Assignment blocked if conflict exists

### 11.3 Assignment References Valid Game

**Rule:** GameUmpireAssignment MUST reference an existing game slot.

**Enforcement:**
- AssignUmpireToGameAsync validates game exists (GetSlotAsync)
- Returns 404 SLOT_NOT_FOUND if game doesn't exist

### 11.4 Denormalized Data Consistency

**Best Effort:** Denormalized game details in assignment should match source game.

**Reality:**
- Updates happen in fire-and-forget background task
- May have brief inconsistency if update fails
- Game slot is source of truth
- Acceptable trade-off for performance

---

## 12. AUTHORIZATION MATRIX

| Operation | GlobalAdmin | LeagueAdmin | Coach | Umpire | Viewer |
|-----------|-------------|-------------|-------|--------|--------|
| Create umpire | ✅ | ✅ | ❌ | ❌ | ❌ |
| Edit umpire (all fields) | ✅ | ✅ | ❌ | ❌ | ❌ |
| Edit umpire (own profile, limited) | ❌ | ❌ | ❌ | ✅ Self | ❌ |
| Deactivate umpire | ✅ | ✅ | ❌ | ❌ | ❌ |
| Assign umpire to game | ✅ | ✅ | ❌ | ❌ | ❌ |
| Remove assignment | ✅ | ✅ | ❌ | ❌ | ❌ |
| Accept assignment | ✅ | ✅ | ❌ | ✅ Self | ❌ |
| Decline assignment | ✅ | ✅ | ❌ | ✅ Self | ❌ |
| View own assignments | ✅ | ✅ | ❌ | ✅ Self | ❌ |
| View umpire for own game | ✅ | ✅ | ✅ Own team | ❌ | ✅ Own team |
| View umpire contact | ✅ | ✅ | ✅ If Accepted | ❌ | ❌ |
| View all umpires | ✅ | ✅ | ❌ | ❌ | ❌ |

**Key Rules:**
- Umpires are self-scoped (can only see/update own data)
- Coaches see umpires only for their own team's games
- Contact info only visible for Accepted assignments (privacy)
- Admins have full visibility and control

---

## 13. EDGE CASES

### 13.1 Umpire Deactivated Mid-Season

**Scenario:** Admin deactivates umpire who has future assignments

**Behavior:**
- If reassignFutureGames = true: All future assignments cancelled, games to unassigned
- If reassignFutureGames = false: Assignments remain, umpire can still view/respond
- Admin notified of games needing reassignment

### 13.2 Game Rescheduled Creates Umpire Conflict

**Scenario:** Game moved to time when assigned umpire has another game

**Behavior:**
- Auto-unassign umpire (status = Cancelled)
- Notify umpire: "Unassigned due to reschedule conflict"
- Game returns to unassigned queue
- Admin must assign different umpire

**Design Rationale:** Game reschedule is more critical than preserving umpire assignment.

### 13.3 Multiple Admins Assign Same Umpire Simultaneously

**Scenario:** Two admins try to assign same umpire to overlapping games at same time

**Behavior:**
- First assignment succeeds
- Second assignment's conflict check detects the first assignment
- Returns 409 UMPIRE_CONFLICT
- No double-booking occurs

**Pattern:** Same optimistic concurrency as team/field conflicts (proven reliable)

### 13.4 Umpire Accepts Then Game Cancelled

**Scenario:** Umpire accepts assignment, then admin cancels game

**Behavior:**
- Game cancellation propagates to assignment
- Assignment status set to Cancelled
- Umpire notified via email
- Assignment shows as Cancelled in umpire portal

### 13.5 Last-Minute Cancellation

**Scenario:** Game cancelled 1 hour before start time

**Behavior:**
- Same as any cancellation (no special handling in MVP)
- Umpire receives immediate email notification
- Phase 2 could add push notifications for urgency

---

## 14. SUCCESS CRITERIA

**MVP is successful if (after 1 season):**
- ✅ 80%+ of games have umpire assigned
- ✅ <5% double-booking incidents (target: 0%)
- ✅ 60%+ umpire portal adoption
- ✅ 70%+ umpires respond to assignments
- ✅ Zero critical bugs (data corruption, notification failures)

---

## 15. FUTURE ENHANCEMENTS (Phase 2)

**Multi-Umpire Games:**
- Assign 2-3 umpires per game
- Position-specific (Home Plate, Field 1, Field 2)
- Crew scheduling (same crew across multiple games)

**Smart Assignment:**
- Auto-suggest based on availability + proximity + certification
- Load balancing (distribute games evenly)
- Certification matching (Level 2 games → Level 2+ umpires)

**Advanced Availability:**
- Time-specific windows (Mon/Wed/Fri 6pm-9pm)
- Recurring rules (every Monday)
- Blackout dates (vacation periods)
- Preferred fields/locations

**Enhanced Communication:**
- SMS via Twilio
- 48hr and 2hr game reminders
- Push notifications (mobile app)

---

This contract defines the authoritative behavior for umpire assignment management as of 2026-04-24. All implementation must conform to these specifications.
