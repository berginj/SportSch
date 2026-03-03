# Request Games Feature - Implementation Plan

Priority B: Complete request game feature for one-time away games.

---

## 🎯 **FEATURE OVERVIEW**

**Request Games** = One-time away games at specific external fields/dates
- **Use Case:** Tournaments, cross-division play, makeup games
- **Direction:** Away games (team plays at external venue)
- **Frequency:** Non-recurring (unlike regular season matchups)
- **Assignment:** Specific team plays away on specific date/time/field

**Different from:**
- Regular matchups (recurring, home/away alternates)
- Guest games (home games with external opponent)

---

## 📋 **REQUIREMENTS**

Based on earlier discussion:

1. **Data Structure:**
   - Date, time, field, team (away), optional opponent name
   - One-time only (not pattern-based like guest anchors)

2. **Scheduling:**
   - Integrate into wizard preview
   - Count toward team's total game load
   - Respect constraints (max games/week, no doubleheaders)

3. **UI:**
   - Add/edit/delete request games
   - Table in Rules or Slot Planning step
   - Validation (date in season, team exists)

4. **Current Status:**
   - ✅ RequestGameSlot record created
   - ✅ Added to WizardRequest signature
   - ⏳ UI not yet built
   - ⏳ Scheduling integration not yet done

---

## 🏗️ **IMPLEMENTATION PLAN**

### **Phase 1: Backend Data Model** ✅ DONE

**File:** `api/Functions/ScheduleWizardFunctions.cs`

```csharp
public record RequestGameSlot(
    string? gameDate,      // YYYY-MM-DD
    string? startTime,     // HH:MM
    string? endTime,       // HH:MM
    string? fieldKey,      // External field identifier
    string? teamId,        // Team playing AWAY
    string? opponentName   // Optional external opponent
);
```

**Added to WizardRequest:**
```csharp
List<RequestGameSlot>? requestGames
```

### **Phase 2: UI - Add Request Games Table**

**File:** `src/manage/SeasonWizard.jsx`

**Location:** Rules step (after rivalry matchups)

**State:**
```javascript
const [requestGames, setRequestGames] = useState([]);
```

**Functions:**
```javascript
function addRequestGame() {
  setRequestGames(prev => [...prev, {
    gameDate: "",
    startTime: "",
    endTime: "",
    fieldKey: "",
    teamId: "",
    opponentName: ""
  }]);
}

function updateRequestGame(index, patch) {
  setRequestGames(prev => prev.map((rg, i) =>
    i === index ? { ...rg, ...patch } : rg
  ));
}

function removeRequestGame(index) {
  setRequestGames(prev => prev.filter((_, i) => i !== index));
}
```

**UI:**
```jsx
<div className="card mt-3">
  <div className="row row--between items-center mb-2">
    <div>
      <div className="font-bold">Request Games (Away)</div>
      <div className="subtle">
        One-time away games at external fields (tournaments, makeup games)
      </div>
    </div>
    <button className="btn btn--ghost" onClick={addRequestGame}>
      + Add Request Game
    </button>
  </div>

  {requestGames.length === 0 ? (
    <div className="subtle">No request games. Add for tournaments or external venues.</div>
  ) : (
    <table className="table table--compact">
      <thead>
        <tr>
          <th>Date</th>
          <th>Start</th>
          <th>End</th>
          <th>Field</th>
          <th>Team (Away)</th>
          <th>Opponent</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        {requestGames.map((rg, idx) => (
          <tr key={idx}>
            <td>
              <input
                type="date"
                value={rg.gameDate}
                onChange={(e) => updateRequestGame(idx, { gameDate: e.target.value })}
              />
            </td>
            <td>
              <input
                type="time"
                value={rg.startTime}
                onChange={(e) => updateRequestGame(idx, { startTime: e.target.value })}
              />
            </td>
            <td>
              <input
                type="time"
                value={rg.endTime}
                onChange={(e) => updateRequestGame(idx, { endTime: e.target.value })}
              />
            </td>
            <td>
              <input
                type="text"
                value={rg.fieldKey}
                onChange={(e) => updateRequestGame(idx, { fieldKey: e.target.value })}
                placeholder="external/tournament"
              />
            </td>
            <td>
              <select
                value={rg.teamId}
                onChange={(e) => updateRequestGame(idx, { teamId: e.target.value })}
              >
                <option value="">Select team</option>
                {divisionTeams.map(t => (
                  <option key={t.teamId} value={t.teamId}>
                    {t.name || t.teamId}
                  </option>
                ))}
              </select>
            </td>
            <td>
              <input
                type="text"
                value={rg.opponentName}
                onChange={(e) => updateRequestGame(idx, { opponentName: e.target.value })}
                placeholder="Opponent (optional)"
              />
            </td>
            <td>
              <button className="btn btn--ghost" onClick={() => removeRequestGame(idx)}>
                Remove
              </button>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )}
</div>
```

