# Scheduling Logic Improvements - Implementation Plan

Based on user feedback and real-world usage analysis.
Created: 2026-03-03

---

## 🎯 **CRITICAL FIXES NEEDED**

### **Fix 1: Guest Game Scheduling Priority** ⚠️⚠️⚠️

**Current Problem:**
- Guest games assigned LAST (after regular matchups)
- No week 1 exclusion
- No bracket week exclusion
- Often fail to schedule → "missed guest games" issue

**Requirements (from user):**
- Guest games must be scheduled FIRST (before regular matchups)
- Exclude first 7 days of season (week 1)
- Exclude all bracket days (bracketStart to bracketEnd)
- Flexible distribution (not strict per week)
- Evenly spread across teams
- If week can't accommodate, FLAG as issue (don't force)

**Implementation:**

**File:** `api/Functions/ScheduleWizardFunctions.cs`

**Change 1: Update SelectReservedExternalSlots signature (line 1591)**
```csharp
private static List<SlotInfo> SelectReservedExternalSlots(
    List<SlotInfo> slots,
    int externalOfferPerWeek,
    GuestAnchorSet? guestAnchors,
    DateOnly seasonStart,        // NEW
    DateOnly? bracketStart,      // NEW
    DateOnly? bracketEnd)        // NEW
{
    if (externalOfferPerWeek <= 0 || slots.Count == 0)
        return new List<SlotInfo>();

    // Calculate week 1 end (first 7 days)
    var weekOneEnd = seasonStart.AddDays(6);

    // Filter out excluded weeks
    var validSlots = slots.Where(s => {
        if (!DateOnly.TryParseExact(s.gameDate, "yyyy-MM-dd", out var date))
            return false;

        // Exclude week 1 (first 7 calendar days)
        if (date >= seasonStart && date <= weekOneEnd)
            return false;

        // Exclude bracket weeks (all days in bracket range)
        if (bracketStart.HasValue && bracketEnd.HasValue)
        {
            if (date >= bracketStart.Value && date <= bracketEnd.Value)
                return false;
        }

        return true;
    }).ToList();

    // Continue with existing anchor matching logic on validSlots
    var picked = new List<SlotInfo>();
    foreach (var weekGroup in validSlots.GroupBy(s => WeekKey(s.gameDate))...)
    {
        // ... existing logic
    }

    return picked;
}
```

**Change 2: Update callers (lines 542, 1542, etc.)**
```csharp
var reservedExternalSlots = SelectReservedExternalSlots(
    slots,
    externalOfferPerWeek,
    guestAnchors,
    seasonStart,       // Pass through
    bracketStart,      // Pass through
    bracketEnd);       // Pass through
```

**Change 3: Add validation warning**
```csharp
// After guest game assignment
if (externalOfferPerWeek > 0)
{
    var actualGuestGames = assignments.Count(a => a.IsExternalOffer);
    var targetGuestGames = regularWeeksCount * externalOfferPerWeek;

    if (actualGuestGames < targetGuestGames * 0.8)  // Less than 80% target
    {
        warnings.Add(new {
            code = "GUEST_GAMES_UNDERUTILIZED",
            message = $"Only {actualGuestGames} guest games scheduled (target: {targetGuestGames}). " +
                     $"Week 1 and bracket weeks are excluded. Check slot availability and anchors."
        });
    }
}
```

**Impact:**
- ✅ Guest games guaranteed to avoid week 1
- ✅ Guest games guaranteed to avoid bracket
- ✅ Clear warning if insufficient guest slots
- ✅ Solves "missed guest games" issue

**Effort:** 2-3 hours
**Risk:** Medium (changes core scheduling flow)
**Testing Required:** Verify guest games appear in weeks 2+ only, not in bracket

---

### **Fix 2: Default Min Games to 13**

**Current:** `minGamesPerTeam` defaults to 0 or 8
**Requirement:** 13 games minimum (includes pool play, excludes bracket)

**Implementation:**

**File:** `src/manage/SeasonWizard.jsx`

**Change:** Line ~978
```javascript
const [minGamesPerTeam, setMinGamesPerTeam] = useState(13);  // was 0
```

**Impact:**
- ✅ Matches Little League requirements
- ✅ Saves users from manual entry
- ✅ Feasibility calculations start from correct baseline

**Effort:** 1 minute
**Risk:** Zero
**Testing:** Verify wizard shows 13 by default

---

### **Fix 3: Feasibility Accounts for Blackouts** ⚠️⚠️

