# CRITICAL BUGS FOUND - Spring Break & Backward Loading

Three critical bugs identified from user report.

User Report:
- Spring Break (Mar 31 - Apr 6) should have no games, but games are scheduled
- Backward loading not working - 5 games in first week (should be 0)
- Guest/offer games scheduled early (should be end-loaded)

---

## 🔴 **BUG 1: Spring Break Games Scheduled**

### **Expected:**
- User blocks Mar 31 - Apr 6 (Spring Break)
- NO games should appear in this range
- Blackout should apply to ALL game types

### **What's Happening:**
Games (possibly guest/offer games) ARE appearing in Spring Break week

### **Root Cause Investigation:**

**Blackout Logic (ScheduleWizardFunctions.cs:3004-3022):**
```csharp
private static List<SlotInfo> ApplyDateBlackouts(List<SlotInfo> slots, List<BlockedDateRange> blockedRanges)
{
    if (blockedRanges.Count == 0) return slots;
    return slots.Where(slot => !IsInBlockedRange(slot.gameDate, blockedRanges)).ToList();
}

private static bool IsInBlockedRange(string gameDate, List<BlockedDateRange> blockedRanges)
{
    if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", ..., out var date))
        return false;

    foreach (var range in blockedRanges)
    {
        if (date >= range.StartDate && date <= range.EndDate)
            return true;  // Slot IS in blocked range
    }
    return false;  // Slot is OK
}
```

**This logic is CORRECT!**

**Where It's Applied (Line 264, 499, 1384):**
```csharp
var filteredAllSlots = ApplyDateBlackouts(allSlots, blockedRanges);
```

**BUT:** This filters availability slots BEFORE guest reservation

**Check:** Are guest games bypassing blackout filter?

**Line 1393:**
```csharp
var reservedExternalSlots = SelectReservedExternalSlots(
    regularSlots,  // ← Uses regularSlots (AFTER blackout filter)
    externalOfferPerWeek,
    guestAnchors,
    seasonStart,
    bracketStart,
    bracketEnd);
```

**Wait!** `regularSlots` comes from:
```csharp
// Line 1387:
var regularSlots = FilterSlots(gameCapableSlots, seasonStart, regularRangeEnd);
```

And `gameCapableSlots` comes from:
```csharp
// Line 1386:
var gameCapableSlots = filteredAllSlots.Where(IsGameCapableSlotType).ToList();
```

And `filteredAllSlots` comes from:
```csharp
// Line 1384:
var filteredAllSlots = ApplyDateBlackouts(allSlots, blockedRanges);
```

**So guest slots SHOULD be filtered!**

### **Possible Issues:**

**Issue A: Are you seeing games or just SLOTS?**
- Slots (availability) vs Games (assignments) are different
- You might be seeing available slots (not filtered) vs assigned games (should be filtered)

**Issue B: Date format mismatch?**
- Your Spring Break: "2026-03-31" to "2026-04-06"
- Code expects: YYYY-MM-DD format
- If you entered MM-DD-YYYY or different format, filter won't work

**Issue C: Offer games created AFTER blackout filtering**
- Regular season assignments are filtered ✓
- Guest games are created from reserved slots (should be filtered) ✓
- BUT: If external offer assignment happens outside the reservation flow...

**QUESTION:** What type of game do you see in Spring Break week?
- Regular game (Team A vs Team B)?
- Guest game (Team A vs TBD external)?
- Request game (locked away game)?

---

## 🔴 **BUG 2: Backward Loading Not Working (5 Games in Week 1)**

### **Expected:**
- Backward greedy should schedule latest dates FIRST
- Week 1 (earliest) should be EMPTY or have 0-1 games
- Late weeks (May-June) should be FULL

### **What's Happening:**
- Week 1 has 5 games
- Suggests backward loading NOT working

### **Investigation:**

