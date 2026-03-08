# Coach Onboarding

Current product scope:
- `#coach-setup` is the team setup handoff page for coaches.
- Practice selection is not embedded on onboarding anymore.
- Recurring requests, one-off practice booking, and commissioner approval flow live in the Practice Portal.

## Coach flow

1. Open the personalized onboarding link for your league/team.
2. Confirm team information:
   - team name
   - primary contact
   - assistant coaches
3. Open the Practice Portal from onboarding to handle practice requests.
4. Set clinic preference.
5. Review the current schedule.
6. Mark onboarding complete when ready.

## Commissioner flow

1. Generate coach links from League Management.
2. Send coaches their `#coach-setup` links.
3. Track completion status from the coach link workflow and team records.
4. Review practice requests in the Practice Requests manager / Practice Portal workflow.

## Links

Example onboarding link:

```text
https://yourapp.com/?leagueId=BGSB2026&teamId=Panthers#coach-setup
```

The onboarding page keeps the coach in authenticated league scope and routes practice work to `#practice`.

## Page responsibilities

`CoachOnboardingPage.jsx`
- team profile edits
- clinic preference
- schedule review
- onboarding completion
- Practice Portal handoff

`PracticePortalPage.jsx`
- recurring practice requests
- one-off practice booking
- division eligibility and gate checks
- request status tracking

## API touchpoints

Onboarding uses:
- `GET /api/teams?division=...`
- `PATCH /api/teams/{division}/{teamId}`
- `GET /api/practice-requests?teamId=...`
- `GET /api/slots?division=...&status=Confirmed&dateFrom=...&dateTo=...`

Practice Portal uses:
- `GET /api/practice-portal/settings`
- `POST /api/practice-requests`
- `GET /api/practice-requests`
- `PATCH /api/practice-requests/{requestId}/approve`
- `PATCH /api/practice-requests/{requestId}/reject`
- `POST /api/slots/{division}/{slotId}/practice`

## Notes

- Coaches can still revisit onboarding after completion.
- Team setup and practice setup are intentionally separated now to avoid duplicate workflows.
- Use `docs/contract.md` and `docs/PRACTICE_REQUESTS_AND_CLAIMS_BEHAVIORAL_CONTRACT.md` for the canonical API and workflow rules.