**Current Problem:**
- Feasibility calculates on total slots
- Doesn't subtract blackout windows
- Shows misleading "feasible" when Spring Break blocks critical capacity

**Example Bug:**
```
Season: Mar 15 - Jun 6 (12 weeks, 60 slots generated)
Spring Break: Mar 24-28 (5 days blocked)
Actual available: ~55 slots
Feasibility shows: 60 slots (WRONG)
```

**Implementation:**

**File:** `api/Functions/ScheduleWizardFunctions.cs`

**Current (line ~230):**
```csharp
var feasibilityResult = GameSwap.Scheduling.ScheduleFeasibility.Analyze(
    teamCount: teams.Count,
    availableRegularSlots: regularSlots.Count,  // WRONG - doesn't account for blackouts
    ...
```

**Should be:**
```csharp
// Apply blackouts BEFORE counting
var effectiveRegularSlots = ApplyDateBlackouts(regularSlots, blockedRanges);
var effectivePoolSlots = ApplyDateBlackouts(poolSlots, blockedRanges);
var effectiveBracketSlots = ApplyDateBlackouts(bracketSlots, blockedRanges);

var feasibilityResult = GameSwap.Scheduling.ScheduleFeasibility.Analyze(
    teamCount: teams.Count,
    availableRegularSlots: effectiveRegularSlots.Count,  // CORRECT
    availablePoolSlots: effectivePoolSlots.Count,
    availableBracketSlots: effectiveBracketSlots.Count,
    ...
```

**Impact:**
- ✅ Accurate capacity recommendations
- ✅ Prevents misleading "feasible" when Spring Break blocks capacity
- ✅ Users make informed decisions

**Effort:** 30 minutes
**Risk:** Low (pure calculation fix)
**Testing:** Verify feasibility with Spring Break shows reduced capacity

---

### **Fix 4: Season Date Defaults (Mar 15 - Jun 6)**

**Implementation:**

**File:** `src/manage/SeasonWizard.jsx`

**Change:** Lines ~970-971
```javascript
const [seasonStart, setSeasonStart] = useState("2026-03-15");  // was ""
const [seasonEnd, setSeasonEnd] = useState("2026-06-06");      // was ""
```

**Better:** Calculate for current year
```javascript
const getDefaultSeasonDates = () => {
  const year = new Date().getFullYear();
  return {
    start: `${year}-03-15`,
    end: `${year}-06-06`
  };
};

const defaults = getDefaultSeasonDates();
const [seasonStart, setSeasonStart] = useState(defaults.start);
const [seasonEnd, setSeasonEnd] = useState(defaults.end);
```

**Impact:**
- ✅ Convenience (save ~10 seconds per season setup)
- ✅ Prevents date entry errors

**Effort:** 10 minutes
**Risk:** Zero

---

## 🚀 **MAJOR FEATURE: Generate 4 Schedules Comparison**

### **Your Workflow:**
> "Run 4 iterations, compare, pick winner"

**This is BRILLIANT** - it compensates for greedy algorithm limitations!

**Implementation Plan:**

**File:** `src/manage/SeasonWizard.jsx`

**New UI Flow:**

**Step 4b: Schedule Comparison** (between Rules and Preview)
```javascript
async function generateScheduleOptions() {
  setLoading(true);
  setScheduleOptions([]);

  const basePayload = buildWizardPayload();
  const options = [];

  // Generate 4 schedules with different strategies
  for (let i = 0; i < 4; i++) {
    const payload = {
      ...basePayload,
      seed: Date.now() + (i * 1000),  // Different seed per schedule
      constructionStrategy: i % 2 === 0 ? "backward_greedy_v1" : "forward_greedy_v1"  // Alternate strategies
    };

    try {
      const result = await apiFetch("/api/schedule/wizard/preview", {
        method: "POST",
        body: JSON.stringify(payload)
      });

      options.push({
        id: i + 1,
        seed: payload.seed,
        strategy: payload.constructionStrategy,
        preview: result,
        metrics: calculateScheduleMetrics(result)
      });
    } catch (e) {
      options.push({
        id: i + 1,
        error: e.message,
        metrics: null
      });
    }
  }

  setScheduleOptions(options);
  setLoading(false);
}

function calculateScheduleMetrics(preview) {
  const summary = preview.summary?.regularSeason || {};

  return {
    // Primary metrics
    unassignedMatchups: summary.unassignedMatchups || 0,
    guestGamesScheduled: preview.assignments?.filter(a => a.isExternalOffer).length || 0,
    hardIssues: preview.ruleHealth?.hardViolationCount || 0,
    softScore: preview.ruleHealth?.softScore || 0,

    // Balance metrics
    doubleheaders: preview.issues?.filter(i => i.ruleId === "double-header").length || 0,
    teamLoadSpread: calculateTeamLoadSpread(preview.assignments),
    pairDiversity: calculatePairDiversity(preview.assignments),
    dateSpread: calculateDateSpreadScore(preview.assignments),

    // Quality score (0-100)
    overallQuality: calculateOverallQuality(preview)
  };
}
```