**Slot Ordering (ScheduleWizardFunctions.cs:3024-3044):**
```csharp
private static List<ScheduleSlot> OrderSlotsByPreference(
    List<SlotInfo> slots,
    List<DayOfWeek> preferredDays,
    bool scheduleBackward)
{
    var ordered = slots
        .OrderBy(s => SlotTypeSchedulingPriority(s))       // 1. Slot type (game first)
        .ThenBy(s => s.priorityRank.HasValue ? 0 : 1)      // 2. Has priority rank?
        .ThenBy(s => s.priorityRank ?? int.MaxValue)       // 3. Priority rank (1 = highest)
        .ThenBy(s => PreferredDayRank(s.gameDate, preferredDays));  // 4. Preferred day

    ordered = scheduleBackward
        ? ordered
            .ThenByDescending(s => WeatherReliabilityOrderWeight(s.gameDate, slotDateRange))
            .ThenByDescending(s => s.gameDate)  // ← LATEST DATE FIRST
            .ThenByDescending(s => s.startTime)
            .ThenBy(s => s.fieldKey)
        : ordered.ThenBy(s => s.gameDate)...;  // Forward: earliest first
}
```

**PROBLEM IDENTIFIED:** Priority rank comes BEFORE date ordering!

**Scenario:**
- Early week slot has priority rank 1 (highest priority)
- Late week slot has priority rank 3 (lower priority)
- Early slot processed FIRST (because lower rank number = higher priority)
- Result: Early week fills before late week!

**This violates the contract:**
> "Regular Season assignment MUST schedule from back to front... while respecting slot priority."

The code respects priority MORE than backward ordering. Should be:
- Sort by date (descending for backward)
- THEN by priority within each date/week

**Current:** Priority → Date
**Should Be:** Date → Priority (or weight both somehow)

---

## 🔴 **BUG 3: Guest Games Not End-Loaded**

### **Expected:**
- Guest games should also be backward-loaded
- Latest weeks get guest games
- Early weeks (especially week 1) should have NO guest games

### **What's Happening:**
User sees guest games in early weeks

### **Investigation:**

**Guest Slot Selection (ScheduleWizardFunctions.cs:1954-1989):**
```csharp
var picked = new List<SlotInfo>();
foreach (var weekGroup in validSlots
    .GroupBy(s => WeekKey(s.gameDate))
    .OrderBy(g => g.Key))  // ← ASCENDING ORDER (earliest weeks first!)
{
    var orderedWeekSlots = weekGroup
        .OrderBy(s => SlotTypeSchedulingPriority(s))
        .ThenBy(s => s.priorityRank.HasValue ? 0 : 1)
        .ThenBy(s => s.priorityRank ?? int.MaxValue)
        .ThenBy(s => s.gameDate)  // ← ASCENDING (earliest in week first!)
        .ThenBy(s => s.startTime)
        .ThenBy(s => s.fieldKey)
        .ToList();

    // Pick slots from this week for guests
}
```

**PROBLEM IDENTIFIED:** Line 1959 uses `OrderBy(g => g.Key)` which is **ASCENDING**!

This means:
- Week 1 processed first
- Week 2 processed second
- ...
- Week 20 processed last

Guest games fill EARLIEST weeks first! **This is WRONG for backward strategy!**

**Should Be:**
```csharp
.OrderByDescending(g => g.Key)  // ← Latest weeks first
```

**This is a CRITICAL BUG!**

---

## 🎯 **FIXES NEEDED**

### **Fix 1: Guest Games Should Use Backward Ordering**

**File:** `api/Functions/ScheduleWizardFunctions.cs:1959`

**Change:**
```csharp
// BEFORE (WRONG):
foreach (var weekGroup in validSlots
    .GroupBy(s => WeekKey(s.gameDate))
    .OrderBy(g => g.Key))  // ← ASCENDING = wrong!

// AFTER (CORRECT):
foreach (var weekGroup in validSlots
    .GroupBy(s => WeekKey(s.gameDate))
    .OrderByDescending(g => g.Key))  // ← DESCENDING = backward!
```

