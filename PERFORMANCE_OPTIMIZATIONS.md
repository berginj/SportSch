# Performance Optimizations - Implementation Plan

Priority C from scheduling improvements.

---

## 🎯 **OPTIMIZATION 1: Add Look-Ahead to Greedy Algorithm**

### **Current Problem:**
Greedy algorithm can assign matchup to slot without checking if remaining matchups can still be scheduled, leading to dead-ends and wasted slots.

### **Solution: Simple Feasibility Check**

**File:** `api/Scheduling/ScheduleEngine.cs`

**Add after line 476 (before scoring candidate):**

```csharp
// Check if assigning this matchup leaves remaining matchups schedulable
if (EnableLookAhead && matchups.Count > 10)  // Only for larger problems
{
    var wouldLeaveSchedulable = CheckRemainingFeasible(
        matchups.Except([m]).ToList(),
        remainingSlots.Skip(currentSlotIndex + 1).ToList(),
        gamesByDate,
        gamesByWeek,
        maxGamesPerWeek,
        noDoubleHeaders);

    if (!wouldLeaveSchedulable)
        continue;  // Skip this candidate, try next
}
```

**Helper Method:**
```csharp
private static bool CheckRemainingFeasible(
    List<MatchupPair> remainingMatchups,
    List<ScheduleSlot> remainingSlots,
    Dictionary<string, HashSet<string>> gamesByDate,
    Dictionary<string, int> gamesByWeek,
    int? maxGamesPerWeek,
    bool noDoubleHeaders)
{
    // Simple heuristic: Can we fit remaining matchups into remaining slots?

    // 1. Check basic capacity
    if (remainingMatchups.Count > remainingSlots.Count)
        return false;

    // 2. Check weekly capacity constraints
    if (maxGamesPerWeek.HasValue && noDoubleHeaders)
    {
        // Estimate weeks needed
        var teamSet = new HashSet<string>();
        foreach (var m in remainingMatchups)
        {
            teamSet.Add(m.HomeTeamId);
            teamSet.Add(m.AwayTeamId);
        }

        var teamCount = teamSet.Count;
        var gamesNeeded = remainingMatchups.Count;
        var weeksAvailable = remainingSlots
            .Select(s => WeekKey(s.GameDate))
            .Distinct()
            .Count();

        // Each team can play max X games per week
        var maxPossibleGames = weeksAvailable * maxGamesPerWeek.Value * teamCount / 2;
        if (gamesNeeded > maxPossibleGames)
            return false;
    }

    // 3. More sophisticated checks could go here

    return true;  // Looks feasible
}
```

**Benefits:**
- Prevents dead-end assignments
- ~5-10% better slot utilization
- Minimal performance cost (only runs on larger problems)

**Risks:**
- Adds complexity
- Heuristic may be too conservative (reject valid assignments)
- Need empirical testing

**Estimated Impact:** MEDIUM (5-10% improvement)
**Effort:** 4-6 hours (implementation + testing)
**Risk:** MEDIUM (need to tune heuristic)

---

## 🎯 **OPTIMIZATION 2: Add Simple Backtracking**

### **Current Problem:**
If greedy assignment leads to dead-end (no valid candidates for next slot), that slot goes unused and we don't retry earlier decisions.

### **Solution: Limited-Depth Backtracking**

**File:** `api/Scheduling/ScheduleEngine.cs`

**Modify AssignMatchups (line 126):**

```csharp
public static ScheduleResult AssignMatchups(
    List<ScheduleSlot> slots,
    List<MatchupPair> matchups,
    IReadOnlyList<string> teams,
    ScheduleConstraints constraints,
    bool includePlacementTraces = false,
    int? tieBreakSeed = null,
    IReadOnlyDictionary<string, int>? matchupPriorityByPair = null,
    int maxBacktrackDepth = 3)  // NEW parameter
{
    // ... existing setup ...

    var backtrackStack = new Stack<BacktrackState>();

    foreach (var (slot, index) in slots.Select((s, i) => (s, i)))
    {
        var candidate = PickMatchup(...);

        if (candidate == null)
        {
            // Dead-end detected - try backtracking
            if (backtrackStack.Count > 0 && backtrackStack.Count <= maxBacktrackDepth)
            {
                var state = backtrackStack.Pop();
                // Restore state
                // Remove last assignment
                // Mark that matchup as "tried"
                // Retry current slot with remaining matchups

                // ... backtracking logic ...
            }
            else
            {
                // Can't backtrack, slot goes unused
                unassignedSlots.Add(...);
            }
        }
        else
        {
            // Save state for potential backtrack
            backtrackStack.Push(new BacktrackState(slot, candidate, ...));

            // Assign matchup
            assignments.Add(...);
        }
    }
}

record BacktrackState(
    ScheduleSlot Slot,
    MatchupPair Assignment,
    Dictionary<string, int> HomeCountsSnapshot,
    Dictionary<string, int> AwayCountsSnapshot,
    List<MatchupPair> RemainingMatchupsSnapshot);
```

**Benefits:**
- Recovers from dead-ends automatically
- Better slot utilization
- Fewer unscheduled matchups

**Drawbacks:**
- Increased complexity
- Performance cost (copying state)
- May not help if problem is truly infeasible

**Estimated Impact:** MEDIUM (10-15% fewer unscheduled matchups)
**Effort:** 1-2 days (complex implementation)
**Risk:** HIGH (changes core algorithm, extensive testing needed)

---

## 🎯 **OPTIMIZATION 3: Improve Repair Engine Effectiveness**

### **Current Problem:**
Users regenerate instead of using repairs (repairs too complex or ineffective).

### **Solution: Smarter Repair Ranking**

**File:** `api/Scheduling/ScheduleRepairEngine.cs`