**Comparison UI:**
```jsx
<div className="schedule-comparison">
  <div className="comparison-header">
    <h3>Schedule Options - Pick Your Favorite</h3>
    <div className="subtle">Generated 4 different schedules. Compare and select the best one.</div>
  </div>

  <div className="comparison-grid">
    {scheduleOptions.map((option) => (
      <div key={option.id} className={`schedule-option ${selectedOption === option.id ? "selected" : ""}`}>
        <div className="option-header">
          <h4>Option {option.id}</h4>
          {option.metrics && (
            <div className="quality-badge">
              Quality: {option.metrics.overallQuality}/100
              {option.metrics.overallQuality === Math.max(...scheduleOptions.map(o => o.metrics?.overallQuality || 0)) && (
                <span className="badge-recommended">⭐ Recommended</span>
              )}
            </div>
          )}
        </div>

        {option.error ? (
          <div className="callout callout--error">Failed: {option.error}</div>
        ) : (
          <>
            <div className="metrics-grid">
              <div className="metric">
                <div className="metric-label">Unscheduled</div>
                <div className={`metric-value ${option.metrics.unassignedMatchups === 0 ? "good" : "bad"}`}>
                  {option.metrics.unassignedMatchups}
                </div>
              </div>

              <div className="metric">
                <div className="metric-label">Guest Games</div>
                <div className={`metric-value ${option.metrics.guestGamesScheduled >= expectedGuestGames ? "good" : "warn"}`}>
                  {option.metrics.guestGamesScheduled}
                </div>
              </div>

              <div className="metric">
                <div className="metric-label">Hard Issues</div>
                <div className={`metric-value ${option.metrics.hardIssues === 0 ? "good" : "bad"}`}>
                  {option.metrics.hardIssues}
                </div>
              </div>

              <div className="metric">
                <div className="metric-label">Soft Score</div>
                <div className="metric-value">{option.metrics.softScore}</div>
              </div>

              <div className="metric">
                <div className="metric-label">Doubleheaders</div>
                <div className="metric-value">{option.metrics.doubleheaders}</div>
              </div>

              <div className="metric">
                <div className="metric-label">Team Balance</div>
                <div className="metric-value">{option.metrics.teamLoadSpread.toFixed(1)}</div>
              </div>

              <div className="metric">
                <div className="metric-label">Pair Diversity</div>
                <div className="metric-value">{option.metrics.pairDiversity.toFixed(1)}%</div>
              </div>

              <div className="metric">
                <div className="metric-label">Date Spread</div>
                <div className="metric-value">{option.metrics.dateSpread.toFixed(1)}</div>
              </div>
            </div>

            <div className="option-actions">
              <button
                className="btn btn--primary"
                onClick={() => selectScheduleOption(option.id)}
              >
                {selectedOption === option.id ? "✓ Selected" : "Select This Schedule"}
              </button>
              <button
                className="btn btn--ghost"
                onClick={() => previewScheduleOption(option.id)}
              >
                Preview Details
              </button>
            </div>
          </>
        )}
      </div>
    ))}
  </div>

  {selectedOption && (
    <div className="comparison-footer">
      <button className="btn btn--primary btn--large" onClick={proceedWithSelected}>
        Continue with Option {selectedOption} →
      </button>
    </div>
  )}
</div>
```

**Metrics Calculation:**

