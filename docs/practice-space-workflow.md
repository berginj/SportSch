# Practice Space Workflow

This document describes the normalized practice-space workflow now used by the admin and coach surfaces.

## Canonical Flow

1. County workbook inventory is imported and committed.
2. Admins review committed rows in `Manage -> Practice Space Admin`.
3. Admins save reusable division aliases, team aliases, and booking policies.
4. Admins preview or apply normalization.
5. Normalization backfills deterministic canonical availability rows into `GameSwapSlots`.
6. Coaches request, move, or cancel practice blocks from the Practice Portal.
7. Commissioner-reviewed requests are approved or rejected from the same admin surface.

## Primary Surfaces

- `Manage -> Practice Space Admin`
  - review committed inventory rows
  - inspect normalization state at the 90-minute block level
  - save mapping and booking-policy decisions
  - normalize missing canonical availability
  - query canonical availability by date, division, and exact time window
  - approve or reject pending practice requests, including moves
- `Practice`
  - browse normalized practice blocks for the signed-in coach's team
  - request auto-approve or commissioner-review space
  - move an active request to another normalized block
  - cancel a pending or approved request
- `Coach Onboarding`
  - hands coaches off to the Practice Portal
  - shows current normalized practice request status

## Normalization Model

Committed inventory only becomes requestable when it is both:

- `AvailabilityStatus = available`
- `UtilizationStatus = not_used`

Each eligible row is split into 90-minute blocks. Each block is evaluated into one of these states:

- `ready`
  - the imported block is requestable and can be backfilled into `GameSwapSlots`
- `normalized`
  - a matching canonical availability slot already exists with aligned metadata
- `conflict`
  - overlapping or incompatible canonical data already exists and needs admin review
- `blocked`
  - the imported block cannot be normalized yet because of mapping gaps, requestability rules, or existing usage

Normalization writes deterministic slot ids by using `SlotKeyUtil.BuildAvailabilitySlotId(...)`.

## Booking Policies

- `auto_approve`
  - imported group policy allows immediate confirmation
  - requests are approved immediately while the block is still available
- `commissioner_review`
  - block is requestable but requires commissioner approval
  - request remains pending until approved or rejected
- `not_requestable`
  - used, unavailable, or missing enough mapping/policy information to expose safely

Each normalized practice block can be booked exclusively or shared with one named partner team.

## Admin Workflow

### First pass

1. Open `Manage -> Practice Space Admin`.
2. Review imported rows and the normalization calendar/table together.
3. Save division aliases where workbook text does not match canonical division codes.
4. Save team aliases where workbook team text should map to a canonical team.
5. Save booking policy mappings for reusable imported group labels.

### Normalize missing availability

1. Use `Preview Normalize` to inspect the target date range.
2. Filter to `Missing`, `Conflict`, or a specific issue when needed.
3. Use `Normalize Missing` for a range or `Normalize Day` for a single block.
4. Re-check conflicts before exposing that space to coaches.

### Review request queue

1. Filter the request queue by status.
2. Approve or reject pending requests.
3. For move requests, confirm the replacement block before the source request is released.

## Coach Workflow

### Request space

1. Open `Practice`.
2. Filter by day or approval path.
3. Request a normalized block.

Result:

- `Auto-approve` blocks confirm immediately.
- `Commissioner review` blocks become pending.

### Move a request

1. In `My Practice Requests`, choose `Move` on an active request.
2. Select a replacement normalized block.

Result:

- if the target block is `auto_approve`, the move completes immediately
- if the target block needs review, the replacement request stays pending
- the original slot remains active until the move is approved or auto-approved

### Cancel a request

1. Cancel a pending or approved request no longer needed.

Result:

- the normalized block becomes available again if no other active request still holds it
- admin and coach views stay aligned

## Operational Notes

- The admin normalization calendar is the source of truth for imported-vs-canonical drift.
- Conflicts are highlighted, not silently overwritten.
- Requestable coach inventory only comes from normalized or ready practice blocks that are currently available.
- Legacy `/api/practice-requests`, `practice-portal/settings`, and direct slot-claim flows have been removed from the product path.
