# Scheduling workflow (rules → slots → schedule → validation → export)

This guide documents the **rule-driven schedule seed flow** and how to produce exports for SportsEngine.

## 1) Create availability rules

Rules define repeating field availability for a division (field + date range + days + time window).

**Rule inputs** (mirrors `api/Functions/AvailabilityFunctions.cs`):

- `division` (ex: `10U`)
- `fieldKey` (`parkCode/fieldCode`)
- `startsOn`, `endsOn` (`YYYY-MM-DD`)
- `daysOfWeek` (ex: `Mon`, `Thu`)
- `startTimeLocal`, `endTimeLocal` (`HH:MM` local)
- `recurrencePattern` (currently `Weekly`)
- `timezone` (defaults to league timezone)

These rules are stored in **GameSwapFieldAvailabilityRules** (PK `AVAILRULE|{leagueId}|{fieldKey}`), with exceptions in **GameSwapFieldAvailabilityExceptions** (PK `AVAILRULEEX|{ruleId}`).

## 2) Add rule exceptions

Exceptions block availability on date ranges (league holidays, tournaments, etc.). Each exception entry defines:

- `dateFrom`, `dateTo` (`YYYY-MM-DD`)
- `startTimeLocal`, `endTimeLocal` (`HH:MM` local)
- optional `reason`

When expanded, any slot overlapping the exception window is skipped.

## 3) Generate availability slots

Use the slot-generation API to turn a rule into concrete availability slots:

```
POST /api/schedule/slots/preview
```

Body (align `dateFrom`, `dateTo`, `daysOfWeek`, `startTime`, `endTime` with your rule):

```json
{
  "division": "10U",
  "fieldKey": "gunston/turf",
  "dateFrom": "2026-03-01",
  "dateTo": "2026-05-31",
  "daysOfWeek": ["Mon", "Thu"],
  "startTime": "18:00",
  "endTime": "22:00"
}
```

Apply with:

```
POST /api/schedule/slots/apply?mode=skip|overwrite
```

## 4) Check scheduling feasibility (Season Wizard)

The Season Wizard includes an intelligent feasibility system that validates capacity and constraints **before** running the full scheduling engine.

### Feasibility Endpoint

```
POST /api/schedule/wizard/feasibility
```

Body (same as preview/apply):

```json
{
  "division": "10U",
  "seasonStart": "2026-04-01",
  "seasonEnd": "2026-06-30",
  "minGamesPerTeam": 10,
  "maxGamesPerWeek": 2,
  "noDoubleHeaders": true,
  "externalOfferPerWeek": 1,
  "slotPlan": [...]
}
```

Response:

```json
{
  "regularSeasonFeasible": true,
  "poolPlayFeasible": true,
  "bracketFeasible": true,
  "conflicts": [
    {
      "conflictId": "capacity-insufficient",
      "message": "15 games/team requires 68 slots, but only 45 available. Recommend 10 games per team.",
      "severity": "error"
    }
  ],
  "recommendations": {
    "minGamesPerTeam": 10,
    "maxGamesPerTeam": 11,
    "optimalGuestGamesPerWeek": 1,
    "message": "Recommend 10-11 games per team with 1 guest game/week for balanced schedule",
    "utilizationStatus": "Good utilization (90%)"
  },
  "capacity": {
    "availableRegularSlots": 45,
    "requiredRegularSlots": 50,
    "surplusOrShortfall": -5,
    "guestSlotsReserved": 10,
    "effectiveSlotsRemaining": 35
  }
}
```

### Feasibility Checks

The system detects these constraint conflicts:

| Conflict Type | Detection | Resolution |
|--------------|-----------|------------|
| `capacity-insufficient` | Required slots > available slots | Reduce games per team or add more slots |
| `guest-games-over-consuming` | Guest games leave insufficient capacity | Reduce guest games per week |
| `no-doubleheaders-blocking` | Can't fit games in weeks with constraints | Increase maxGamesPerWeek or allow doubleheaders |
| `max-games-per-week-insufficient` | Weekly capacity too tight | Increase maxGamesPerWeek or extend season |

### Auto-Fill Recommendations

When the Season Wizard loads Step 4 (Rules) with `minGamesPerTeam=0`:
- Automatically calculates optimal games per team based on available capacity
- Recommends guest games per week for odd team counts
- Auto-fills inputs with recommended values
- Updates in real-time as constraints change (500ms debounce)

### UI Integration

In `SeasonWizard.jsx`, Step 4 displays:
- ✅ **Success banner**: Shows recommended configuration when no conflicts
- ⚠️ **Warning banner**: Shows constraint warnings (fixable issues)
- ❌ **Error banner**: Shows blocking conflicts (impossible configurations)
- **Capacity breakdown**: Regular, pool, and bracket slot utilization
- **Inline hints**: Recommended ranges below input fields

## 5) Generate division schedule

Preview assignments:

```
POST /api/schedule/preview
```

Body:

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

Apply assignments (writes to slots):

```
POST /api/schedule/apply
```

## 6) Rerun validations

Every `/api/schedule/preview` response includes validation warnings (double headers, max games per week, missing opponents, unassigned matchups/slots).

To rerun validation after edits against scheduled games, call:

```
POST /api/schedule/validate
```

Body (same shape as preview):

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

## 7) Export CSVs (internal + SportsEngine)

In the UI, use **League Management → Scheduler → Export CSV** or **Export SportsEngine CSV**.

The SportsEngine export is generated from the same assignments using the template in `docs/sportsenginetemplate.csv`.

For scripting, use the shared helper in `api/Scheduling/ScheduleExport.cs`.