```javascript
function calculateTeamLoadSpread(assignments) {
  const gamesByTeam = new Map();

  assignments.forEach(a => {
    if (a.homeTeamId) gamesByTeam.set(a.homeTeamId, (gamesByTeam.get(a.homeTeamId) || 0) + 1);
    if (a.awayTeamId) gamesByTeam.set(a.awayTeamId, (gamesByTeam.get(a.awayTeamId) || 0) + 1);
  });

  const counts = Array.from(gamesByTeam.values());
  if (counts.length === 0) return 0;

  const max = Math.max(...counts);
  const min = Math.min(...counts);
  return max - min;  // Lower is better
}

function calculatePairDiversity(assignments) {
  const pairCounts = new Map();

  assignments.forEach(a => {
    if (!a.homeTeamId || !a.awayTeamId) return;
    const key = [a.homeTeamId, a.awayTeamId].sort().join("|");
    pairCounts.set(key, (pairCounts.get(key) || 0) + 1);
  });

  const values = Array.from(pairCounts.values());
  const repeats = values.filter(count => count > 1).length;
  const totalPairs = values.length;

  return totalPairs > 0 ? ((totalPairs - repeats) / totalPairs) * 100 : 100;  // Higher is better
}

function calculateDateSpreadScore(assignments) {
  // Calculate standard deviation of game counts per week
  const weekCounts = new Map();

  assignments.forEach(a => {
    const weekKey = getWeekKey(a.gameDate);
    weekCounts.set(weekKey, (weekCounts.get(weekKey) || 0) + 1);
  });

  const counts = Array.from(weekCounts.values());
  if (counts.length === 0) return 0;

  const avg = counts.reduce((a, b) => a + b, 0) / counts.length;
  const variance = counts.reduce((sum, count) => sum + Math.pow(count - avg, 2), 0) / counts.length;
  const stdDev = Math.sqrt(variance);

  // Lower std dev = better spread
  return Math.max(0, 10 - stdDev);  // Score out of 10
}

function calculateOverallQuality(preview) {
  const metrics = calculateScheduleMetrics(preview);

  // Weighted quality score
  let score = 100;

  // Penalties
  score -= metrics.unassignedMatchups * 10;  // -10 per unscheduled
  score -= metrics.hardIssues * 15;          // -15 per hard issue
  score -= metrics.doubleheaders * 2;        // -2 per doubleheader
  score -= metrics.teamLoadSpread * 5;       // -5 per game spread difference
  score -= (100 - metrics.pairDiversity);    // Penalty for lack of diversity

  // Bonuses
  score += metrics.softScore / 10;           // Soft score contributes
  score += metrics.dateSpread * 2;           // Bonus for even spread
  score += metrics.guestGamesScheduled * 0.5; // Bonus for guest games

  return Math.max(0, Math.min(100, Math.round(score)));
}
```

**User Feedback Capture:**
```javascript
async function submitScheduleFeedback(scheduleId, selectedOptionId, feedback) {
  await apiFetch("/api/schedule/feedback", {
    method: "POST",
    body: JSON.stringify({
      scheduleId,
      selectedOption: selectedOptionId,
      allOptions: scheduleOptions.map(o => ({
        id: o.id,
        metrics: o.metrics,
        wasSelected: o.id === selectedOptionId
      })),
      userFeedback: feedback,  // "Great balance!" or "Too many doubleheaders"
      leagueId,
      division,
      timestamp: new Date().toISOString()
    })
  });
}
```

**Backend Endpoint (NEW):**
```csharp
[Function("ScheduleFeedback")]
public async Task<HttpResponseData> SubmitScheduleFeedback(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "schedule/feedback")] HttpRequestData req)
{
    // Store feedback for ML training
    var feedback = await HttpUtil.ReadJsonAsync<ScheduleFeedbackRecord>(req);

    // Save to Table Storage: Partition = LeagueId, Row = Timestamp
    await _feedbackRepo.SaveAsync(feedback);

    return ApiResponses.Ok(req, new { recorded = true });
}

record ScheduleFeedbackRecord(
    string LeagueId,
    string Division,
    int SelectedOption,
    List<ScheduleOptionMetrics> AllOptions,
    string? UserFeedback,
    DateTime Timestamp);
```

**Benefits:**
- ✅ Matches your actual workflow (4 iterations → pick best)
- ✅ Automatic comparison (saves manual checking)
- ✅ Recommendation highlights best option
- ✅ Feedback captured for future ML improvements
- ✅ Side-by-side metrics make decision easy

**Effort:** 1 day
**Risk:** Medium (new UI step)
**Testing:** Generate 4, verify differences, test selection

---

## 📐 **DESIGN CLARIFICATIONS**

Based on your answers:

### **Game Counting Formula:**
```
Total Games = Regular Season + Pool Play
Championship Games = Bracket (separate, don't count toward minimum)

Example:
- 10 regular season games
- 3 pool play games
- Total: 13 games ✓ (meets minimum)
- Bracket: 2 semifinal games + 1 finals (NOT counted, bonus)
```

### **Guest Game Rules:**
```
1. Priority: Schedule FIRST (before regular matchups)
2. Exclude: First 7 calendar days from seasonStart
3. Exclude: All days between bracketStart and bracketEnd
4. Distribution: Flexible per week, STRICT across teams (even spread)
5. Failure: If week can't fit guest games, FLAG but don't force
```

