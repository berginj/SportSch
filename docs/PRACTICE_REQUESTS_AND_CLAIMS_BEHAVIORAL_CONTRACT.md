# Practice Requests and Claims Behavioral Contract

## 1. Scope and Authority

This document is the authoritative workflow contract for practice-slot request and claim behavior.

- It applies to:
  - `POST /practice-requests`
  - `GET /practice-requests`
  - `PATCH /practice-requests/{requestId}/approve`
  - `PATCH /practice-requests/{requestId}/reject`
  - `POST /slots/{division}/{slotId}/practice`
- It governs slot mutations in `GameSwapSlots`.
- It governs request rows in:
  - `GameSwapPracticeRequests` (commissioner-reviewed workflow),
  - `GameSwapSlotRequests` (direct-claim compatibility flow).

## 2. Canonical Definitions

- `Practice-requestable availability`: slot where:
  - slot is availability (`IsAvailability=true` or `GameType=Availability`), and
  - allocation slot type is `practice` or `both` when present.
- `Practice request`: row in `GameSwapPracticeRequests`.
- `Direct claim`: immediate coach/admin claim path that confirms a practice slot without commissioner review.
- `Recurring pattern approval`: approving one request and confirming matching same weekday/field/time slots from the representative date forward.
- `One-off practice`: additional practice booking allowed only when one-off policy gates pass.

## 3. Storage and Keys

### 3.1 Practice Requests Table

- Table: `GameSwapPracticeRequests`
- `PartitionKey`: `PRACTICEREQ|{leagueId}`
- `RowKey`: `requestId`

### 3.2 Slots Table

- Table: `GameSwapSlots`
- `PartitionKey`: `SLOT|{leagueId}|{division}`
- `RowKey`: `slotId`

### 3.3 Direct Claim Request Rows

Direct claim writes an approved row into `GameSwapSlotRequests` using canonical slot-request keys:

- `PartitionKey`: `SLOTREQ|{leagueId}|{division}|{slotId}`
- `RowKey`: `requestId`

## 4. Status Model

### 4.1 Practice Request Status

- `Pending`
- `Approved`
- `Rejected`

### 4.2 Slot Status in Practice Workflow

- `Open`: available for request/claim.
- `Pending`: reserved by a practice request awaiting commissioner review.
- `Confirmed`: approved and assigned as practice.
- `Cancelled`: not assignable.

## 5. Authorization and League Scoping

- All endpoints MUST require `x-league-id`.
- `CreatePracticeRequest`:
  - coach, league admin, and global admin are allowed,
  - coach MUST match assigned division/team,
  - admin/global MAY act for another team in-division.
- `ApprovePracticeRequest` and `RejectPracticeRequest`:
  - league admin or global admin only.
- `ClaimPracticeSlot`:
  - coach, league admin, or global admin (not viewer),
  - coach MUST match assigned division/team unless admin/global override is used.

## 6. Commissioner-Reviewed Workflow

### 6.1 Create Practice Request

`POST /practice-requests` MUST:

- require `division`, `teamId`, `slotId`,
- require slot exists and slot status is `Open`,
- require slot is practice-requestable availability,
- enforce max 3 active requests per team (`Pending` + `Approved`),
- enforce unique priority 1..3 among active team requests,
- auto-assign a free priority when omitted,
- reject duplicate active request for same team+slot,
- reject if slot already has another active practice request.

On success:

- create `GameSwapPracticeRequests` row in `Pending`,
- reserve slot by setting:
  - `Status = Pending`,
  - `PendingRequestId = requestId`,
  - `PendingTeamId = teamId`.

### 6.2 Approve Practice Request

`PATCH /practice-requests/{requestId}/approve` MUST:

- require request status `Pending`,
- set request status to `Approved`,
- confirm slots for practice using either:
  - single-slot confirmation, or
  - recurring-pattern confirmation.

Confirmed slot mutation MUST set:

- `Status = Confirmed`,
- `ConfirmedRequestId = requestId`,
- `ConfirmedTeamId = teamId`,
- `OfferingTeamId = teamId`,
- `IsAvailability = false`,
- `GameType = Practice`,
- `PracticeBookingMode = RecurringApproved`,
- clear `PendingRequestId` and `PendingTeamId`.

After approval, competing pending practice requests for confirmed slots SHOULD be marked `Rejected`.

### 6.3 Reject Practice Request

`PATCH /practice-requests/{requestId}/reject` MUST:

- require request status `Pending`,
- set request status to `Rejected`,
- release slot reservation:
  - if another pending request exists for same slot, transfer `PendingRequestId/PendingTeamId`,
  - otherwise set slot back to `Open` and clear pending fields.

## 7. Recurring Pattern Approval Rules

When representative slot has valid date/field/time, approval MAY run recurring mode:

- candidate slots are same division, same weekday, same field, same start/end time, date >= representative date,
- cancelled candidates are excluded,
- only one practice per team per week is allowed in this path,
- overlapping same-day team commitments are skipped,
- unavailable or conflicting candidates are skipped (non-representative candidates).

If no recurring week can be confirmed, approval MUST fail for that request.

## 8. Direct Claim Workflow

`POST /slots/{division}/{slotId}/practice` is an immediate confirmation path.

It MUST:

- require slot exists and is `Open`,
- require slot is availability (`IsAvailability=true`),
- enforce exact division match for coaches,
- reject overlap with existing confirmed team slots on same date/time,
- enforce one-practice-per-week unless `oneOffBooking=true`.

When one-off booking is requested by non-admin:

- league/division one-off setting MUST be enabled,
- all teams in that division MUST already have approved recurring practice coverage.

On success, direct claim MUST:

- create approved request row in `GameSwapSlotRequests`,
- confirm slot as practice with:
  - `Status = Confirmed`,
  - `GameType = Practice`,
  - `IsAvailability = false`,
  - `PracticeBookingMode = DirectClaim` or `OneOffDirect`,
  - share-field metadata from request when provided.

## 9. Share-Field Semantics

If `openToShareField=true`:

- `shareWithTeamId` is required,
- target share team MUST exist in same division,
- `shareWithTeamId` MUST differ from requesting team.

If `openToShareField=false`, `shareWithTeamId` MUST be cleared.

## 10. Data and Counting Semantics

- Practice requests and claims operate on availability inventory and practice assignment state.
- They are separate from scheduler-required game-count logic.
- Direct claims and practice approvals MUST preserve league/division slot scoping and canonical key conventions.

## 11. Iteration Notes

This is iteration 1 of this contract. It captures current behavior and expected invariants for:

- practice request reservation/review lifecycle,
- recurring approval semantics,
- direct claim semantics and one-off gating.

Future iterations SHOULD unify the two request-row representations (`GameSwapPracticeRequests` vs `GameSwapSlotRequests`) if policy is changed.

## 12. Change Control

- Any behavior change in practice request or claim workflows MUST update this contract in the same change set.
- UI copy and reviewer guidance MUST remain aligned with this contract.
