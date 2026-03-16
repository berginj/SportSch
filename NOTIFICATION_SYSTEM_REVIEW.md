# Notification & Email System Review

Comprehensive review of notification and email alert capabilities.

Generated: 2026-03-10
Status: System built but not fully wired up

---

## ✅ **WHAT EXISTS (Infrastructure Complete)**

### **1. NotificationService (In-App Notifications)**

**File:** `api/Services/NotificationService.cs`
**Status:** ✅ **COMPLETE**

**Capabilities:**
- Create notifications for users
- Query user notifications (paginated)
- Get unread count
- Mark as read
- Mark all as read
- Delete notifications

**Storage:** GameSwapNotifications table
**Partition:** By userId
**Features:**
- Type, message, link
- Related entity tracking
- Read/unread status
- Timestamps

---

### **2. EmailService (SendGrid Integration)**

**File:** `api/Services/EmailService.cs`
**Status:** ✅ **COMPLETE with graceful degradation**

**Capabilities:**
- Send emails via SendGrid
- Queue emails if SendGrid fails
- Retry logic
- Graceful fallback (queues if not configured)

**Configuration Needed:**
```
SENDGRID_API_KEY=your_api_key_here
EMAIL_FROM_ADDRESS=noreply@fifthseasonadvisors.com
EMAIL_FROM_NAME=Fifth Season Sports
```

**Behavior:**
- If configured: Sends immediately via SendGrid
- If not configured: Queues to EmailQueue table (warning logged)
- If send fails: Queues for retry

**SendGrid Method:**
```csharp
private async Task SendEmailViaSendGridAsync(
    string to,
    string subject,
    string body,
    string emailId)
{
    var msg = new SendGridMessage
    {
        From = new EmailAddress(_fromEmail, _fromName),
        Subject = subject,
        PlainTextContent = body,
        HtmlContent = body
    };
    msg.AddTo(new EmailAddress(to));

    var response = await _sendGridClient.SendEmailAsync(msg);

    if (!response.IsSuccessStatusCode)
        throw new Exception($"SendGrid returned {response.StatusCode}");
}
```

---

### **3. GameReminderFunction (Timer Trigger)**

**File:** `api/Functions/GameReminderFunction.cs`
**Status:** ✅ **COMPLETE**

**Schedule:** Runs **hourly** (NCRONTAB: "0 0 * * * *")

**What It Does:**
1. Queries all leagues
2. For each league:
   - Finds upcoming confirmed games
   - Checks if within reminder windows (24h or 2h before game)
   - Gets coaches for participating teams
   - Checks user email preferences
   - Sends reminder emails
3. Logs reminder count

**Reminder Windows:**
- 24 hours before game (window: 23-25 hours out)
- 2 hours before game (window: 90-150 minutes out)

**Prevents Duplicates:**
- Uses ReminderDispatches table
- Tracks which reminders already sent
- Won't resend for same game

---

### **4. NotificationPreferencesService**

**File:** `api/Services/NotificationPreferencesService.cs`
**Status:** ✅ **COMPLETE**

**Capabilities:**
- Get user notification preferences
- Update preferences
- Honor user opt-outs

**Storage:** GameSwapNotificationPreferences table

---

## ⚠️ **CRITICAL GAP: NO TRIGGERS WIRED UP**

### **The Problem:**

**Notification service exists BUT:**
```bash
grep -r "CreateNotificationAsync" api/Functions/*.cs
Result: 0 matches ❌
```

**Email service exists BUT:**
```bash
grep -r "QueueEmailAsync" api/Functions/*.cs
Result: 0 matches ❌
```

**Game reminders work BUT:**
- Only sends reminders for upcoming games
- Doesn't notify on request approvals, cancellations, etc.

### **What's Missing:**

**Notifications NOT Triggered For:**
1. ❌ Slot request approved/denied
2. ❌ Slot cancelled
3. ❌ Practice request approved/denied
4. ❌ Access request approved/denied
5. ❌ Coach assigned to team
6. ❌ Schedule changes
7. ❌ Game time changes

**Emails NOT Sent For:**
1. ❌ Request approvals/denials
2. ❌ Cancellations
3. ❌ Team assignments
4. ❌ Access grants

---

## 🔧 **WHAT NEEDS TO BE DONE**

### **Quick Wins (2-3 hours) - Wire Up Basic Notifications**

#### **Add to CreateSlotRequest (api/Services/RequestService.cs):**
```csharp
// After creating request
await _notificationService.CreateNotificationAsync(
    offeringCoachUserId,
    leagueId,
    "SLOT_REQUEST_RECEIVED",
    $"{requestingTeam} requested your {gameDate} {startTime} slot",
    link: $"/calendar?division={division}&date={gameDate}",
    relatedEntityId: slotId,
    relatedEntityType: "SlotRequest"
);

await _emailService.QueueEmailAsync(
    to: offeringCoachEmail,
    subject: "Game Slot Request",
    body: BuildRequestReceivedEmailBody(requestingTeam, gameDate, startTime, fieldName),
    emailType: "SlotRequest",
    userId: offeringCoachUserId,
    leagueId: leagueId
);
```

#### **Add to ApproveSlotRequest:**
```csharp
// After approving
await _notificationService.CreateNotificationAsync(
    requestingUserId,
    leagueId,
    "SLOT_REQUEST_APPROVED",
    $"Your request for {gameDate} {startTime} was approved!",
    link: $"/calendar?division={division}&date={gameDate}"
);

await _emailService.QueueEmailAsync(
    to: requestingEmail,
    subject: "Game Request Approved!",
    body: BuildRequestApprovedEmailBody(...),
    emailType: "RequestApproved"
);
```