**Add to payload:**
```javascript
function buildWizardPayload() {
  // ... existing payload ...

  const requestGamesPayload = requestGames
    .filter(rg => rg.gameDate && rg.teamId && rg.fieldKey)
    .map(rg => ({
      gameDate: rg.gameDate,
      startTime: rg.startTime,
      endTime: rg.endTime,
      fieldKey: rg.fieldKey,
      teamId: rg.teamId,
      opponentName: rg.opponentName || undefined
    }));

  if (requestGamesPayload.length > 0) {
    payload.requestGames = requestGamesPayload;
  }

  return payload;
}
```

### **Phase 3: Backend Scheduling Integration**

**File:** `api/Functions/ScheduleWizardFunctions.cs`

**Build Request Game Slots:**
```csharp
private static List<SlotInfo> BuildRequestGameSlots(
    List<RequestGameSlot>? requestGames,
    List<string> teams)
{
    if (requestGames is null || requestGames.Count == 0)
        return new List<SlotInfo>();

    var result = new List<SlotInfo>();

    foreach (var rg in requestGames)
    {
        var gameDate = (rg.gameDate ?? "").Trim();
        var startTime = (rg.startTime ?? "").Trim();
        var endTime = (rg.endTime ?? "").Trim();
        var fieldKey = (rg.fieldKey ?? "").Trim();
        var teamId = (rg.teamId ?? "").Trim();
        var opponentName = (rg.opponentName ?? "").Trim();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(gameDate) ||
            string.IsNullOrWhiteSpace(startTime) ||
            string.IsNullOrWhiteSpace(endTime) ||
            string.IsNullOrWhiteSpace(fieldKey) ||
            string.IsNullOrWhiteSpace(teamId))
            continue;

        // Validate date format
        if (!DateOnly.TryParseExact(gameDate, "yyyy-MM-dd", out _))
            continue;

        // Validate team exists
        if (!teams.Contains(teamId, StringComparer.OrdinalIgnoreCase))
            continue;

        // Create virtual slot for request game
        result.Add(new SlotInfo(
            slotId: Guid.NewGuid().ToString(),
            gameDate: gameDate,
            startTime: startTime,
            endTime: endTime,
            fieldKey: fieldKey,
            offeringTeamId: "",
            slotType: "game",
            priorityRank: null,
            isRequestGame: true,                    // NEW flag
            requestGameTeamId: teamId,              // Team playing away
            requestGameOpponentName: opponentName   // External opponent
        ));
    }

    return result;
}
```

**Add to SlotInfo record:**
```csharp
private record SlotInfo(
    string slotId,
    string gameDate,
    string startTime,
    string endTime,
    string fieldKey,
    string offeringTeamId,
    string slotType,
    int? priorityRank,
    bool isRequestGame = false,              // NEW
    string? requestGameTeamId = null,        // NEW
    string? requestGameOpponentName = null); // NEW
```

