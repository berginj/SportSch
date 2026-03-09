# Practice Requests and Claims Behavioral Contract

This document is the current workflow contract for normalized practice-space behavior.

## 1. Scope

This contract applies to the field-inventory practice workflow exposed by:

- `GET /api/field-inventory/practice/admin`
- `GET /api/field-inventory/practice/coach`
- `POST /api/field-inventory/practice/mappings/divisions`
- `POST /api/field-inventory/practice/mappings/teams`
- `POST /api/field-inventory/practice/policies`
- `POST /api/field-inventory/practice/normalize`
- `POST /api/field-inventory/practice/requests`
- `PATCH /api/field-inventory/practice/requests/{requestId}/move`
- `PATCH /api/field-inventory/practice/requests/{requestId}/cancel`
- `PATCH /api/field-inventory/practice/requests/{requestId}/approve`
- `PATCH /api/field-inventory/practice/requests/{requestId}/reject`

Legacy `/api/practice-requests` and direct practice-slot claim routes remain compatibility surfaces only. They are not the intended coach/admin product flow.

## 2. Canonical Data Model

- Committed field inventory is the upstream source of practice availability.
- Normalized practice availability is represented in `GameSwapSlots`.
- Normalized practice slot ids must be deterministic and produced by `SlotKeyUtil.BuildAvailabilitySlotId(...)`.
- Practice request rows currently continue through the practice request service and its request store while the normalized workflow owns coach/admin behavior.

## 3. Normalization States

Each derived 90-minute practice block must resolve to one of:

- `ready`
- `normalized`
- `conflict`
- `blocked`

Meaning:

- `ready`: requestable imported block with no canonical slot written yet
- `normalized`: canonical slot exists and matches the imported block
- `conflict`: overlapping or incompatible canonical slot state exists
- `blocked`: requestability or mapping rules prevent normalization

Conflicts must be surfaced to admins. They must not be silently overwritten during normalization.

## 4. Booking Policy Model

Each practice block must resolve to one of:

- `auto_approve`
- `commissioner_review`
- `not_requestable`

Meaning:

- `auto_approve`: coach request is confirmed immediately if capacity remains
- `commissioner_review`: request is created pending commissioner action
- `not_requestable`: block is hidden from coach request flow

## 5. Capacity Model

- Each normalized practice block currently supports one active team reservation.
- Remaining capacity must be recalculated from approved and pending requests.
- A coach cannot request or move into a block with no remaining capacity unless that block is already tied to that same active request.

## 6. Request Status Model

Practice requests may be:

- `Pending`
- `Approved`
- `Rejected`
- `Cancelled`

Move requests are still practice requests. They additionally carry move metadata describing the source request and source slot details.

## 7. Admin Behavior

### 7.1 Admin view

`GET /api/field-inventory/practice/admin` must return the imported rows, normalized block projection, request queue, and summary counts needed to run the workflow from one surface.

### 7.2 Mapping writes

Division alias, team alias, and booking-policy writes must return a refreshed admin view so the UI stays aligned with the latest normalization state.

### 7.3 Normalize availability

`POST /api/field-inventory/practice/normalize` must:

- accept date-range scoped normalization
- support dry-run preview
- only create or update canonical practice availability for `ready` blocks
- leave `conflict` and `blocked` blocks untouched
- return both a normalization result summary and a refreshed admin view

## 8. Coach Behavior

### 8.1 Coach view

`GET /api/field-inventory/practice/coach` must return only requestable normalized or ready blocks relevant to the authenticated coach's team/division plus that team's current requests.

### 8.2 Create request

`POST /api/field-inventory/practice/requests` must:

- require a normalized or ready practice block
- reject non-requestable or conflict-blocked inventory
- enforce division/team authorization
- apply booking-policy behavior immediately

### 8.3 Move request

`PATCH /api/field-inventory/practice/requests/{requestId}/move` must:

- require an active source request
- create a replacement request against another normalized or ready block
- preserve the source request until the replacement request is approved or auto-approved
- reject moves into the same slot or a full slot

### 8.4 Cancel request

`PATCH /api/field-inventory/practice/requests/{requestId}/cancel` must release the reserved or confirmed capacity associated with that request.

## 9. Commissioner Review

- `approve` confirms pending commissioner-reviewed requests
- `reject` rejects pending commissioner-reviewed requests and reopens capacity
- approving a move must finalize the replacement request before the source request is released

## 10. League Scoping and Auth

- All endpoints must remain header-scoped by `x-league-id`
- coach actions must remain team/division-scoped
- commissioner actions require league-admin or global-admin permissions

## 11. Legacy Boundary

The following routes are not the authoritative contract for the current product flow:

- legacy `/api/practice-requests` endpoints
- direct `POST /api/slots/{division}/{slotId}/practice`

If they remain in the codebase for compatibility or debugging, they must not drive the primary coach/admin experience.

## 12. Change Control

- Any behavior change to normalization, request, move, cancel, approve, or reject behavior must update this contract in the same change set.
- UI help text and workflow documentation must stay aligned with this contract.
