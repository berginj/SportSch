# Slot Lifecycle Behavioral Contract

## 1. Scope and Authority

This document is the authoritative contract for slot lifecycle behavior in the GameSwap domain.

- It applies to `GameSwapSlots` and `GameSwapSlotRequests`.
- It applies to slot endpoints under `/api/slots/*`.
- Any backend or UI change that alters slot lifecycle behavior MUST update this document in the same change set.

## 2. Canonical Entities and Keys

- Slot table: `GameSwapSlots`
  - `PartitionKey`: `SLOT|{leagueId}|{division}`
  - `RowKey`: `slotId`
- Slot request table: `GameSwapSlotRequests`
  - `PartitionKey`: `SLOTREQ|{leagueId}|{division}|{slotId}`
  - `RowKey`: `requestId`

All new writes MUST use canonical partitioning above.

## 3. Canonical Slot States

Slots support these lifecycle states:

- `Open`
- `Confirmed`
- `Cancelled`
- `Completed`
- `Postponed`

Practice workflows may also place a slot in `Pending` while a practice request is under commissioner review.

## 4. Canonical Slot Request States

Slot requests support:

- `Pending`
- `Approved`
- `Denied`

Current game-slot accept flow is immediate-confirm and writes request status `Approved` at creation time.

## 5. League Scoping and Authorization

- All slot lifecycle operations MUST be league-scoped and require `x-league-id`.
- If user role is `Viewer`, write operations MUST be denied.
- Coaches MUST be restricted to their assigned division/team where applicable.
- League admins and global admins MAY operate across teams in-league.

Current endpoint policy:

- `POST /slots`: coach (assigned team/division) or admin/global.
- `PATCH /slots/{division}/{slotId}`: admin/global only.
- `PATCH /slots/{division}/{slotId}/cancel`: slot owner team or confirmed team, or admin/global.
- `POST /slots/{division}/{slotId}/requests`: coach/admin/global; exact division match required.
- `PATCH /slots/{division}/{slotId}/status`: admin/global only.

## 6. Lifecycle Transitions

### 6.1 Create Slot

`POST /slots` MUST:

- validate required fields (`division`, `offeringTeamId`, `gameDate`, `startTime`, `endTime`, `fieldKey`),
- validate date/time and field existence/active state,
- reject field-time overlap conflicts,
- create slot in `Open` with:
  - `HomeTeamId = OfferingTeamId`,
  - `AwayTeamId = ""`,
  - `IsExternalOffer = false`,
  - `IsAvailability = false`.

### 6.2 Accept Slot (Create Request)

`POST /slots/{division}/{slotId}/requests` MUST:

- require slot exists and is `Open`,
- reject availability-only slots,
- reject own-slot acceptance,
- enforce exact division match,
- reject assigned non-external slots (`AwayTeamId` set and `IsExternalOffer=false`),
- reject team double-booking against confirmed slot overlaps.

On success, current canonical behavior is immediate confirm:

- create request row with status `Approved`,
- set slot status to `Confirmed`,
- set `ConfirmedTeamId` and `ConfirmedRequestId`,
- best-effort deny other pending requests for the same slot.

### 6.3 No Separate Approve Step

Game-slot acceptance is single-step. There is no separate approve endpoint in the current contract.

- `POST /slots/{division}/{slotId}/requests` is the only acceptance path.
- Successful acceptance MUST create an approved request row and confirm the slot in the same workflow.

### 6.4 Cancel Slot

`PATCH /slots/{division}/{slotId}/cancel` MUST set slot to `Cancelled`.

- Cancel operation SHOULD be idempotent.
- Cancelling a slot does not automatically rewrite or purge related request rows.

### 6.5 Admin Status Update

`PATCH /slots/{division}/{slotId}/status` MUST allow admin/global to set:

- `Open`
- `Confirmed`
- `Cancelled`
- `Completed`
- `Postponed`

When status changes from `Confirmed` to `Cancelled`, cancellation notifications SHOULD be emitted.

### 6.6 Update Slot Date/Time/Field

`PATCH /slots/{division}/{slotId}` MUST:

- be admin/global only,
- reject edits to `Cancelled` slots,
- validate date/time/field values,
- reject conflicts with non-cancelled overlapping slots on the same field,
- persist normalized field metadata (`ParkName`, `FieldName`, `DisplayName`).

## 7. Query and Filtering Semantics

`GET /slots` query behavior:

- status filter MAY be provided.
- if status filter is omitted, cancelled rows SHOULD be excluded by default.
- pagination MUST use continuation token semantics.
- results SHOULD be sorted by date/time/field for stable display.

## 8. Data Integrity Invariants

- Slot ids and request ids MUST remain immutable after creation.
- `ConfirmedTeamId` and `ConfirmedRequestId` MUST represent the accepted opponent when status is `Confirmed`.
- Field key values MUST be normalized to canonical `parkCode/fieldCode`.
- Time values MUST remain `HH:MM` local time fields and date values MUST remain `YYYY-MM-DD`.

## 9. Known Intentional Compatibility Behavior

- Immediate confirm on request create is canonical today.
- Practice-request review remains a separate pending/approve workflow under the practice contract.

## 10. Change Control

- Lifecycle behavior changes MUST be reflected here before or with code changes.
- UI copy and workflow prompts MUST align with this contract.
- Backend endpoints that diverge from this contract MUST be treated as defects.
