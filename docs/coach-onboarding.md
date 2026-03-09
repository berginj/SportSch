# Coach Onboarding

Coach onboarding and practice setup are now intentionally separated.

## Product Scope

- `#coach-setup` remains the team setup handoff page for coaches.
- Practice selection is handled by the Practice Portal, not inline on onboarding.
- The onboarding page surfaces current practice status and links coaches into the normalized practice workflow.

## Coach Flow

1. Open the personalized onboarding link for the correct league and team.
2. Confirm team profile details.
3. Open the Practice Portal from onboarding.
4. Request, move, or cancel practice space there.
5. Return to onboarding to finish setup items and mark onboarding complete.

## Commissioner Flow

1. Generate coach onboarding links from League Management.
2. Send coaches their `#coach-setup` links.
3. Track onboarding completion separately from practice-space activity.
4. Review practice-space requests in `Manage -> Practice Space Admin`.

## Surface Responsibilities

`CoachOnboardingPage.jsx`

- team profile edits
- clinic preference
- schedule review
- onboarding completion
- practice-portal handoff
- summary of the team's active normalized practice requests

`PracticePortalPage.jsx`

- normalized practice-space browsing
- auto-approve vs commissioner-review request flow
- move flow for active requests
- cancellation flow
- request status tracking

## API Touchpoints

Onboarding uses:

- `GET /api/teams?division=...`
- `PATCH /api/teams/{division}/{teamId}`
- `GET /api/field-inventory/practice/coach`
- `GET /api/slots?division=...&status=Confirmed&dateFrom=...&dateTo=...`

Practice Portal uses:

- `GET /api/field-inventory/practice/coach`
- `POST /api/field-inventory/practice/requests`
- `PATCH /api/field-inventory/practice/requests/{requestId}/move`
- `PATCH /api/field-inventory/practice/requests/{requestId}/cancel`

Practice Space Admin uses:

- `GET /api/field-inventory/practice/admin`
- `POST /api/field-inventory/practice/mappings/divisions`
- `POST /api/field-inventory/practice/mappings/teams`
- `POST /api/field-inventory/practice/policies`
- `POST /api/field-inventory/practice/normalize`
- `PATCH /api/field-inventory/practice/requests/{requestId}/approve`
- `PATCH /api/field-inventory/practice/requests/{requestId}/reject`

## Notes

- Coaches can revisit onboarding after completion.
- Practice setup now depends on normalized field-inventory blocks rather than the legacy practice-request page.
- Use `docs/practice-space-workflow.md` for the current coach/admin behavior.
- Legacy `/api/practice-requests` endpoints are compatibility surfaces, not the intended onboarding path.