**Impact:** Guest games will now be placed in LATEST weeks first, matching backward strategy

---

### **Fix 2: Date Should Take Priority Over Rank**

**File:** `api/Functions/ScheduleWizardFunctions.cs:3027-3031`

**Current Problem:** Priority rank orders before date
**Impact:** Early week high-priority slots filled first

**Option A: Date First, Then Priority**
```csharp
var ordered = slots
    .OrderBy(s => SlotTypeSchedulingPriority(s));

ordered = scheduleBackward
    ? ordered
        .ThenByDescending(s => s.gameDate)          // Date FIRST
        .ThenBy(s => s.priorityRank ?? int.MaxValue) // Priority within date
        .ThenByDescending(s => s.startTime)
    : ordered
        .ThenBy(s => s.gameDate)
        .ThenBy(s => s.priorityRank ?? int.MaxValue);
```

**Option B: Combine into Weighted Score**
```csharp
// Calculate combined score: date weight + priority weight
var ordered = slots
    .OrderBy(s => SlotTypeSchedulingPriority(s))
    .ThenBy(s => {
        var dateWeight = scheduleBackward
            ? -(s.gameDate.CompareTo("2026-01-01")) // Latest = lowest value
            : s.gameDate.CompareTo("2026-01-01");   // Earliest = lowest value
        var priorityWeight = (s.priorityRank ?? 100) * 10; // Rank 1 = 10, Rank 10 = 100
        return dateWeight + priorityWeight;
    });
```

**Option C: Ignore Priority for Backward (Simplest)**
```csharp
// For backward, ignore priority entirely (date is king)
var ordered = scheduleBackward
    ? slots.OrderBy(s => SlotTypeSchedulingPriority(s))
           .ThenByDescending(s => s.gameDate)
           .ThenByDescending(s => s.startTime)
    : slots.OrderBy(s => SlotTypeSchedulingPriority(s))
           .ThenBy(s => s.priorityRank ?? int.MaxValue)
           .ThenBy(s => s.gameDate);
```

**Recommended:** Option C (simplest, most aligned with "back to front" contract)

---

### **Fix 3: Verify Spring Break Filter**

**Need from user:**
1. What exact format did you enter Spring Break dates? (YYYY-MM-DD?)
2. What TYPE of game appears in Spring Break week? (regular, guest, request?)
3. Screenshot or copy of the blocked dates configuration

**If blackout is configured correctly:**
- Games should NOT appear in that range
- If they do, it's a critical filter bypass bug

---

## 🚨 **IMMEDIATE ACTIONS**

### **For Me to Fix (1 hour):**

1. **Change guest game ordering to descending** (5 min)
   - Line 1959: OrderBy → OrderByDescending
   - Guest games will end-load properly

2. **Change slot ordering to date-first** (15 min)
   - Lines 3027-3039: Date before priority
   - Regular games will end-load properly

3. **Investigate Spring Break filter** (30 min)
   - Need user's exact configuration
   - Verify blackout logic
   - Check if specific game type bypasses filter

4. **Test all changes** (10 min)
   - Build and test
   - Verify no regressions

### **For User:**

**Share with me:**
1. How did you configure Spring Break?
   - Copy the exact dates you entered
   - Which step/field did you use?

2. What game appears in Spring Break week?
   - Regular game? (Team vs Team)
   - Guest game? (Team vs TBD external)
   - Request game? (locked away game)

3. Can you check your slot plan?
   - How many slots have priority rank 1, 2, 3?
   - This explains why early slots fill first

---

## 🎯 **ROOT CAUSES IDENTIFIED**

1. ✅ **Guest games use forward ordering** (OrderBy instead of OrderByDescending)
2. ✅ **Priority rank trumps date** (priority sorted before date)
3. ⚠️ **Spring Break bypass** (need more info to diagnose)

**These are CRITICAL contract violations!**

**Should I implement fixes 1 & 2 now (20 minutes)?**
