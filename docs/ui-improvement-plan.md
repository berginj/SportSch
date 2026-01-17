# UI/UX Improvement Plan
**Date:** 2026-01-17
**Status:** Draft

## Executive Summary

This document outlines comprehensive UI/UX improvements to the Sports Scheduler application focusing on three user personas: Coaches, League Administrators/Commissioners, and Global Admins. The improvements aim to reduce friction, eliminate repetitive data entry, add quality-of-life features, and create role-specific experiences.

## Goals

1. **Streamline workflows** - Reduce clicks and repetitive data entry
2. **Role-specific experiences** - Tailor UI to coach, admin, and global admin needs
3. **Reduce cognitive load** - Smart defaults, contextual actions, inline editing
4. **Improve notifications** - Real-time updates via email and in-app
5. **Calendar integration** - Personal calendar subscriptions (iCal/Google Calendar)
6. **Mobile optimization** - Touch-first design for on-the-go usage

---

## Current State Analysis

### Pagination Implementation (Completed)
✅ Backend endpoints support continuation tokens
✅ Frontend `usePagination` hook handles load-more pattern
✅ Pagination component displays load-more button
✅ Applied to: access requests, memberships, slots (when needed)

### Pain Points Identified

**Coaches:**
- Must navigate to calendar, find open slot, click through to accept
- No notifications when new slots are offered in their division
- Can't see their team's upcoming games at a glance
- Repeatedly enter same field/time preferences when offering slots
- No way to "favorite" or save preferred game times/fields

**League Admins:**
- AdminPage is crowded with global admin features they can't use
- Must switch between sections to complete related tasks (e.g., create team → assign coach)
- Repetitive CSV uploads for similar data (fields, teams, schedules)
- No dashboard showing league health (pending requests, unassigned coaches, schedule gaps)
- Can't bulk-approve access requests
- Schedule generation requires multiple preview/adjust cycles

**Global Admins:**
- Functions mixed with league-specific admin features
- No centralized view of all leagues
- Can't quickly switch between league contexts
- User management scattered across multiple sections

---

## Proposed Improvements by Persona

## 1. Coach Experience Improvements

### A. Dashboard for Coaches
**Goal:** Give coaches a personalized home page with actionable items.

**Features:**
- **My Team Summary Card**
  - Team name, division, record (if tracking wins/losses)
  - Quick link to team roster
  - Upcoming games (next 3 games with date, time, field, opponent)

- **Action Items Card**
  - "You have 2 new slot offers in your division" (with CTA: Review Offers)
  - "3 of your slot offers are still open" (with CTA: View My Offers)
  - "Your team has a game tomorrow at 6 PM" (with CTA: View Details)

- **Quick Actions**
  - "Offer a Game Slot" button (opens modal with smart defaults)
  - "Browse Available Slots" button
  - "View My Team Schedule" button

**Implementation:**
- New route: `/coach` or make `/` redirect to coach dashboard when role=Coach
- New component: `src/pages/CoachDashboard.jsx`
- New endpoint: `GET /coach/dashboard` (aggregates upcoming games, open offers, pending requests)
- Smart defaults: Pre-fill division, team based on coach's assigned team

### B. Smart Slot Offer Form
**Goal:** Reduce repetitive data entry when offering slots.

**Features:**
- **Remember Preferences**
  - Save coach's preferred fields (e.g., "Gunston/Turf")
  - Save typical game times (e.g., "Weekday 6 PM - 8 PM")
  - Pre-populate form with last-used values

- **Quick Templates**
  - "Offer Friday 6 PM at my usual field"
  - "Offer weekend morning slot"
  - Customizable templates saved per coach

- **Bulk Offer**
  - Offer multiple slots at once (e.g., "Every Wednesday in April at Gunston")
  - Preview all slots before creating

**Implementation:**
- Update `CreateSlot` modal with saved preferences
- New localStorage keys: `coach_preferred_fields`, `coach_preferred_times`, `coach_slot_templates`
- New UI component: `src/components/SlotTemplateSelector.jsx`

### C. Slot Notifications
**Goal:** Keep coaches informed of new opportunities without constantly checking the app.