### **Week 1 Calculation:**
```csharp
var weekOneEnd = seasonStart.AddDays(6);
bool isWeekOne = date >= seasonStart && date <= weekOneEnd;
```

---

## 🎯 **IMPLEMENTATION ORDER**

### **Phase 1: Critical Fixes (This Week)**

**Day 1:**
1. ✅ Fix guest game priority scheduling (2-3 hours)
   - Add week 1 exclusion
   - Add bracket exclusion
   - Move to FIRST in assignment order
   - Add even-spread validation

2. ✅ Fix min games default to 13 (1 minute)

3. ✅ Fix feasibility blackout awareness (30 minutes)

4. ✅ Add season date defaults Mar 15 - Jun 6 (10 minutes)

**Build, test locally, push if all pass**

### **Phase 2: Major Feature (Next Week)**

**Day 2-3:**
5. ✅ Implement "Generate 4 Schedules" comparison (1 day)
   - Backend: Support multiple previews
   - Frontend: Comparison UI
   - Metrics calculation
   - Recommendation algorithm
   - Feedback capture

**Day 4:**
6. ✅ Add feedback storage endpoint (2 hours)
7. ✅ Test feedback collection (1 hour)

---

## 📊 **METRICS FOR "BETTER" SCHEDULE**

Based on your criteria, here's the ranking formula:

```javascript
function rankSchedules(options) {
  return options
    .map(option => ({
      ...option,
      score: calculateRankingScore(option.metrics)
    }))
    .sort((a, b) => b.score - a.score);
}

function calculateRankingScore(metrics) {
  let score = 100;

  // Critical: All matchups scheduled (highest priority)
  score -= metrics.unassignedMatchups * 20;        // -20 per unscheduled

  // Balance across teams (your top requirement)
  score -= metrics.teamLoadSpread * 8;             // -8 per game difference

  // Diversity: Don't repeat same matchup consecutively
  score += metrics.pairDiversity;                  // 0-100, higher better

  // Date spread: Games evenly distributed
  score += metrics.dateSpread * 5;                 // 0-10 scale, ×5 weight

  // Hard issues block application
  score -= metrics.hardIssues * 50;                // -50 per hard violation

  // Guest games (critical for odd teams)
  const guestTarget = expectedGuestGames;
  const guestDiff = Math.abs(metrics.guestGamesScheduled - guestTarget);
  score -= guestDiff * 10;                         // -10 per missed guest game

  // Soft penalties
  score -= metrics.doubleheaders * 3;              // -3 per doubleheader
  score += metrics.softScore / 10;                 // Soft score 0-1000 → 0-100

  return Math.max(0, score);
}
```

**Recommendation Logic:**
```javascript
const ranked = rankSchedules(scheduleOptions);
const best = ranked[0];

// Show recommendation
if (best.score >= 85) {
  recommendation = "⭐ Excellent schedule - highly recommended";
} else if (best.score >= 70) {
  recommendation = "✓ Good schedule - recommended";
} else if (best.score >= 50) {
  recommendation = "⚠️ Acceptable schedule - has some issues";
} else {
  recommendation = "❌ Poor schedule - consider adjusting constraints";
}
```

---

## 🤔 **QUESTIONS BEFORE I START**

### **Guest Game Spread:**
**Q1:** For even spread across teams, if you have 10 teams and 12 guest games total:
- Should it be exactly 1-2 per team?
- Or approximately even (some teams 0, some teams 2, average ~1)?

**Q2:** Guest games are "home" games with external opponent, correct?
- Team hosts, opponent is external/TBD
- Not away games at external fields?

### **Pool Play:**
**Q3:** When pool play exists, does it reduce regular season games?

Example:
- Target: 13 total games
- Pool play: 3 games
- Regular season: 10 games (13 - 3)?

Or:
- Regular season: 13 games (as configured)
- Pool play: 3 games (bonus)
- Total: 16 games?

### **Schedule Comparison:**
**Q4:** Should I show **full preview** of selected option before applying?
- Or just metrics, then user commits to selection?

**Q5:** Should failed schedules (errors during generation) be shown?
- Or silently retry until 4 successful?

---

## 🚀 **READY TO IMPLEMENT**

I'm ready to start with **Phase 1** (critical fixes). Before I code:

**A)** Answer my 5 questions above
**B)** Tell me to proceed with Phase 1 as-is
**C)** Adjust priorities

What would you like?