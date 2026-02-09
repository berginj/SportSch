# Coach Onboarding System

Complete guide for setting up and using the coach onboarding workflow.

## Overview

The coach onboarding system guides coaches through team setup before the season starts. It includes:

1. **Team Information** - Update team name, contact info, and add assistant coaches
2. **Practice Requests** - Request 1-3 practice slots (requires commissioner approval)
3. **Clinic Preferences** - Select preferred time window for league-wide events
4. **Schedule Review** - View upcoming games for the season
5. **Progress Tracking** - Visual checklist showing completion status

---

## For Commissioners

### Step 1: Generate Coach Links

1. Navigate to **League Management → Coach Links**
2. View all teams with their contact information
3. Filter by division or onboarding status
4. Generate personalized links for each team

**Actions**:
- **Copy Individual Link**: Click "📋 Copy" next to any team
- **Copy All as CSV**: Click "📋 Copy All as CSV" to get all links in CSV format
- **Download CSV**: Click "⬇️ Download CSV" to get a file for mail merge

**CSV Format**:
```csv
Team Name,Division,Coach Email,Onboarding Link
Panthers,10U,coach@example.com,https://yourapp.com/?leagueId=BGSB2026&teamId=Panthers#coach-setup
```

### Step 2: Send Links to Coaches

**Option A: Manual Emails**
1. Copy individual links and paste into emails
2. Send personalized messages to each coach

**Option B: Mail Merge (Recommended)**
1. Download the CSV file
2. Open in Excel or Google Sheets
3. Use mail merge to send bulk emails with personalized links

**Email Template**:
```
Subject: Complete Your Team Setup - [League Name] [Season]

Hi [Coach Name],

Welcome to [League Name]! Please complete your team setup before [Deadline].

Your personalized setup link: [Onboarding Link]

This will take about 5-10 minutes and includes:
- Team name and contact information
- Practice slot requests (select 1-3 preferred times)
- Clinic preferences
- Schedule review

If you have any questions, reply to this email.

Thanks!
[Your Name]
[League Administrator]
```

### Step 3: Monitor Progress

Navigate to **League Management → Coach Links** to see:
- **Total Teams**: Count of all teams in the league
- **Onboarding Complete**: Teams that finished setup (green checkmark)
- **Pending Setup**: Teams that haven't completed setup

**Filter Options**:
- By division (e.g., "10U", "12U")
- By status ("Complete" or "Incomplete")

### Step 4: Approve Practice Requests

1. Navigate to **League Management → Practice Requests**
2. View all pending requests from coaches
3. Review request details:
   - Team name and division
   - Requested date, time, and location
   - Request timestamp

**Actions**:
- **Approve**: Click "Approve" to confirm the practice slot
- **Reject**: Click "Reject" if the slot is no longer available

**Status Tabs**:
- **Pending**: Requests awaiting your review (⏳)
- **Approved**: Requests you've approved (✅)
- **Rejected**: Requests you've declined (❌)
- **All**: Complete history of requests

**Best Practices**:
- Review requests within 24-48 hours
- Approve slots on a first-come, first-served basis
- Reject with a reason if slot is unavailable
- Monitor the Pending tab regularly during onboarding period

---

## For Coaches

### Accessing Your Onboarding Page

1. Click the personalized link sent by your league administrator
2. Sign in with your account (if not already signed in)
3. You'll land on your team's onboarding page

**URL Format**: `https://yourapp.com/?leagueId={leagueId}&teamId={teamId}#coach-setup`

### Completing Setup

#### Section 1: Team Information

**Update the following**:
- **Team Name**: Choose a name for your team (default is team ID)
- **Primary Contact**: Your name, email, and phone number
- **Assistant Coaches**: Add up to 3 assistant coaches with their contact info

Click **"Save Team Information"** when done.

#### Section 2: Practice Slot Requests

**Select 1-3 practice slots**:
1. Browse available practice slots (dates, times, locations)
2. Click **"Request"** on slots that work for your team
3. Wait for commissioner approval

**Rules**:
- Maximum 3 requests per team
- Requests require commissioner approval
- First-come, first-served priority
- You can request slots even before completing other sections

**Status Indicators**:
- **Pending Approval**: ⏳ Waiting for commissioner review
- **Approved**: ✅ Slot confirmed for your team
- **Rejected**: ❌ Slot no longer available

#### Section 3: Clinic Preference

**Select your preferred time window** for league-wide clinics and open practices:
- Weekday Evenings (6-9 PM)
- Saturday Mornings (8-11 AM)
- Saturday Afternoons (12-3 PM)
- Sunday Mornings (8-11 AM)
- Sunday Afternoons (12-3 PM)

This helps the league schedule events at convenient times for most teams.

#### Section 4: Schedule Review

**View your team's game schedule**:
- Next 15 games with dates, times, and locations
- Home vs Away indicator
- Opponent information

**Note**: Games may not appear until the league finalizes the schedule.

### Progress Tracking

The setup page shows a progress bar with 6 checklist items:

- ✅ Set team name
- ✅ Complete contact information
- ✅ Add assistant coach(es)
- ✅ Request practice slots (1-3)
- ✅ Set clinic preference
- ✅ Review schedule & complete setup

Once you've completed at least 50% (3 items), you can click **"Complete Onboarding Setup"** to mark your setup as done.

### After Completing Onboarding

- You can still update your team information at any time
- Your onboarding page shows "✅ Onboarding Complete"
- You can access the page again via the same link

---

## Technical Details

### API Endpoints

#### Practice Requests

**Create Request** (Coach or Admin)
```http
POST /api/practice-requests
Content-Type: application/json

{
  "division": "10U",
  "teamId": "Panthers",
  "slotId": "slot-123",
  "reason": "Practice request from coach onboarding"
}
```

