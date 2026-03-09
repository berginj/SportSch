# Practice Space Workflow

This workflow replaces the old coach-facing practice slot request tool with an inventory-backed practice space flow sourced from committed AGSA field inventory.

## Product Shape

There are now two distinct surfaces:

- Admin review
  - `Manage -> Practice Space Admin`
  - review imported field availability/utilization in one place
  - align imported division/team text to canonical SportsCH values
  - save reusable group booking policies
  - approve or reject commissioner-reviewed practice requests
- Coach request page
  - `Practice`
  - browse requestable 90-minute practice blocks
  - see whether a block auto-approves or needs commissioner review
  - track and cancel the team’s requests

## How Requestable Space Is Derived

Requestable space only comes from committed field inventory records that are:

- `AvailabilityStatus = available`
- `UtilizationStatus = not_used`

Those records are normalized into:

- 90-minute request blocks
- capacity of 2 teams per block

Booking policy is then applied:

- `auto_approve`
  - Ponytail-assigned space
  - coach request is confirmed immediately if capacity remains
- `commissioner_review`
  - available but unassigned space
  - coach request is created as pending until a commissioner approves it
- `not_requestable`
  - used, unavailable, or still missing a clear policy mapping

## Admin Walkthroughs

### Iteration 1: First import review

1. Open `Manage -> Practice Space Admin`.
2. Review imported rows by date, field, group, raw division text, and raw team/event text.
3. Save division mappings where AGSA workbook language does not match canonical SportsCH division codes.
4. Save team mappings where workbook team/event text should resolve to a real SportsCH team.
5. Save booking policies for reusable group labels.

Expected result:

- unresolved mapping counts shrink
- previously blocked rows become requestable
- coach page starts showing more actionable space

### Iteration 2: Ponytail-assigned space

1. Find rows where imported group is `Ponytail`.
2. Confirm they resolve to `auto_approve`.
3. Verify the row now shows requestable 90-minute blocks.

Expected result:

- coaches requesting those blocks are approved immediately
- the request queue does not grow for those blocks unless capacity is exhausted

### Iteration 3: Unassigned county availability

1. Find rows with available/not-used space and no assigned group, division, or team/event.
2. Confirm they resolve to `commissioner_review`.
3. Watch pending requests arrive in the request queue.
4. Approve or reject from the same admin surface.

Expected result:

- coaches can ask for open county space
- commissioners keep control over unassigned inventory

## Coach Walkthroughs

### Scenario 1: Coach requests Ponytail space

1. Open `Practice`.
2. Filter to the desired day and look for `Auto-approve`.
3. Click `Book Now`.

Result:

- request becomes `Approved` immediately
- one of the two seats is consumed

### Scenario 2: Coach requests unassigned available space

1. Open `Practice`.
2. Filter to `Commissioner review`.
3. Click `Request for Approval`.

Result:

- request becomes `Pending`
- the seat is reserved while awaiting commissioner decision
- commissioner approves or rejects from `Practice Space Admin`

### Scenario 3: Two teams share one practice block

1. Team A requests a 90-minute block.
2. Team B requests the same block while one seat remains.

Result:

- the block can hold both teams
- once capacity reaches 2, the block is full

### Scenario 4: Coach cancels a request

1. Open `Practice -> My Practice Requests`.
2. Cancel a pending or approved request no longer needed.

Result:

- capacity reopens for another team
- the admin queue and coach page stay aligned

## Coach Help Cues

The coach page should always make these ideas explicit:

- why a block is auto-approved vs commissioner-reviewed
- that each practice block is 90 minutes
- that each block allows 2 teams
- that cancelled requests reopen capacity

Current UI support:

- hover hints on filters and policy labels
- in-page help links
- a dedicated `How This Works` section on the coach page
