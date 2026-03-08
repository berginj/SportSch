# Scheduling workflow (rules -> slots -> schedule -> validation -> export)

This guide documents the rule-driven season scheduling flow and how to produce exports for SportsEngine.

## 1) Create availability rules

Rules define repeating field availability for a division (field + date range + days + time window).

Rule inputs (mirrors `api/Functions/AvailabilityFunctions.cs`):

- `division` (ex: `10U`)
- `fieldKey` (`parkCode/fieldCode`)
- `startsOn`, `endsOn` (`YYYY-MM-DD`)
- `daysOfWeek` (ex: `Mon`, `Thu`)
- `startTimeLocal`, `endTimeLocal` (`HH:MM` local)
- `recurrencePattern` (currently `Weekly`)
- `timezone` (defaults to league timezone)

These rules are stored in `GameSwapFieldAvailabilityRules` (PK `AVAILRULE|{leagueId}|{fieldKey}`), with exceptions in `GameSwapFieldAvailabilityExceptions` (PK `AVAILRULEEX|{ruleId}`).

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

The Season Wizard includes a feasibility check that validates capacity and constraints before running full scheduling.

Feasibility endpoint:

```
POST /api/schedule/wizard/feasibility
```

Body (same shape as preview/apply):

```json
{
  "division": "10U",
  "seasonStart": "2026-04-01",
  "seasonEnd": "2026-06-30",
  "minGamesPerTeam": 10,
  "maxGamesPerWeek": 2,
  "noDoubleHeaders": true,
  "externalOfferPerWeek": 1,
  "slotPlan": []
}
```

The system reports conflicts such as:

- `capacity-insufficient`
- `guest-games-over-consuming`
- `no-doubleheaders-blocking`
- `max-games-per-week-insufficient`

## 5) Generate division schedule (wizard engine)

Preview assignments:

```
POST /api/schedule/wizard/preview
```

Body:

```json
{
  "division": "10U",
  "seasonStart": "2026-04-01",
  "seasonEnd": "2026-06-30",
  "poolStart": "2026-06-07",
  "poolEnd": "2026-06-19",
  "bracketStart": "2026-06-20",
  "bracketEnd": "2026-06-27",
  "minGamesPerTeam": 11,
  "poolGamesPerTeam": 3,
  "maxGamesPerWeek": 2,
  "noDoubleHeaders": true,
  "balanceHomeAway": true,
  "externalOfferPerWeek": 2,
  "slotPlan": []
}
```

Apply assignments (writes to slots):

```
POST /api/schedule/wizard/apply
```

## 6) Validation

Wizard preview/apply responses include rule-health output (hard + soft rule summaries), warnings, and issue details.

## 7) Export CSVs (internal + SportsEngine)

Use League Management -> Commissioner Hub -> Season Wizard and export from the schedule output tools.

The SportsEngine export is generated from the same assignments using the template in `docs/sportsenginetemplate.csv`.

For scripting, use the shared helper in `api/Scheduling/ScheduleExport.cs`.
