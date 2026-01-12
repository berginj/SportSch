# Telemetry Guide

This app uses Application Insights for frontend event tracking and backend usage logging.

## Where events show up

- Frontend events (from `trackEvent`) appear in the `customEvents` table.
- Backend usage logs appear in the `traces` table with the message prefix `usage_event`.

## Quick KQL

```kusto
// Frontend feature usage
customEvents
| where name startswith "ui_"
| project timestamp, name, customDimensions
| order by timestamp desc
```

```kusto
// Backend usage logs (API confirmations)
traces
| where message startswith "usage_event"
| project timestamp, message, customDimensions
| order by timestamp desc
```

```kusto
// Sample: count UI events by name in the last 7 days
customEvents
| where name startswith "ui_"
| where timestamp > ago(7d)
| summarize count() by name
| order by count_ desc
```

```kusto
// Sample: count API usage by event name in the last 7 days
traces
| where message startswith "usage_event"
| where timestamp > ago(7d)
| extend eventName = tostring(customDimensions.EventName)
| summarize count() by eventName
| order by count_ desc
```

## Event naming convention

- UI events: `ui_<area>_<action>` (frontend only)
- API events: `api_<area>_<action>` (server-confirmed)

## Smoke test checklist

- Open the app and navigate to a league with admin access.
- Perform a UI action (example: download a CSV template).
- Perform a server action (example: import a CSV).
- In Application Insights, run the KQL queries above.
- Confirm you see the matching `ui_` event in `customEvents` and the `api_` event in `traces`.

## UI event catalog

- `ui_access_request_submit` (leagueId, requestedRole, source)
- `ui_admin_access_request_approve` (leagueId, userId, role)
- `ui_admin_access_request_deny` (leagueId, userId)
- `ui_admin_create_league` (leagueId)
- `ui_admin_delete_league` (leagueId)
- `ui_admin_import_slots_success` (leagueId, upserted, rejected, skipped)
- `ui_admin_import_teams_success` (leagueId, upserted, rejected, skipped)
- `ui_admin_membership_assign` (leagueId, userId, division, teamId)
- `ui_admin_season_save` (leagueId)
- `ui_admin_teams_template_download` (leagueId)
- `ui_admin_user_save` (userId)
- `ui_availability_allocations_import_success` (leagueId, upserted, rejected, skipped)
- `ui_availability_allocations_template_download` (leagueId)
- `ui_availability_slots_generate` (leagueId, division, mode, created)
- `ui_availability_slots_import_success` (leagueId, upserted, rejected, skipped, divisionFixes?)
- `ui_availability_slots_preview` (leagueId, division)
- `ui_availability_slots_template_download` (leagueId)
- `ui_calendar_event_create` (leagueId, division)
- `ui_calendar_event_delete` (leagueId, eventId)
- `ui_calendar_slot_accept` (leagueId, division, slotId)
- `ui_calendar_slot_cancel` (leagueId, division, slotId)
- `ui_fields_import_success` (leagueId, source, upserted, rejected, skipped)
- `ui_season_wizard_apply` (leagueId, division)
- `ui_season_wizard_preview` (leagueId, division)
- `ui_slot_create_success` (leagueId, division, entryType, occurrences)
- `ui_slot_request_success` (leagueId, division, slotId)
- `ui_teams_import_success` (leagueId, upserted, rejected, skipped)
- `ui_teams_template_download` (leagueId)

## API event catalog

- `api_import_availability_allocations` (leagueId, upserted, rejected, skipped)
- `api_import_availability_slots` (leagueId, upserted, rejected, skipped)
- `api_import_fields` (leagueId, upserted, rejected, skipped)
- `api_import_slots` (leagueId, upserted, rejected, skipped)
- `api_import_teams` (leagueId, upserted, rejected, skipped)
- `api_availability_allocations_slots_preview` (leagueId, division, slots, conflicts)
- `api_availability_allocations_slots_apply` (leagueId, division, created, conflicts)
- `api_event_create` (leagueId, eventId, type, division)
- `api_schedule_preview` (leagueId, division, slotsAssigned, issues)
- `api_schedule_apply` (leagueId, division, runId, slotsAssigned, issues)
- `api_schedule_wizard_preview` (leagueId, division, slotsTotal, assignedTotal, issues)
- `api_schedule_wizard_apply` (leagueId, division, slotsTotal, assignedTotal, issues)
- `api_slot_create` (leagueId, division, slotId, fieldKey, gameDate, startTime, endTime, gameType)
- `api_slot_request_accept` (leagueId, division, slotId, requestingTeamId)
- `api_slot_request_approve` (leagueId, division, slotId, requestId)
- `api_slot_cancel` (leagueId, division, slotId)