**Integration Point (in RunWizard):**
```csharp
// After loading teams, before slot filtering
var requestGameSlots = BuildRequestGameSlots(body.requestGames, teams);

// Merge with availability slots
allSlots.AddRange(requestGameSlots);

// Request games will participate in normal assignment
// They'll be treated as away games for the specified team
```

**Scheduling Logic:**
```csharp
// In AssignPhaseSlots or similar
foreach (var slot in slots)
{
    if (slot.isRequestGame)
    {
        // Handle request game specially
        var awayTeam = slot.requestGameTeamId;
        var homeTeam = "";  // No home team (external opponent)

        assignments.Add(new ScheduleAssignment(
            SlotId: slot.slotId,
            GameDate: slot.gameDate,
            StartTime: slot.startTime,
            EndTime: slot.endTime,
            FieldKey: slot.fieldKey,
            HomeTeamId: homeTeam,
            AwayTeamId: awayTeam,
            IsExternalOffer: false,
            IsRequestGame: true,
            RequestGameOpponent: slot.requestGameOpponentName
        ));

        // Update team's game counts
        IncrementTeamCount(totalCounts, awayTeam);
        // Check constraints (max games/week, no doubleheaders)
    }
    else
    {
        // Normal slot assignment logic
    }
}
```

**Display in Preview:**
```csharp
public record WizardSlotDto(
    string phase,
    string slotId,
    string gameDate,
    string startTime,
    string endTime,
    string fieldKey,
    string homeTeamId,
    string awayTeamId,
    bool isExternalOffer,
    bool isRequestGame = false,         // NEW
    string? requestGameOpponent = null); // NEW
```

### **Phase 4: Validation**

**Add validation in frontend:**
```javascript
const requestGameIssues = useMemo(() => {
  const issues = [];

  requestGames.forEach((rg, idx) => {
    // Date in season range
    if (rg.gameDate < seasonStart || rg.gameDate > seasonEnd) {
      issues.push(`Request game ${idx+1}: Date outside season`);
    }

    // Time valid
    const start = parseMinutes(rg.startTime);
    const end = parseMinutes(rg.endTime);
    if (start >= end) {
      issues.push(`Request game ${idx+1}: Invalid time range`);
    }

    // Team exists
    const teamExists = divisionTeams.some(t => t.teamId === rg.teamId);
    if (!teamExists) {
      issues.push(`Request game ${idx+1}: Unknown team`);
    }

    // No duplicates (same team, same date)
    const dupes = requestGames.filter((other, i) =>
      i !== idx && other.teamId === rg.teamId && other.gameDate === rg.gameDate
    );
    if (dupes.length > 0) {
      issues.push(`Request game ${idx+1}: Duplicate (same team, same date)`);
    }
  });

  return issues;
}, [requestGames, seasonStart, seasonEnd, divisionTeams]);
```

---

## 📊 **IMPLEMENTATION STATUS**

| Phase | Status | Effort | Risk |
|-------|--------|--------|------|
| 1. Data Model | ✅ DONE | - | - |
| 2. UI Table | ⏳ TODO | 2-3 hours | Low |
| 3. Backend Integration | ⏳ TODO | 3-4 hours | Medium |
| 4. Validation | ⏳ TODO | 1-2 hours | Low |

**Total Effort:** 6-9 hours
**Total Risk:** MEDIUM (scheduling logic changes)

---

## 🚀 **NEXT STEPS**

**Option A:** Implement full request game feature now (6-9 hours)
**Option B:** Defer to next session (foundation is ready)
**Option C:** Implement UI only (2-3 hours, backend later)

**Recommendation:** Given session length and build error history, defer full implementation to next session. Foundation is in place.

---

## 📝 **WHAT'S READY**

✅ RequestGameSlot record created
✅ Added to WizardRequest signature
✅ Data model complete
✅ Implementation plan documented

**Can be completed incrementally:**
1. Add UI (2-3 hours)
2. Add backend integration (3-4 hours)
3. Add validation (1-2 hours)
4. Test thoroughly

---

**Status:** Foundation complete, full implementation deferred pending user approval.
