# Practice Availability Contract

Canonical source:
- Canonical practice availability is the normalized slot in `GameSwapSlots`.
- Canonical practice request state is `GameSwapPracticeRequests`.
- Imported field inventory remains the ingestion source, not the runtime booking source.

## Query APIs

### `GET /api/field-inventory/practice/availability/options`
Purpose:
- Return canonical practice options for a specific day.
- Optionally narrow to an exact start/end time and/or field.

Query:
- `date` required, `YYYY-MM-DD`
- `seasonLabel` optional
- `startTime` optional, `HH:MM`
- `endTime` optional, `HH:MM`
- `fieldKey` optional
- `division` required for admins, ignored for coaches

Response:
- `{ data: PracticeAvailabilityOptionsResponse }`

### `GET /api/field-inventory/practice/availability/check`
Purpose:
- Return whether an exact day/time window is currently bookable.
- Also return matching canonical options for that window.

Query:
- `date` required, `YYYY-MM-DD`
- `startTime` required, `HH:MM`
- `endTime` required, `HH:MM`
- `seasonLabel` optional
- `fieldKey` optional
- `division` required for admins, ignored for coaches

Response:
- `{ data: PracticeAvailabilityCheckResponse }`

## Booking API

### `POST /api/field-inventory/practice/requests`
Purpose:
- Create a practice booking request against a canonical practice option.

Body:
- `seasonLabel` optional
- `practiceSlotKey` required
- `teamId` optional for admins, ignored for coaches
- `notes` optional
- `openToShareField` optional, default `false`
- `shareWithTeamId` optional, required when `openToShareField=true`

Rules:
- Coaches are scoped to their own division/team.
- Shared booking is expressed on the request itself.
- A shared booking can reserve the primary team plus one named partner team.

## Move API

### `PATCH /api/field-inventory/practice/requests/{requestId}/move`
Body:
- `seasonLabel` optional
- `practiceSlotKey` required
- `notes` optional
- `openToShareField` optional, default `false`
- `shareWithTeamId` optional, required when `openToShareField=true`

## Shared Booking Semantics

- `PracticeShareable=true`
- `PracticeMaxTeamsPerBooking=2`
- Sharing is canonical on the request and mirrored onto the confirmed slot.
- Canonical slot writers must persist `StartMin` and `EndMin` everywhere to keep overlap checks correct.