**Get Requests** (Coach sees own, Admin sees all)
```http
GET /api/practice-requests?status=Pending&teamId=Panthers
```

**Approve Request** (Admin only)
```http
PATCH /api/practice-requests/{requestId}/approve
Content-Type: application/json

{
  "reason": "Approved by commissioner"
}
```

**Reject Request** (Admin only)
```http
PATCH /api/practice-requests/{requestId}/reject
Content-Type: application/json

{
  "reason": "Slot no longer available"
}
```

#### Team Updates

**Update Team** (Coach can edit own team, Admin can edit any)
```http
PATCH /api/teams/{division}/{teamId}
Content-Type: application/json

{
  "name": "Panthers",
  "primaryContact": {
    "name": "John Doe",
    "email": "john@example.com",
    "phone": "(555) 123-4567"
  },
  "assistantCoaches": [
    {
      "name": "Jane Smith",
      "email": "jane@example.com",
      "phone": "(555) 987-6543"
    }
  ],
  "clinicPreference": "weekday-evenings",
  "onboardingComplete": true
}
```

### Data Models

**PracticeRequest**:
```typescript
{
  requestId: string;
  division: string;
  teamId: string;
  slotId: string;
  status: "Pending" | "Approved" | "Rejected";
  reason?: string;
  requestedUtc: DateTimeOffset;
  reviewedUtc?: DateTimeOffset;
  reviewedBy?: string;
  slot?: {
    slotId: string;
    gameDate: string;
    startTime: string;
    endTime: string;
    displayName?: string;
    fieldKey?: string;
  };
}
```

**Team** (extended):
```typescript
{
  division: string;
  teamId: string;
  name: string;
  primaryContact: {
    name?: string;
    email?: string;
    phone?: string;
  };
  assistantCoaches?: Array<{
    name?: string;
    email?: string;
    phone?: string;
  }>;
  clinicPreference?: string;
  onboardingComplete: boolean;
}
```

### Pages and Components

**Coach-Facing**:
- `CoachOnboardingPage.jsx` - Main onboarding page at `#coach-setup`
- Accessible via: `/?leagueId={leagueId}&teamId={teamId}#coach-setup`

**Commissioner-Facing**:
- `PracticeRequestsManager.jsx` - Approve/reject practice requests
- `CoachLinksGenerator.jsx` - Generate and export onboarding links
- Both accessible via **League Management** tab

---

## Frequently Asked Questions

### For Commissioners

**Q: Can I resend links after the season starts?**
A: Yes, the links remain valid. Coaches can access them at any time to update team information.

**Q: What if a coach loses their link?**
A: Regenerate it from **League Management → Coach Links** and copy the link again.

**Q: Can I bulk approve all practice requests?**
A: Not currently. Each request must be reviewed individually to ensure fairness and availability.

**Q: How do I know if all coaches completed onboarding?**
A: Check the stats in **League Management → Coach Links**. Green checkmarks indicate complete.

### For Coaches

**Q: Can I change my practice requests after submitting?**
A: Not directly. Contact your league administrator if you need to change a request.

**Q: What if I don't see any available practice slots?**
A: The league may not have released practice slots yet, or all slots may be taken. Contact your administrator.

**Q: Can I update my team name after completing onboarding?**
A: Yes, revisit your onboarding link and update the information. Changes are saved immediately.

**Q: I added the wrong email for my assistant coach. Can I fix it?**
A: Yes, click "Remove" next to the assistant coach, then add them again with the correct information.

---

## Troubleshooting

### Issue: Link doesn't work / Page shows "Team Assignment Required"

**Cause**: The coach's account is not assigned to the team in the URL.

**Solution** (Commissioner):
1. Go to **League Management → Settings → Teams & Coaches**
2. Find the coach in the list
3. Assign them to the correct team
4. Ask the coach to refresh the page

### Issue: Can't request practice slots / No slots visible

**Cause**: No availability slots marked as practice-capable exist for that division.

**Solution** (Commissioner):
1. Go to **League Management → Fields → Availability Slots**
2. Generate availability slots
3. Mark slots with `isAvailability=true` for practice use
4. Slots will appear in the onboarding page

### Issue: Practice request stuck as "Pending"

**Cause**: Commissioner hasn't reviewed the request yet.

**Solution** (Coach):
- Wait 24-48 hours for review
- Contact the commissioner if urgent

**Solution** (Commissioner):
- Go to **League Management → Practice Requests**
- Review and approve/reject the request

### Issue: CSV export is empty

**Cause**: No teams exist in the league yet.

**Solution** (Commissioner):
1. Import teams via **League Management → Settings → Teams & Coaches**
2. Upload CSV with team information
3. Refresh the Coach Links page

---

## Best Practices

### For Commissioners

1. **Send links early**: Give coaches 2-3 weeks before the season starts
2. **Set a deadline**: Clearly communicate when onboarding must be complete
3. **Follow up**: Send reminder emails to coaches who haven't completed setup
4. **Review practice requests promptly**: Approve/reject within 24-48 hours
5. **Monitor progress**: Check the stats regularly to ensure all teams are set up

### For Coaches

1. **Complete early**: Don't wait until the last minute
2. **Request multiple slots**: Increase your chances of getting practice time
3. **Keep information current**: Update contact info if it changes
4. **Review schedule carefully**: Note all game dates and times
5. **Ask questions**: Contact your administrator if anything is unclear

---

## Related Documentation

- [Scheduling Workflow](./scheduling.md) - How the schedule is built
- [Season Wizard Guide](./season-wizard.md) - Using the Season Wizard for scheduling
- [API Documentation](./api.md) - Complete API reference