**Features:**
- **Email Notifications**
  - New slot offered in your division → "New game opportunity: Friday 6 PM at Gunston"
  - Your slot offer was accepted → "Your game slot was claimed by Eagles!"
  - Upcoming game reminder → "Reminder: Game tomorrow at 6 PM"

- **In-App Notifications**
  - Bell icon in TopNav showing unread count
  - Dropdown showing recent notifications
  - Mark as read / clear all

- **Notification Preferences**
  - Toggle email notifications on/off
  - Choose notification frequency (immediate, daily digest, weekly digest)
  - Filter by notification type (offers, acceptances, reminders)

**Implementation:**
- New table: `GameSwapNotifications` (userId, type, message, isRead, createdUtc)
- New endpoint: `GET /notifications` (paginated list of user's notifications)
- New endpoint: `PATCH /notifications/{id}/read`
- New endpoint: `POST /notifications/preferences` (save user's email preferences)
- Email service: Azure Communication Services or SendGrid integration
- New component: `src/components/NotificationBell.jsx`
- New page: `src/pages/NotificationSettings.jsx`

### D. Calendar Subscription for Coaches
**Goal:** Sync team schedule to personal calendar apps.

**Features:**
- **Personal iCal Feed**
  - Unique URL per coach: `/calendar/ics?userId={userId}&token={secret}`
  - Feed includes only coach's team games (confirmed slots)
  - Auto-updates when schedule changes

- **Subscribe Button**
  - "Add to Google Calendar" button generates subscription link
  - "Add to Apple Calendar" button
  - "Copy iCal URL" for other calendar apps

- **Feed Customization**
  - Include only confirmed games vs. all offered/requested slots
  - Include team practices/events
  - Include division-wide events

**Implementation:**
- Update existing `GET /calendar/ics` endpoint to support userId filtering and auth tokens
- New endpoint: `POST /calendar/token/generate` (creates secure subscription token)
- New table: `GameSwapCalendarTokens` (userId, token, createdUtc, expiresUtc)
- Add "Subscribe to Calendar" section to coach dashboard
- New component: `src/components/CalendarSubscribeModal.jsx`

### E. Mobile-First Slot Browser
**Goal:** Make it easy for coaches to browse and accept slots on mobile devices.

**Features:**
- **Filter Chips**
  - Quick filters at top: "This Week", "Weekends", "My Preferred Times", "All"
  - Horizontally scrollable chip row (mobile-friendly)

- **Card-Based Layout**
  - Each slot displayed as a card with essential info
  - Large "Accept" button (44px min height)
  - Swipe actions: Swipe right to accept, swipe left to dismiss

- **Map View (Future Enhancement)**
  - Show slots on a map with field locations
  - Filter by proximity to coach's location

**Implementation:**
- Update `src/pages/CalendarPage.jsx` with mobile-optimized card view
- Add filter chip component: `src/components/FilterChips.jsx`
- Add swipe gesture detection for mobile users
- Use `@use-gesture/react` library for swipe gestures

---

## 2. League Admin Experience Improvements

### A. League Admin Dashboard
**Goal:** Provide at-a-glance health metrics and quick actions for league admins.

**Features:**
- **League Health Metrics**
  - Pending access requests: 5 (with CTA: Review Requests)
  - Unassigned coaches: 3 (with CTA: Assign Teams)
  - Schedule coverage: 80% of slots assigned (with CTA: Generate Schedule)
  - Upcoming games: 12 this week

- **Quick Actions Panel**
  - "Create Division" button
  - "Import Teams CSV" button
  - "Generate Schedule" button
  - "Manage Fields" button

- **Recent Activity Feed**
  - "Coach John added game slot for Tigers vs Eagles on 4/10"
  - "Schedule generated for 10U division (20 games)"
  - "3 new access requests received"

**Implementation:**
- New route: `/admin/dashboard`
- Make `/admin` redirect to dashboard (move current admin sections to sub-routes)
- New endpoint: `GET /admin/dashboard/metrics` (aggregates metrics)
- New component: `src/pages/admin/AdminDashboard.jsx`
- New components: `MetricCard.jsx`, `ActivityFeed.jsx`, `QuickActionsPanel.jsx`

### B. Reorganize Admin Page
**Goal:** Separate league admin functions from global admin functions.

**Current Structure (AdminPage.jsx):**
```
AdminPage
├── Access Requests
├── Coach Assignments
├── CSV Import (Slots, Teams)
└── Global Admin Section (conditional)
    ├── League Management
    ├── Season Settings
    ├── User Admin
    └── Memberships (All Leagues)
```

**Proposed Structure:**

**League Admin Routes:**
```
/admin                    → Admin Dashboard
/admin/access-requests    → Access Requests Section
/admin/coaches            → Coach Assignments Section
/admin/import             → CSV Import Section
/admin/divisions          → Division Management
/admin/fields             → Field Management
/admin/schedule           → Schedule Generation
/admin/settings           → League Settings
```

**Global Admin Routes (separate):**
```
/global-admin             → Global Admin Dashboard
/global-admin/leagues     → League Management
/global-admin/users       → User Management
/global-admin/memberships → All Memberships
/global-admin/settings    → Global Settings
```

**Implementation:**
- Create new route structure in `src/App.jsx`
- Split `AdminPage.jsx` into separate route pages
- Create `src/pages/GlobalAdminPage.jsx` with sub-routes
- Update TopNav to conditionally show "Admin" vs "Global Admin" link based on role
- Add breadcrumb navigation within admin sections

### C. Workflow Improvements

#### Streamlined Access Request Approval
**Current:** Select status filter → view list → click approve on each individually

**Improved:**
- Checkbox selection (select multiple requests)
- Bulk actions: "Approve Selected", "Deny Selected"
- Quick assign: Approve + assign team in one action (dropdown with team list)
- Keyboard shortcuts: "a" to approve focused item, "d" to deny

#### Integrated Team & Coach Assignment
**Current:** Create team → separately assign coach in coach assignments section

**Improved:**
- Team creation modal includes optional "Assign Coach" field
- Autocomplete coach email/name from memberships
- If coach not yet a member, show "Invite Coach" button inline
- After creating team, show success toast with "Assign Coach Now" action

#### Smart CSV Templates
**Current:** Download blank template → fill manually → upload

**Improved:**
- "Export Current Data" button generates CSV prefilled with existing teams/fields
- "Duplicate Last Upload" button pre-fills form with last CSV filename and preview
- Inline CSV editor: Edit CSV data in a spreadsheet-like table before uploading
- Error recovery: If upload has errors, show editable table with errors highlighted

**Implementation:**
- Add checkbox column to access requests table
- Add bulk action toolbar when items selected
- Update team creation modal with coach assignment field
- Create inline CSV editor component: `src/components/CsvEditor.jsx`
- Use `react-datasheet-grid` or similar library for spreadsheet UI

### D. Schedule Generation Wizard Improvements
**Goal:** Reduce preview/adjust cycles in schedule generation.

**Features:**
- **Real-Time Validation**
  - Show validation warnings as constraints are adjusted
  - Highlight conflicting constraints before preview
  - Suggest constraint adjustments based on available slots

- **Saved Presets**
  - Save successful constraint configurations as "presets"
  - Quick apply: "Use Spring 2025 Settings", "Use Fall Tournament Settings"
  - League-wide presets vs division-specific presets

- **Visual Schedule Preview**
  - Calendar grid view showing all scheduled games
  - Color-coded by team
  - Drag-and-drop to manually adjust assignments
  - Highlight issues (double-headers, unbalanced home/away)

**Implementation:**
- Update `src/manage/SchedulerManager.jsx` with real-time validation
- Add constraint presets: localStorage `schedule_presets_${leagueId}`
- Add calendar grid view: `src/manage/scheduler/CalendarGrid.jsx`
- Use `react-big-calendar` for visual schedule display
- Add drag-and-drop: `@dnd-kit/core` library

---

## 3. Global Admin Experience Improvements

### A. Global Admin Dashboard
**Goal:** Centralized view of all leagues and system-wide metrics.

**Features:**
- **System Overview**
  - Total leagues: 12
  - Total users: 450
  - Total games scheduled: 1,200
  - Active coaches: 85

- **League List (Paginated)**
  - Card for each league showing:
    - League name, ID, timezone
    - Member count, division count, upcoming games
    - "Manage League" button (opens league context)
  - Search/filter leagues
  - Sort by: name, member count, activity

- **Quick Actions**
  - "Create League" button
  - "Manage Global Admins" button
  - "View System Health" button (storage, API status)

**Implementation:**
- New route: `/global-admin` (replace current admin page for global admins)
- New component: `src/pages/GlobalAdminDashboard.jsx`
- New endpoint: `GET /global-admin/metrics` (system-wide stats)
- New component: `src/components/LeagueCard.jsx` (displays league summary)

### B. League Context Switcher
**Goal:** Allow global admins to quickly switch between league contexts.

**Features:**
- **Dropdown in TopNav**
  - Shows current league context (if any)
  - Dropdown lists all leagues
  - Click to switch context (sets `x-league-id` header for subsequent requests)
  - "View All Leagues" option returns to global admin dashboard

- **Persistent Context**
  - Remember last-selected league in localStorage
  - Breadcrumb shows: "Global Admin > Arlington League > Divisions"

**Implementation:**
- Update `TopNav.jsx` with league context switcher
- Add `useLeagueContext` hook to manage league selection
- Update `apiFetch` to use context league ID when global admin is acting in league scope
- Add breadcrumb component: `src/components/Breadcrumb.jsx`

### C. Consolidated User & Membership Management
**Goal:** Single interface for managing users across all leagues.

**Current:** User admin section and memberships section separated

**Improved:**
- **Unified User Table**
  - Columns: User ID, Email, Home League, Memberships (count), Last Active
  - Expandable rows showing all memberships for user
  - Inline edit: Click to change home league, role
  - Bulk actions: "Remove from League", "Change Role"

- **Advanced Filters**
  - Filter by league, role, last active date
  - Search by user ID or email
  - "Show only unassigned coaches" toggle

**Implementation:**
- New route: `/global-admin/users` (replaces separate user and membership sections)
- New component: `src/pages/global-admin/UserManagement.jsx`
- Use expandable table rows: `@tanstack/react-table` library
- Implement inline editing with optimistic updates

---

## 4. Cross-Cutting Improvements

### A. Reduce Repetitive Data Entry

#### Smart Form Defaults
- **Context-Aware Pre-Population**
  - Creating a slot? Pre-fill division and team based on current user
  - Creating a team? Pre-fill division based on last-created team
  - Importing CSV? Pre-fill division based on current page context

- **Form State Persistence**
  - Save form drafts to localStorage
  - "Resume Draft" button when returning to form
  - Clear draft after successful submission

#### Inline Editing
- **Click to Edit Tables**
  - Coach assignments table: Click team cell to change assignment
  - Divisions table: Click name to rename
  - Fields table: Click address to update

- **Optimistic Updates**
  - Update UI immediately, revert on API error
  - Show subtle loading indicator during save

#### Duplicate & Modify Patterns
- **Duplicate Button on All Entities**
  - Teams: "Duplicate Team" (copies team with "-Copy" suffix, same division)
  - Slots: "Duplicate Slot" (copies all fields, increment date by 1 week)
  - Divisions: "Duplicate Division" (copies templates and settings)

**Implementation:**
- Create `useFormPersistence` hook (saves to localStorage on change)
- Add inline editing component: `src/components/InlineEdit.jsx`
- Add optimistic update logic to API layer
- Add "Duplicate" action to all entity tables

### B. Email Notification System

#### Notification Types

**For Coaches:**
- New slot offered in your division
- Your slot offer was accepted
- Your slot request was approved/denied
- Game reminder (24 hours before)
- Schedule change (game time/field updated)
- Team assignment changed

**For League Admins:**
- New access request submitted
- Schedule generation completed (with summary)
- CSV import completed (with errors/warnings)
- Upcoming deadline reminder (e.g., "Season starts in 1 week")

**For Global Admins:**
- New league created
- League deleted
- New global admin added

#### Email Templates
Create reusable email templates with:
- Subject line
- Plain text and HTML body
- Personalization tokens ({{userName}}, {{teamName}}, etc.)
- Call-to-action button linking back to app

#### Unsubscribe & Preferences
- Every email includes "Manage Preferences" link
- Unsubscribe page: `/notifications/preferences`
- Toggle notifications by type
- Choose frequency: immediate, daily digest, weekly digest
- Option to unsubscribe from all emails

**Implementation:**
- Email service: Azure Communication Services (already available in Azure ecosystem)
  - Alternatively: SendGrid, Mailgun, or AWS SES
- New table: `GameSwapNotificationPreferences` (userId, emailNotifications enabled, digestFrequency, typePreferences JSON)
- New table: `GameSwapEmailQueue` (emailId, toEmail, subject, body, status, createdUtc, sentUtc)
- Background job (Azure Function Timer Trigger): Process email queue every minute
- Email template engine: Handlebars or similar
- Store templates in `api/EmailTemplates/` directory
- New endpoints:
  - `GET /notifications/preferences`
  - `POST /notifications/preferences`
  - `POST /notifications/unsubscribe`

### C. Calendar Integration

#### iCal Feed Enhancements
**Current:** Basic ICS feed with all slots

**Improved:**
- **Personalized Feeds**
  - Coach feed: Only my team's games
  - Team feed: Specific team's games (shareable with parents)
  - Division feed: All games in a division
  - League feed: All games across league

- **Secure Token-Based Access**
  - Generate unique subscription URL per user/team/division
  - Revoke and regenerate tokens
  - Tokens expire after 1 year (with renewal prompt)

- **Rich Calendar Events**
  - Event title: "Tigers vs Eagles (10U)"
  - Location: Field address with Google Maps link
  - Description: Game notes, coach contact info
  - Alarms: Reminder 24h and 1h before game

#### Subscribe UI
- **Easy Subscribe Flow**
  - "Subscribe to Calendar" button on dashboard
  - Modal shows options: "My Team", "My Division", "Entire League"
  - Copy URL or click platform-specific buttons:
    - "Add to Google Calendar" (opens Google Calendar with webcal:// URL)
    - "Add to Apple Calendar" (opens Calendar.app on iOS/macOS)
    - "Add to Outlook" (opens Outlook)

**Implementation:**
- Update `GET /calendar/ics` to support filtering by userId, teamId, division
- Add token-based authentication: `?token={secureToken}`
- New endpoint: `POST /calendar/token` (generates subscription token)
- New endpoint: `DELETE /calendar/token` (revokes token)
- New table: `GameSwapCalendarTokens` (tokenId, userId, entityType, entityId, token, expiresUtc)
- Enhance ICS generation to include rich event properties (GEO coordinates, ALARM triggers)
- New component: `src/components/CalendarSubscribe.jsx`

---

## 5. Implementation Phases

### Phase 1: Core Improvements (2-3 weeks)
**Goal:** Address immediate pain points and reorganize admin experience.

**Deliverables:**
1. ✅ Split AdminPage into route-based structure (`/admin/*`)
2. ✅ Create GlobalAdminPage with separate routes (`/global-admin/*`)
3. ✅ Build CoachDashboard with action items and quick actions
4. ✅ Add smart defaults to slot offer form (remember preferences)
5. ✅ Implement bulk approval for access requests
6. ✅ Add inline editing for coach assignments

**Success Metrics:**
- Admin page load time < 2 seconds
- Coaches can create slot in < 30 seconds (vs 1 minute currently)
- League admins can approve 10 access requests in < 1 minute (vs 3 minutes)

### Phase 2: Notification System (2 weeks)
**Goal:** Keep users informed without requiring constant app checks.

**Deliverables:**
1. Set up email service integration (Azure Communication Services)
2. Create email templates for all notification types
3. Build notification preferences page
4. Implement email queue processing (Azure Function Timer)
5. Add in-app notification bell with dropdown
6. Create notification settings page

**Success Metrics:**
- Email notifications sent within 5 minutes of triggering event
- 80%+ of coaches enable email notifications
- Email open rate > 40%

### Phase 3: Calendar Integration (1 week)
**Goal:** Enable personal calendar subscriptions.

**Deliverables:**
1. Enhance ICS feed with rich event properties
2. Implement token-based authentication for feeds
3. Create calendar subscription modal
4. Add "Subscribe to Calendar" section to coach dashboard
5. Generate platform-specific subscription links

**Success Metrics:**
- 50%+ of coaches subscribe to calendar feed
- Feeds update within 15 minutes of schedule changes
- Zero security incidents related to calendar tokens

### Phase 4: Advanced Features (2-3 weeks)
**Goal:** Reduce repetitive work and add power-user features.

**Deliverables:**
1. Implement form state persistence (resume drafts)
2. Add inline CSV editor for imports
3. Build visual schedule calendar with drag-and-drop
4. Add saved constraint presets for schedule generation
5. Implement duplicate & modify for all entities
6. Add league context switcher for global admins

**Success Metrics:**
- Schedule generation time reduced by 50%
- CSV import error rate reduced by 30%
- Global admins can switch between leagues in < 2 seconds

### Phase 5: Mobile Optimization (1-2 weeks)
**Goal:** Make key workflows mobile-friendly.

**Deliverables:**
1. Mobile-optimized slot browser with card layout
2. Swipe gestures for accept/dismiss slots
3. Filter chips for quick filtering
4. Touch-optimized forms (large inputs, bottom sheets)
5. Test on iOS and Android devices

**Success Metrics:**
- Mobile usage increases by 30%
- Mobile task completion rate matches desktop
- Touch target size compliance: 100% of interactive elements ≥ 44px

---

## 6. Technical Architecture

### Frontend Architecture Changes

**New Directory Structure:**
```
src/
├── pages/
│   ├── CoachDashboard.jsx              (new)
│   ├── admin/
│   │   ├── AdminDashboard.jsx          (new)
│   │   ├── AccessRequestsPage.jsx      (renamed from Section)
│   │   ├── CoachAssignmentsPage.jsx    (renamed from Section)
│   │   ├── CsvImportPage.jsx           (renamed from Section)
│   │   ├── DivisionsPage.jsx           (new)
│   │   ├── FieldsPage.jsx              (new)
│   │   ├── SchedulePage.jsx            (new)
│   │   └── SettingsPage.jsx            (new)
│   └── global-admin/
│       ├── GlobalAdminDashboard.jsx    (new)
│       ├── LeagueManagement.jsx        (new)
│       ├── UserManagement.jsx          (new)
│       └── SystemSettings.jsx          (new)
├── components/
│   ├── notifications/
│   │   ├── NotificationBell.jsx        (new)
│   │   ├── NotificationDropdown.jsx    (new)
│   │   └── NotificationSettings.jsx    (new)
│   ├── calendar/
│   │   ├── CalendarSubscribe.jsx       (new)
│   │   ├── CalendarGrid.jsx            (new)
│   │   └── EventCard.jsx               (new)
│   ├── forms/
│   │   ├── InlineEdit.jsx              (new)
│   │   ├── CsvEditor.jsx               (new)
│   │   └── SmartSlotForm.jsx           (new)
│   └── admin/
│       ├── MetricCard.jsx              (new)
│       ├── ActivityFeed.jsx            (new)
│       ├── QuickActionsPanel.jsx       (new)
│       └── BulkActionToolbar.jsx       (new)
└── lib/
    ├── hooks/
    │   ├── useNotifications.js         (new)
    │   ├── useLeagueContext.js         (new)
    │   └── useFormPersistence.js       (new)
    └── api/
        └── notifications.js            (new)
```

**New Routes:**
```javascript
// src/App.jsx
<Routes>
  {/* Coach routes */}
  <Route path="/" element={<CoachDashboard />} />
  <Route path="/coach" element={<CoachDashboard />} />

  {/* League admin routes */}
  <Route path="/admin" element={<AdminDashboard />} />
  <Route path="/admin/access-requests" element={<AccessRequestsPage />} />
  <Route path="/admin/coaches" element={<CoachAssignmentsPage />} />
  <Route path="/admin/import" element={<CsvImportPage />} />
  <Route path="/admin/divisions" element={<DivisionsPage />} />
  <Route path="/admin/fields" element={<FieldsPage />} />
  <Route path="/admin/schedule" element={<SchedulePage />} />
  <Route path="/admin/settings" element={<SettingsPage />} />

  {/* Global admin routes */}
  <Route path="/global-admin" element={<GlobalAdminDashboard />} />
  <Route path="/global-admin/leagues" element={<LeagueManagement />} />
  <Route path="/global-admin/users" element={<UserManagement />} />
  <Route path="/global-admin/settings" element={<SystemSettings />} />

  {/* Existing routes */}
  <Route path="/calendar" element={<CalendarPage />} />
  <Route path="/manager" element={<ManagerPage />} />
  {/* ... */}
</Routes>
```

### Backend Architecture Changes

**New Services:**
```
api/Services/
├── INotificationService.cs         (new)
├── NotificationService.cs          (new)
├── IEmailService.cs                (new)
├── EmailService.cs                 (new)
├── IDashboardService.cs            (new)
└── DashboardService.cs             (new)
```

**New Functions:**
```
api/Functions/
├── NotificationsFunctions.cs       (new)
├── EmailPreferencesFunctions.cs    (new)
├── DashboardFunctions.cs           (new)
├── CalendarTokenFunctions.cs       (new)
└── EmailQueueProcessor.cs          (new - Timer Trigger)
```

**New Tables:**
```
- GameSwapNotifications (userId PK, notificationId RK)
- GameSwapNotificationPreferences (userId PK, "PREFS" RK)
- GameSwapEmailQueue (queueId PK, emailId RK)
- GameSwapCalendarTokens (userId PK, tokenId RK)
- GameSwapFormDrafts (userId PK, formKey RK)
```

**Email Templates:**
```
api/EmailTemplates/
├── new-slot-offer.hbs
├── slot-accepted.hbs
├── slot-request-approved.hbs
├── game-reminder.hbs
├── schedule-change.hbs
├── access-request-submitted.hbs
└── _layout.hbs (base template)
```

---

## 7. Success Metrics

### User Engagement
- **Coaches:** 80%+ weekly active users (vs 60% currently)
- **League Admins:** Average session time < 15 minutes (vs 30 minutes currently)
- **Mobile Usage:** 40%+ of traffic from mobile devices (vs 20% currently)

### Task Efficiency
- **Create Slot:** < 30 seconds (vs 1 minute)
- **Approve Access Requests:** < 1 minute for 10 requests (vs 3 minutes)
- **Generate Schedule:** < 5 minutes including adjustments (vs 15 minutes)
- **CSV Import:** < 2 minutes including error correction (vs 5 minutes)

### Feature Adoption
- **Email Notifications:** 80%+ of users enable at least one notification type
- **Calendar Subscriptions:** 50%+ of coaches subscribe within first 2 weeks
- **Smart Defaults:** 90%+ of slot offers use pre-filled fields
- **Bulk Actions:** 70%+ of access request approvals use bulk action

### Quality Metrics
- **Email Deliverability:** > 99% of emails delivered successfully
- **Email Open Rate:** > 40% for transactional emails
- **Calendar Feed Uptime:** 99.9% availability
- **Mobile Performance:** Lighthouse score > 90 on mobile

### User Satisfaction
- **NPS Score:** Increase from 7 to 8+ within 3 months
- **Support Tickets:** Reduce by 40% related to repetitive tasks
- **User Feedback:** 85%+ positive feedback on new features

---

## 8. Risks & Mitigations

### Risk: Email Deliverability Issues
**Impact:** High - Users won't receive critical notifications
**Mitigation:**
- Use reputable email service (Azure Communication Services)
- Implement proper SPF, DKIM, DMARC records
- Monitor bounce rates and blacklist status
- Provide alternative in-app notifications

### Risk: Calendar Feed Performance
**Impact:** Medium - Slow feed updates frustrate users
**Mitigation:**
- Cache ICS files for 15 minutes (CDN or Blob Storage)
- Implement efficient queries with proper indexes
- Set reasonable cache headers for calendar clients
- Monitor feed generation time and optimize

### Risk: Notification Fatigue
**Impact:** Medium - Users unsubscribe from all emails
**Mitigation:**
- Sensible defaults (daily digest for non-urgent)
- Clear notification preferences UI
- Respect unsubscribe immediately
- A/B test notification frequency

### Risk: Complex Route Refactoring
**Impact:** Medium - Breaking existing bookmarks and workflows
**Mitigation:**
- Implement redirects from old routes to new routes
- Gradual migration with feature flags
- Thorough testing of all navigation paths
- Update documentation and user guides

### Risk: Mobile Performance
**Impact:** Medium - Slow mobile experience
**Mitigation:**
- Code-split routes to reduce initial bundle size
- Lazy-load heavy components (calendar grid, CSV editor)
- Optimize images and assets
- Use React.memo and useMemo for expensive renders

---

## 9. Open Questions

1. **Email Service Selection:**
   - Azure Communication Services (native to Azure) vs SendGrid (more mature)?
   - Budget for email sending (# of emails per month)?

2. **Calendar Token Security:**
   - How long should tokens be valid? (Recommend: 1 year with renewal)
   - Should users be able to revoke tokens and regenerate?

3. **Notification Preferences:**
   - Should digest emails be opt-in or opt-out?
   - What's the default notification setting for new users?

4. **Mobile App Future:**
   - Is a native mobile app planned? If yes, should we build API endpoints with that in mind?
   - Should we prioritize PWA features (offline support, install prompt)?

5. **Bulk Operations:**
   - What's the maximum number of items for bulk actions? (e.g., approve 100 access requests at once)
   - Should we implement background jobs for large bulk operations?

6. **Global Admin UX:**
   - Should global admins always operate in a league context, or have league-agnostic views?
   - How should navigation work when switching between leagues?

---

## 10. Next Steps

1. **Review & Approval:** Share this plan with stakeholders for feedback
2. **Prioritization:** Confirm phase order and adjust timeline based on priorities
3. **Technical Spike:** Prototype email service integration and calendar token auth
4. **Design Mockups:** Create high-fidelity designs for coach dashboard and admin reorganization
5. **Begin Phase 1:** Start with admin route refactoring and coach dashboard

---

## Appendix A: Detailed Wireframes

(To be added: Wireframes for coach dashboard, admin dashboard, notification bell, calendar subscribe modal, etc.)

## Appendix B: API Endpoint Additions

### New Endpoints for Notifications
```
GET    /notifications                      # List user's notifications (paginated)
POST   /notifications/{id}/read            # Mark notification as read
POST   /notifications/read-all             # Mark all as read
GET    /notifications/preferences          # Get user's email preferences
POST   /notifications/preferences          # Update email preferences
POST   /notifications/unsubscribe          # Unsubscribe from all emails
```

### New Endpoints for Dashboard
```
GET    /coach/dashboard                    # Coach dashboard metrics
GET    /admin/dashboard/metrics            # League admin dashboard metrics
GET    /global-admin/dashboard/metrics     # Global admin system metrics
GET    /admin/activity-feed                # Recent activity log
```

### New Endpoints for Calendar
```
POST   /calendar/token                     # Generate calendar subscription token
GET    /calendar/token                     # Get user's active tokens
DELETE /calendar/token/{tokenId}           # Revoke token
GET    /calendar/ics                       # Enhanced with token auth and filters
```

### New Endpoints for Form Persistence
```
GET    /drafts/{formKey}                   # Get saved draft
POST   /drafts/{formKey}                   # Save draft
DELETE /drafts/{formKey}                   # Clear draft
```

## Appendix C: Email Template Examples

### New Slot Offer Email
**Subject:** New game opportunity in {{division}}: {{gameDate}} at {{field}}

**Body:**
```
Hi {{coachName}},

A new game slot is available in your division!

📅 {{gameDate}} at {{startTime}} - {{endTime}}
🏟️ {{fieldName}}
🎯 Division: {{division}}

This is a great opportunity to schedule a game for your team.

[View Slot Details] [Accept This Slot]

To manage your notification preferences, visit:
{{appUrl}}/notifications/preferences

Thanks,
Sports Scheduler Team
```

### Game Reminder Email
**Subject:** Reminder: Game tomorrow at {{startTime}}

**Body:**
```
Hi {{coachName}},

This is a reminder that your team has a game tomorrow:

🏆 {{homeTeam}} vs {{awayTeam}}
📅 {{gameDate}} at {{startTime}}
🏟️ {{fieldName}} - {{address}}

[View Game Details] [Get Directions]

See you on the field!

Sports Scheduler Team
```

---

**End of Document**
