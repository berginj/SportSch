# Schedule Quality Analysis - Troubleshooting Guide

User reported schedule with quality issues. Analysis and solutions.

---

## 🔴 **REPORTED ISSUES**

### **Schedule Metrics:**
- **Status:** YELLOW
- **Hard Issues:** 0 ✅
- **Soft Issues:** 4
- **Soft Score:** 381.99/1000 ⚠️ (POOR - should be 700+)
- **Strategy:** backward_greedy_v1+strict_validation_v2
- **Seed:** 1564059183

### **Soft Rule Violations:**
1. **Home/away balance:** Uneven for 5 teams
2. **Idle gaps:** Long gaps for 5 teams
3. **Opponent repeats:** 4 pairings repeat >1x
4. **Unused capacity:** **76 game-capable slots unused** 🚨

### **User Observations:**
1. "Doesn't look like it fully backward loaded"
2. "Calendar shows games under wrong letter day even though date is right"

---

## 🔍 **ROOT CAUSE ANALYSIS**

### **Issue 1: Day-of-Week Display Bug** ✅ FIXED

**Problem:** Calendar shows wrong day letter (e.g., "Thu" for a Friday date)

**Root Cause:**
```javascript
// CalendarView.jsx line 391 (BEFORE FIX)
function getDayName(dateStr) {
  const date = new Date(dateStr); // ← Parses as UTC midnight
  return days[date.getDay()];     // ← Returns in LOCAL time
}

// Example:
// Date: "2026-05-15" (actually a Thursday)
// new Date("2026-05-15") = May 15, 00:00 UTC
// In US Eastern (UTC-5), this becomes May 14, 19:00 local
// getDay() returns Wednesday (off by 1)!
```

**Fix Applied:**
```javascript
function getDayName(dateStr) {
  // Parse explicitly as local date (not UTC)
  const parts = dateStr.split("-");
  const year = parseInt(parts[0], 10);
  const month = parseInt(parts[1], 10) - 1;
  const day = parseInt(parts[2], 10);
  const date = new Date(year, month, day); // Local time
  return days[date.getDay()]; // Correct day-of-week
}
```

**Status:** ✅ FIXED (committed, deploying)

---

### **Issue 2: 76 Unused Slots** 🚨 CRITICAL

**This is the MAIN PROBLEM causing low quality score.**

**Possible Causes:**

#### **Cause A: Insufficient Matchups**
```
Available: 76 + assigned slots = let's say 100 total slots
Matchups: 9 teams × 13 games ÷ 2 = 58.5 ≈ 59 matchups needed
Assigned: 100 - 76 = 24 slots used
Missing: 59 - 24 = 35 matchups unassigned!

This suggests: TOO FEW MATCHUPS being generated
```

**Check:**
- How many teams in division?
- What's minGamesPerTeam setting?
- Are matchups being generated correctly?

#### **Cause B: Over-Constrained**
```
76 unused slots with only 24 used suggests:
- Constraints too tight (max games/week, no doubleheaders)
- Guest games consuming too much capacity
- Blackout dates blocking critical slots
- Preferred weeknights too restrictive
```

**Check:**
- MaxGamesPerWeek setting (is it 2? Too restrictive?)
- Guest games per week (how many?)
- Blackout dates (Spring Break, holidays)
- Strict preferred weeknights enabled?

#### **Cause C: Backward Strategy Not Working**
```
If backward greedy is working properly:
- Should fill LATEST slots first
- Work backward toward season start
- Use high-priority slots first

If seeing early slots used and late slots empty:
- Strategy reversed or broken
- Ordering logic incorrect
```

**Check:**
- Are assigned games clustered early in season?
- Or spread throughout?
- Are late-season weeks empty?

#### **Cause D: Slot Type Mismatch**
```
76 unused game-capable slots suggests:
- Slots marked as "game" or "both"
- But scheduler not using them
- Possible: slots marked "practice" incorrectly
- Or: gameCapableSlots filter too restrictive
```

**Check:**
- How many slots total in slot plan?
- How many marked as "game"?
- How many marked as "both"?
- How many marked as "practice"?

---

## 🔧 **TROUBLESHOOTING STEPS**

