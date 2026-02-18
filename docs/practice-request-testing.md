# Practice Request Validation

This runbook gives you a repeatable way to test and validate the full practice request workflow against your live/local API.

## What it validates

The script validates:

1. Coach can create a practice request.
2. Duplicate request for the same team + slot is blocked.
3. Pending request appears in admin query results.
4. Coach cannot approve/reject (role gate check).
5. Admin can approve or reject.
6. Final approved/rejected request appears in filtered results.

## Prerequisites

1. API is running (`api/` Azure Functions host), for example at `http://localhost:7071`.
2. `x-user-id` test identities have memberships in the target league.
3. At least one open slot exists in the target division.
4. If you want role-gate validation, use:
   - coach identity with role `Coach`
   - admin identity with role `LeagueAdmin` or global admin

## Command

```bash
npm run test:practice-requests -- \
  --api-base http://localhost:7071 \
  --league-id BGSB2026 \
  --division 10U \
  --team-id Panthers \
  --coach-user-id coach-test-user \
  --admin-user-id league-admin-user \
  --decision approve
```

Optional flags:

- `--slot-id <slotId>`: force a specific open slot instead of auto-picking one.
- `--decision reject`: run rejection flow instead of approval.
- `--skip-coach-review-check`: skip the 403 role-gate check.
- `--coach-email` and `--admin-email`: override fallback email headers.

## Environment variable option

You can set environment variables instead of passing every flag:

```bash
API_BASE_URL=http://localhost:7071
LEAGUE_ID=BGSB2026
DIVISION=10U
TEAM_ID=Panthers
COACH_USER_ID=coach-test-user
ADMIN_USER_ID=league-admin-user
```

Then run:

```bash
npm run test:practice-requests
```

## Expected result

Successful run ends with:

```text
Validation passed for request <requestId>. Final status: Approved.
```

If a validation fails, the script exits non-zero and prints the failing step and API response details.