#### **Add to CancelSlot:**
```csharp
// After cancellation
if (confirmedTeamId exists)
{
    await _notificationService.CreateNotificationAsync(
        confirmedTeamCoachUserId,
        leagueId,
        "SLOT_CANCELLED",
        $"Game on {gameDate} {startTime} was cancelled",
        link: $"/calendar?division={division}"
    );

    await _emailService.QueueEmailAsync(
        to: confirmedTeamCoachEmail,
        subject: "Game Cancelled",
        body: BuildCancellationEmailBody(...),
        emailType: "GameCancelled"
    );
}
```

---

## 📧 **EMAIL CONFIGURATION**

### **Current Status:**
- ✅ SendGrid integrated
- ✅ Graceful fallback (queues if not configured)
- ⚠️ **SendGrid API key NOT configured**

### **To Enable Email Sending:**

**Step 1: Get SendGrid API Key**
1. Sign up at https://sendgrid.com (free tier: 100 emails/day)
2. Create API key
3. Verify sender email address

**Step 2: Configure in Azure**
```
Azure Static Web App → Configuration → Application Settings:

SENDGRID_API_KEY=SG.xxxxxxxxxxxxx
EMAIL_FROM_ADDRESS=noreply@fifthseasonadvisors.com
EMAIL_FROM_NAME=Fifth Season Sports
```

**Step 3: Test**
```
Trigger any notification action (request slot, approve, etc.)
Check logs for "Sent email" or "SendGrid not configured"
```

---

## ⚠️ **CURRENT BEHAVIOR (Without SendGrid)**

**When notification triggered:**
1. ✅ In-app notification created (works!)
2. ✅ Email queued to EmailQueue table
3. ⚠️ Email NOT sent (SendGrid not configured)
4. ✅ Warning logged
5. ⚠️ Users don't get email alerts

**With SendGrid Configured:**
1. ✅ In-app notification created
2. ✅ Email sent immediately via SendGrid
3. ✅ Email logged as sent
4. ✅ Users get email alerts

---

## 🎯 **PRIORITY FIXES**

### **CRITICAL (Do First) - 2-3 hours**

**Wire Up Core Notifications:**

**Priority 1: Slot Requests (30 min)**
- File: api/Services/RequestService.cs
- Add: CreateNotificationAsync + QueueEmailAsync
- Triggers: Request received, Request approved, Request denied

**Priority 2: Slot Cancellations (20 min)**
- File: api/Services/SlotService.cs
- Add: Notifications for cancellations
- Triggers: Slot cancelled (notify confirmed team)

**Priority 3: Practice Approvals (20 min)**
- File: api/Services/PracticeRequestService.cs
- Add: Notifications for practice requests
- Triggers: Practice approved, Practice denied

**Priority 4: Access Requests (20 min)**
- File: api/Functions/AccessRequestsFunctions.cs
- Add: Notifications for access grants
- Triggers: Access approved, Access denied

**Priority 5: Team Assignments (20 min)**
- File: api/Functions/MembershipsFunctions.cs
- Add: Notifications for coach assignments
- Triggers: Coach assigned to team

**Total:** ~2-3 hours to wire up core notification triggers

---

### **MEDIUM (Nice to Have) - 1-2 hours**

**Email Templates:**
- Create HTML email templates (currently plain text)
- Add team logos
- Better formatting
- Consistent branding

**Queue Processor:**
- Timer function to retry failed emails
- Process EmailQueue table
- Retry with exponential backoff

---

## 🧪 **TESTING PLAN**

### **Test Without SendGrid (Current State):**
```
1. Trigger notification (e.g., request a slot)
2. Check:
   - In-app notification appears? ✓
   - Email queued to EmailQueue table? ✓
   - Warning logged about SendGrid? ✓
   - Email NOT sent? ✓ (expected)
```

### **Test With SendGrid (After Configuration):**
```
1. Configure SENDGRID_API_KEY
2. Trigger notification
3. Check:
   - In-app notification appears? ✓
   - Email sent via SendGrid? ✓
   - Recipient receives email? ✓
   - Email logged as sent? ✓
```

---

## 📊 **RECOMMENDATION**

### **Immediate (This Session - 30 min):**
1. ✅ Wire up slot request notifications (highest value)
2. Document remaining triggers for next session

### **Next Session (2-3 hours):**
1. Complete all notification triggers
2. Create HTML email templates
3. Test with SendGrid

### **Production Deployment:**
1. Configure SendGrid API key
2. Verify sender email
3. Test email delivery
4. Monitor email queue

---

## ✅ **CURRENT CAPABILITY**

**What Works Now:**
- ✅ Game reminders (24h and 2h before games) - AUTOMATIC
- ✅ In-app notifications (infrastructure ready)
- ✅ Email queuing (graceful degradation)

**What Needs Wiring:**
- ⚠️ Slot request notifications (not triggered)
- ⚠️ Access request notifications (not triggered)
- ⚠️ Cancellation notifications (not triggered)

**What Needs Configuration:**
- ⚠️ SendGrid API key (to actually send emails)

---

## 🎯 **IMMEDIATE ACTION**

**Can I wire up slot request notifications now (30 min)?**
This would give you:
- Notifications when someone requests your slot
- Notifications when your request is approved/denied
- Immediate value for users

**Or wrap up and do it fresh in next session?**

**Session: 758k tokens**