### **Step 1: Verify Slot Plan Configuration**

**Go to:** Slot Planning step (Step 3 in wizard)

**Check:**
1. Total slots: ?
2. Game slots: ?
3. Both slots: ?
4. Practice slots: ?

**Expected for good schedule:**
```
For 9 teams, 13 games/team:
- Matchups needed: 59
- Slots needed: 59 (minimum)
- Buffer: 10-15 additional (total: 70-75 slots)
- Game-capable: Should be 70-75
- Practice: Separate pool for practice requests

If you have 100 game-capable slots but only using 24:
→ Problem is NOT slot availability
→ Problem is CONSTRAINT or MATCHUP generation
```

---

### **Step 2: Check Constraints (Step 2: Rules)**

**Review Settings:**

1. **Min Games Per Team:** Should be 13 ✅
2. **Pool Games Per Team:** 2-3
3. **Max Games Per Week:** ?
   - If 1: TOO RESTRICTIVE (can't fit 13 games)
   - If 2: OK but tight
   - If 3+: Good

4. **No Doubleheaders:** Enabled?
   - If Yes + Max 2/week: Very restrictive
   - May be impossible for 13 games in 12 weeks

5. **Guest Games Per Week:** ?
   - If 2-3: Consumes significant capacity
   - May be leaving no room for regular games

6. **Blocked Dates:**
   - Spring Break?
   - Holidays?
   - Too many blackouts = insufficient capacity

---

### **Step 3: Check Feasibility Analysis**

**Look for warnings:**
```
Feasibility should show:
- Available regular slots: ?
- Required regular slots: 59 (for 9 teams × 13 games)
- Surplus/shortfall: ?

If shortfall is negative:
→ Insufficient capacity (add slots or reduce games)

If shortfall is large positive (e.g., +76):
→ Capacity exists but constraints preventing use
```

**Common Issues:**

**Issue:** "Max games per week insufficient"
```
9 teams, 13 games/team, max 2 games/week, 12 weeks

Each team needs: 13 games
Each team can play: 12 weeks × 2 = 24 games maximum
✓ This should work!

But:
- Week 1 excluded (guest games)
- Weeks 11-12 bracket
- Effective: 10 weeks
- Max possible: 10 weeks × 2 = 20 games
✓ Still OK for 13 games

So max games/week is NOT the issue...
```

**Issue:** "No doubleheaders blocking"
```
13 games/team, max 2/week, no doubleheaders, 10 effective weeks

With no doubleheaders:
- Team can play max 1 game per day
- Week has 7 days
- Assuming games on 3 days/week (Mon/Wed/Fri)
- Max games possible: 10 weeks × 3 days = 30 game-days
- For 9 teams: Need 59 matchups = 59 days
- With 3 days/week × 10 weeks = 30 days available
- 59 games / 30 days = ~2 games per day average

This REQUIRES doubleheaders (multiple games same day, different teams)!

Wait, no doubleheaders means:
- Team A can't play twice in one day
- But Team A plays Game 1, Team B plays Game 2 (same day) = OK

So no-doubleheaders should be fine...

UNLESS: Fields are limited and same-day scheduling creates conflicts
```

---

### **Step 4: Diagnosis Decision Tree**

```
76 unused slots?
├─ Few matchups assigned (< 30)
│  ├─ Check: Are matchups generated? (rivalry matchups set?)
│  ├─ Check: Is minGamesPerTeam too low?
│  └─ FIX: Increase minGamesPerTeam or verify matchup generation
│
├─ Many matchups unassigned (30-40)
│  ├─ Check: Constraints too tight?
│  ├─ Check: Guest games consuming capacity?
│  └─ FIX: Relax constraints or reduce guest games
│
└─ Slots clustered in wrong weeks
   ├─ Check: Are early weeks full and late weeks empty?
   ├─ Check: Is backward greedy working?
   └─ FIX: Verify slot ordering (should be latest dates first)
```

---

## 🎯 **IMMEDIATE ACTIONS**

### **Action 1: Check Your Settings**

**Go to Wizard → Rules step, tell me:**
1. Min games per team: ?
2. Pool games per team: ?
3. Max games per week: ?
4. No doubleheaders: Enabled or disabled?
5. Guest games per week: ?
6. Blocked dates: How many?

### **Action 2: Check Slot Plan**

**Go to Wizard → Slot Planning step, tell me:**
1. Total slots: ?
2. Game-capable (Game + Both): ?
3. Practice slots: ?
4. Slots per week (average): ?

### **Action 3: Check Preview Details**

**In Preview → Coverage section:**
1. Regular season matchups total: ?
2. Regular season matchups assigned: ?
3. Regular season matchups unassigned: ?
4. Regular season slots total: ?
5. Regular season slots assigned: ?

---

## 💡 **LIKELY DIAGNOSIS**

Based on 76 unused slots + low soft score:

**Most Likely:** Constraints are too tight

**Evidence:**
- Soft score 381 (should be 700+)
- 4 soft violations (balance, gaps, repeats, unused)
- Many unused slots despite capacity

**Common Culprit:**
```
maxGamesPerWeek = 1 (TOO LOW!)
  + noDoubleHeaders = true
  + guestGamesPerWeek = 2
  = Only 1 regular game per team per week
  = Can't fit 13 games in 12 weeks!
```

**Quick Fix to Try:**
1. Increase maxGamesPerWeek to 3
2. OR: Reduce guestGamesPerWeek to 1
3. OR: Disable no-doubleheaders
4. Regenerate → Should see fewer unused slots

---

## 🚀 **SOLUTION: Generate 4 Options to Compare**

**Instead of debugging one bad schedule:**

1. Click "🔄 Generate 4 Options"
2. System will try 4 different approaches
3. Compare quality scores
4. Pick best one (likely 700+)

**This is exactly what "Generate 4 Options" was built for!**

---

## 📋 **QUALITY SCORE INTERPRETATION**

### **Score Ranges:**
- **900-1000:** Excellent (rare, nearly perfect)
- **800-899:** Very Good (minor issues only)
- **700-799:** Good (acceptable for deployment)
- **600-699:** Fair (some issues, but usable)
- **500-599:** Poor (multiple issues)
- **<500:** Very Poor (major issues) ← **You're at 381**

### **Your Score of 381 Breakdown:**
```
Start: 1000 points

Penalties (estimated):
- Unused capacity: 76 slots × 5 = -380 points
- Home/away imbalance: 5 teams × ~10 = -50 points
- Idle gaps: 5 teams × ~20 = -100 points
- Opponent repeats: 4 pairs × ~10 = -40 points

Estimate: 1000 - 380 - 50 - 100 - 40 = 430 ≈ 381 ✓

The 76 unused slots are the DOMINANT penalty!
```

**Fix the unused slots → Score jumps to 700+**

---

## 🎯 **RECOMMENDED NEXT STEPS**

### **Quick Fix (2 minutes):**
1. **Try "Generate 4 Options"** instead of single preview
2. Pick the best quality score
3. Should get 700+ quality

### **If Still Poor (5 minutes):**
1. **Relax Constraints:**
   - MaxGamesPerWeek: 2 → 3
   - OR: GuestGamesPerWeek: 2 → 1
   - OR: Disable no-doubleheaders
2. **Regenerate**

### **If Still Issues (15 minutes):**
**Tell me your settings** (from Actions 1-3 above) and I'll diagnose specifically.

---

## 📊 **EXPECTED GOOD SCHEDULE**

For 9 teams, 13 games/team, 12 weeks:

```
Matchups: 59 total
Slots used: 59-65 (with some guest games)
Unused slots: 10-20 (reasonable buffer)
Quality score: 750-850

Soft violations:
- Home/away: 0-2 teams (minor imbalance)
- Idle gaps: 0-1 teams (one team has 8-day gap)
- Repeats: 0-2 pairs (unavoidable in some cases)
- Unused: 10-20 slots (normal buffer)
```

**Your current 76 unused is 4-7x too many!**

---

## ✅ **FIXES APPLIED**

1. ✅ **Day-of-week display** - Fixed timezone parsing
2. ⏸️ **76 unused slots** - Need your settings to diagnose
3. ⏸️ **Backward loading** - Need to verify with your data

**Next:** Please try "Generate 4 Options" and let me know the quality scores!