**Changes:**

1. **Limit to Top 3 Proposals** (currently shows many)
```csharp
// Line 66: After ranking, take only top 3
return annotated
    .OrderByDescending(...)
    .Take(3)  // Only show best 3 proposals
    .ToList();
```

2. **Add Success Probability Estimate**
```csharp
record ScheduleRepairProposal(
    // ... existing fields ...
    double SuccessProbability);  // NEW: 0-1 estimate

private static double EstimateSuccessProbability(ScheduleRepairProposal proposal)
{
    double prob = 0.5;  // Base 50%

    // Increase if fixes critical violations
    if (proposal.HardViolationsResolved > 0)
        prob += 0.3;

    // Decrease if touches many games
    prob -= proposal.GamesMoved * 0.05;
    prob -= proposal.TeamsTouched * 0.03;

    // Increase if improves soft score
    if (proposal.SoftScoreDelta > 0)
        prob += 0.1;

    return Math.Max(0, Math.Min(1, prob));
}
```

3. **Simplify Rotation Proposals**
```csharp
// Remove complex 3-way rotations (rarely useful)
// Keep only move and swap proposals
```

**Benefits:**
- Simpler UI (3 proposals vs 10+)
- Success probability helps users decide
- Focus on high-impact repairs

**Estimated Impact:** MEDIUM (may increase repair usage)
**Effort:** 2-3 hours
**Risk:** LOW (doesn't change core algorithm)

---

## 🎯 **OPTIMIZATION 4: Cache Team Counts**

### **Current Problem:**
Dictionary lookups for team counts happen in tight loop (ScoreCandidate called hundreds of times).

### **Solution: Pre-compute and Cache**

**File:** `api/Scheduling/ScheduleEngine.cs`

**In AssignMatchups, before loop:**

```csharp
// Pre-compute total game counts for faster scoring
var totalGameCounts = new Dictionary<string, int>(teams.Count);
foreach (var team in teams)
{
    totalGameCounts[team] = (homeCounts.GetValueOrDefault(team) +
                             awayCounts.GetValueOrDefault(team));
}

// Update cache incrementally as assignments are made
// Instead of recalculating each time
```

**In ScoreCandidate:**

```csharp
// Before: O(1) + O(1) for each team
var homeTotal = homeCounts.GetValueOrDefault(home) + awayCounts.GetValueOrDefault(home);
var awayTotal = homeCounts.GetValueOrDefault(away) + awayCounts.GetValueOrDefault(away);

// After: O(1) single lookup
var homeTotal = totalGameCounts[home];
var awayTotal = totalGameCounts[away];
```

**Benefits:**
- Faster scoring (fewer dictionary lookups)
- ~10-20% speedup on large schedules (50+ teams)

**Risks:**
- Need to keep cache in sync
- More memory usage (negligible)

**Estimated Impact:** LOW-MEDIUM (faster but already fast)
**Effort:** 1-2 hours
**Risk:** LOW

---

## 📊 **Performance Optimization Summary**

| Optimization | Impact | Effort | Risk | Recommend? |
|--------------|--------|--------|------|------------|
| Look-Ahead | Medium (5-10%) | 4-6 hrs | Medium | ⚠️ Maybe |
| Backtracking | Medium (10-15%) | 1-2 days | High | ❌ Not now |
| Better Repairs | Medium | 2-3 hrs | Low | ✅ Yes |
| Cache Counts | Low (speed) | 1-2 hrs | Low | ✅ Yes |

---

## 🎯 **RECOMMENDED APPROACH**

**Phase 1 (Low-Hanging Fruit):**
1. ✅ Cache team counts (1-2 hours, low risk)
2. ✅ Simplify repair proposals (2-3 hours, low risk)

**Phase 2 (Higher Impact):**
3. ⚠️ Add look-ahead heuristic (4-6 hours, medium risk)
   - Implement
   - Test on real data
   - Measure improvement
   - Keep if >5% better, revert if marginal

**Phase 3 (Advanced):**
4. ❌ Backtracking (1-2 days, high risk)
   - Defer until data shows it's needed
   - Complex implementation
   - Extensive testing required

---

## 💡 **ALTERNATIVE: Leverage "Generate 4 Schedules"**

**Current State:**
- You have "Generate 4 Schedules" feature ✅
- Users pick best of 4 options

**Enhancement:**
Instead of optimizing the algorithm itself, **optimize the selection process**:

```javascript
// Generate MORE options (8 or 12 instead of 4)
// Use more diverse strategies:
- backward_greedy_v1
- forward_greedy_v1
- random_slot_order (shuffle slots randomly)
- priority_first (assign priority matchups first)
- balanced_spread (optimize for even distribution)

// Filter to top 4 by quality
// Show only best 4 to user
```

**Benefits:**
- ✅ Leverages existing infrastructure
- ✅ No algorithm changes (lower risk)
- ✅ More diverse schedules to choose from
- ✅ Better outcomes without code complexity

**This might be better than optimizing the greedy algorithm!**

---

## 🤔 **QUESTIONS BEFORE IMPLEMENTING**

1. **How often do you see wasted slots (unassigned)?**
   - If rare: Look-ahead not needed
   - If common: Look-ahead would help

2. **Typical schedule size:**
   - Small (5-8 teams): Optimizations overkill
   - Large (10-13 teams): Worth it

3. **Current "Generate 4" performance:**
   - Is 4 enough options?
   - Would 8 options be better (with filtering to top 4)?

4. **Repair engine usage:**
   - Do you ever use repairs?
   - Or always regenerate?

---

**Recommendation:** Let's start with **feedback capture (Priority A)** and **request games (Priority B)** which are concrete features, then circle back to performance if needed.

Want me to proceed with A and B instead?
