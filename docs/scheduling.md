# Scheduling workflow (rules → slots → schedule → validation → export)

This guide documents the **rule-driven schedule seed flow** and how to produce exports for SportsEngine.

## 1) Create availability rules

Rules define repeating field availability for a division.

**Rule inputs** (mirrors `api/Scheduling/AvailabilityRuleEngine.cs`):

- `division` (ex: `10U`)
- `fieldKey` (`parkCode/fieldCode`)
- `startsOn`, `endsOn` (`YYYY-MM-DD`)
- `daysOfWeek` (ex: `Mon`, `Thu`)
- `startTime`, `endTime` (`HH:MM` local)
- `recurrencePattern` (currently `Weekly`)

These rules are stored in **GameSwapFieldAvailabilityRules** (PK `AVAILRULE|{leagueId}|{fieldKey}`), with exceptions in **GameSwapFieldAvailabilityExceptions** (PK `AVAILRULEEX|{ruleId}`).

## 2) Add rule exceptions

Exceptions block availability on date ranges (league holidays, tournaments, etc.). Each exception entry defines:

- `startDate`, `endDate` (`YYYY-MM-DD`)
- optional `label`

When expanded, any date within an exception window is skipped.

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

## 4) Generate division schedule

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

## 5) Rerun validations

Every `/api/schedule/preview` and `/api/schedule/apply` response includes a `validation` block that summarizes rule warnings (double headers, max games per week, missing opponents, unassigned matchups/slots).

To rerun validation after edits, call `/api/schedule/preview` again with the same constraints/date range.

## 6) Export CSVs (internal + SportsEngine)

In the UI, use **League Management → Scheduler → Export CSV** or **Export SportsEngine CSV**.

The SportsEngine export is generated from the same assignments using the template in `docs/sportsenginetemplate.csv`.

For scripting, use the shared helper in `api/Scheduling/ScheduleExport.cs`.
