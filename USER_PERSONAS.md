# User Personas - Research Input for Feature Development

> Status: persona reference only, not the canonical shipped contract.
>
> These personas are useful for evaluating desirability and future prioritization, but they do not override the current workflow or API contract.
>
> Current shipped scope is an authenticated league-member tool. Parent/public calendar access described here is future-state exploration, not current product scope.
>
> For canonical current-state behavior, use `docs/contract.md`, `docs/SLOT_LIFECYCLE_BEHAVIORAL_CONTRACT.md`, `README.md`, and `AGENTS.md`.

These personas are reference inputs for evaluating features and checking alignment with user needs. Each iteration can use them to test desirability and fit, but they are not the source of truth for shipped behavior.

Created: 2026-03-03
Based on: Codebase analysis, user feedback, Little League requirements

---

## 🎯 **PERSONA 1: Sarah - League Commissioner**

### **Profile**
- **Role:** LeagueAdmin
- **Background:** 45-year-old volunteer, manages 8-team Little League division
- **Experience:** 3rd year as commissioner, non-technical
- **Time:** 5-10 hours/week during season setup, 2-3 hours/week during season
- **Context:** Manages Alexandria Little League 10U division, 60+ players across 8 teams

### **Goals & Motivations**
1. **Primary:** Create fair, balanced schedules that meet Little League minimum game requirements
2. **Secondary:** Minimize conflicts (doubleheaders, field conflicts, parent complaints)
3. **Tertiary:** Save time vs manual scheduling (previous method took 40+ hours)

### **Key Use Cases**

**UC1.1: Season Setup (February-March)**
- **Frequency:** Once per season (Spring & Fall)
- **Workflow:**
  1. Access Commissioner Hub → Season Wizard
  2. Select division, set season dates (Mar 15 - Jun 6 by default)
  3. Define postseason dates (pool play, bracket)
  4. Configure constraints:
     - Min 13 games per team (Little League requirement: 12 + buffer)
     - Max 2 games per week
     - No doubleheaders
     - Guest games for odd team counts
  5. Block Spring Break and holidays (Memorial Day, July 4th)
  6. Click "Generate 4 Options"
  7. Compare schedules, pick best (balanced, minimal conflicts)
  8. Review preview, check for conflicts
  9. Apply schedule
  10. Export to SportsEngine for publishing

**UC1.2: Field Availability Management**
- **Frequency:** Monthly updates
- **Workflow:**
  1. Go to Manage → Fields
  2. Import field CSV or add manually
  3. Create availability rules (which fields, which days, what times)
  4. Add exceptions for tournaments/holidays
  5. Generate slot pool for wizard

**UC1.3: Schedule Adjustment (Mid-Season)**
- **Frequency:** 2-3 times per season
- **Workflow:**
  1. View Calendar page
  2. Identify conflict (weather cancellation, field closure)
  3. Drag game to different slot (drag-swap preview)
  4. Verify no rule violations
  5. Apply change
  6. Coaches auto-notified

**UC1.4: Coach Management**
- **Frequency:** Once at season start, occasional updates
- **Workflow:**
  1. Admin page → Access Requests
  2. Review pending coach requests
  3. Approve with team assignment
  4. Coach can now create slots and manage team

### **Requirements**

**Must Have:**
- ✅ Wizard defaults to 13 games (Little League requirement met)
- ✅ Season dates pre-filled (Mar 15 - Jun 6)
- ✅ Generate 4 schedule options for comparison
- ✅ AI recommendation highlights best schedule
- ✅ Guest games avoid week 1 & bracket weeks
- ✅ Guest games evenly distributed across teams
- ✅ Holiday auto-blackout (6 common holidays)
- ✅ Slot plan templates (reuse configurations)
- ✅ Overwrite warning before applying schedule
- ✅ Export to CSV for SportsEngine

**Should Have:**
- ⏸️ Request game feature for tournaments (90% complete)
- ⏸️ Calendar view in wizard preview (optional)
- ⏳ Schedule feedback capture for ML improvement
- ⏳ Capacity warnings when slot availability tight

**Nice to Have:**
- ⏳ "What-if" capacity calculator
- ⏳ Historical schedule analysis
- ⏳ Auto-detect holidays from calendar API

### **Success Criteria**
1. **Time:** Schedule generation < 30 minutes (vs 40+ hours manual)
2. **Quality:** 0 hard rule violations, minimal soft violations
3. **Balance:** Team game spread ≤ 1 (e.g., all teams 12-13 games)
4. **Guest Games:** All teams get 1-2 guest games (for odd team counts)
5. **Compliance:** Meets Little League 12-game minimum
6. **Parent Satisfaction:** <5% complaints about schedule conflicts

### **Pain Points Addressed**
- ✅ **Manual scheduling tedium** → Generate 4 Options automates workflow
- ✅ **Uncertainty about "best" schedule** → AI recommendation with quality scores
- ✅ **Guest game placement errors** → Automatic exclusion of week 1 & bracket
- ✅ **Repetitive season setup** → Templates and defaults save time
- ✅ **Holiday conflicts** → Quick checkbox selection
- ✅ **Schedule overwrites** → Prominent warnings prevent accidents

### **Contract Evaluation Questions**
- [ ] Can Sarah set up a season in < 30 minutes?
- [ ] Does the recommended schedule meet Little League requirements?
- [ ] Are guest games properly distributed and scheduled?
- [ ] Can Sarah export schedule to SportsEngine without manual reformatting?
- [ ] Does the wizard prevent common mistakes (week 1 guest games, missing holidays)?

---

## 🏃 **PERSONA 2: Mike - Team Coach**

### **Profile**
- **Role:** Coach
- **Background:** 38-year-old parent volunteer, coaches daughter's team
- **Experience:** 1st year coaching, learning the ropes
- **Time:** 10-15 hours/week (practices, games, coordination)
- **Context:** Coaches "Rockets" in 10U division, 12 players on roster

### **Goals & Motivations**
1. **Primary:** Ensure team has enough games to meet Little League requirements
2. **Secondary:** Coordinate game swaps when schedule conflicts arise
3. **Tertiary:** Manage practice times and field allocations

### **Key Use Cases**

**UC2.1: Initial Team Setup (Onboarding)**
- **Frequency:** Once at season start
- **Workflow:**
  1. Receive coach onboarding link from commissioner
  2. Access Coach Onboarding Page
  3. Enter team details (name, assistant coaches, contact info)
  4. Request 1-3 practice slots (preferred days/times)
  5. Review generated game schedule
  6. Complete onboarding checklist

**UC2.2: Browse & Request Game Slots**
- **Frequency:** Weekly during season
- **Workflow:**
  1. Go to Offers page
  2. Filter by division, date range
  3. See open slots from other teams
  4. Click "Request" on available slot
  5. Enter message (optional)
  6. Wait for offering team's approval
  7. Receive notification when approved/denied

**UC2.3: Manage Own Team's Slots**
- **Frequency:** 2-3 times per season
- **Workflow:**
  1. Go to Calendar page
  2. See own team's confirmed and offered slots
  3. If schedule conflict arises:
     - Cancel slot (notifies requesting team if pending)
     - Or approve pending request to fill slot
  4. Coaches receive notifications

**UC2.4: Approve/Deny Incoming Requests**
- **Frequency:** 1-2 times per week during busy periods
- **Workflow:**
  1. Receive notification: "Tigers requested your slot on Apr 15"
  2. Go to Offers page or Calendar
  3. Review request details
  4. Approve → Slot marked Confirmed
  5. Deny → Request rejected, slot remains Open

**UC2.5: View Team Schedule**
- **Frequency:** Daily
- **Workflow:**
  1. Go to Calendar page
  2. Filter to own team
  3. See upcoming games, practices
  4. Export to personal calendar (iCal subscription)
  5. Share with parents

### **Requirements**

**Must Have:**
- ✅ Team assignment required before actions (enforced)
- ✅ Can only modify own team's slots (validated)
- ✅ Clear indication of slot ownership (own vs others')
- ✅ Request/approval workflow with notifications
- ✅ Calendar view for schedule visualization
- ✅ iCal export for parent sharing

**Should Have:**
- ✅ Practice request portal (exists)
- ⏳ Batch approve multiple requests
- ⏳ Request templates (recurring needs)
- ⏳ Conflict warnings when creating slots

**Nice to Have:**
- ⏳ Auto-suggest open slots based on team availability
- ⏳ Team availability preferences
- ⏳ Automated reminders for upcoming games

### **Success Criteria**
1. **Onboarding:** Complete team setup in < 15 minutes
2. **Requests:** Find and request slot in < 2 minutes
3. **Approval:** Approve/deny request in < 1 minute
4. **Visibility:** See all team games in one view
5. **Communication:** Parents get schedule updates automatically

### **Pain Points Addressed**
- ✅ **Complex onboarding** → Wizard with checklist
- ✅ **Manual game coordination** → Request/approve workflow
- ✅ **Schedule confusion** → Clear calendar with filters
- ⏳ **Parent communication** → iCal export (need better integration)

### **Contract Evaluation Questions**
- [ ] Can Mike request a game swap in < 2 minutes?
- [ ] Does Mike see only his team's slots vs others' clearly?
- [ ] Can Mike approve a request without leaving calendar?
- [ ] Are notifications timely when requests arrive?
- [ ] Can Mike export schedule in format parents understand?

---

## 🏆 **PERSONA 3: Jessica - Tournament Coordinator**

### **Profile**
- **Role:** LeagueAdmin (special focus)
- **Background:** 52-year-old league board member, coordinates tournaments
- **Experience:** 10+ years in youth sports, tech-savvy
- **Time:** 20+ hours during tournament weeks
- **Context:** Organizes 3-4 tournaments per season, coordinates with external teams

### **Goals & Motivations**
1. **Primary:** Schedule tournament games with external teams at neutral fields
2. **Secondary:** Track teams' away games at opponent fields
3. **Tertiary:** Ensure tournament games don't conflict with regular season

### **Key Use Cases**

**UC3.1: Schedule Tournament Games (Request Games Feature)**
- **Frequency:** 3-4 times per season
- **Workflow:**
  1. Receive tournament invite (external field, specific dates)
  2. Go to Season Wizard → Rules step
  3. Scroll to "Request Games (Away)" section
  4. Click "+ Add Request Game"
  5. Enter details:
     - Date: Tournament date
     - Time: Game start/end
     - Field: "external/tournament_field"
     - Team: Select which team plays
     - Opponent: External team name
  6. Add multiple tournament games (one per team)
  7. Generate preview
  8. Verify tournament games appear with "REQUEST" badge
  9. Verify tournament games don't create conflicts (max games/week respected)
  10. Apply schedule

**UC3.2: Track Cross-Division Games**
- **Frequency:** 2-3 times per season
- **Workflow:**
  1. 10U team plays 12U team (makeup game)
  2. Add as request game with opponent name
  3. Appears in both teams' calendars
  4. Counts toward team's game total
  5. Respects constraints (no doubleheaders)

**UC3.3: Verify Tournament Capacity**
- **Frequency:** Before each tournament
- **Workflow:**
  1. Check feasibility with tournament games included
  2. Verify max games/week not exceeded
  3. Ensure no doubleheaders on tournament days
  4. Confirm teams have proper rest before/after

### **Requirements**

**Must Have:**
- ✅ Request game feature (away games at external fields)
- ✅ Request game validation (date, time, team exists)
- ✅ Tournament games count toward team totals
- ✅ Tournament games respect constraints (max games/week)
- ✅ Visual distinction in preview ("REQUEST" badge)
- ✅ Opponent name displayed

**Should Have:**
- ⏳ Bulk import tournament games (CSV)
- ⏳ Tournament bracket generator
- ⏳ External opponent database
- ⏳ Field mapping (external fields)

**Nice to Have:**
- ⏳ Tournament results tracking
- ⏳ Automated bracket updates
- ⏳ Cross-league coordination

### **Success Criteria**
1. **Setup:** Add 8 tournament games in < 10 minutes
2. **Validation:** No conflicts with regular season
3. **Visibility:** Tournament games clearly identified
4. **Compliance:** Max games/week respected
5. **Export:** Tournament games included in calendar exports

### **Pain Points Addressed**
- ✅ **Manual tracking of external games** → Request game feature
- ✅ **Tournament game conflicts** → Validation and constraint checking
- ✅ **Visibility issues** → REQUEST badge in preview
- ⏳ **Bulk entry** → CSV import planned

### **Contract Evaluation Questions**
- [ ] Can Jessica add a 6-team tournament in < 10 minutes?
- [ ] Are tournament games properly excluded from feasibility calculations?
- [ ] Do tournament games respect max games/week constraints?
- [ ] Can Jessica identify tournament games in preview at a glance?
- [ ] Are tournament games included in CSV exports?

---

## 👨‍👩‍👧 **PERSONA 4: Robert & Lisa - Parent/Guardians**

### **Profile**
- **Role:** Viewer (or no account)
- **Background:** Parents of 9-year-old player, both work full-time
- **Experience:** No sports management experience
- **Time:** Just need to know when/where games are
- **Context:** Son plays on Tigers team, need schedule for family planning

### **Goals & Motivations**
1. **Primary:** Know when and where games are (dates, times, fields)
2. **Secondary:** Get notified of schedule changes
3. **Tertiary:** Access from mobile device while on-the-go

### **Key Use Cases**

**UC4.1: View Team Schedule**
- **Frequency:** 2-3 times per week
- **Workflow:**
  1. Access Calendar page (public link or viewer account)
  2. Filter to division or team
  3. See upcoming games with dates, times, fields
  4. Add to personal calendar (iCal subscription)
  5. Share with carpooling parents

**UC4.2: Subscribe to Schedule Updates**
- **Frequency:** Once per season
- **Workflow:**
  1. Calendar page → Click "Subscribe" button
  2. Copy iCal URL
  3. Add to Google Calendar / Apple Calendar / Outlook
  4. Auto-updates when schedule changes
  5. Family calendar stays in sync

**UC4.3: Check Game Details**
- **Frequency:** Before each game
- **Workflow:**
  1. Open calendar (mobile device)
  2. Click game event
  3. See: opponent, field name, address, time
  4. Navigate to field (tap address for maps)
  5. Arrive on time

**UC4.4: Handle Schedule Changes**
- **Frequency:** 2-3 times per season
- **Workflow:**
  1. Receive notification: "Game rescheduled"
  2. Check calendar (auto-updated via iCal)
  3. See new date/time
  4. Adjust family plans

### **Requirements**

**Must Have:**
- ✅ Public calendar view (no login required for viewing)
- ✅ iCal subscription (auto-updates)
- ✅ Mobile-responsive calendar
- ✅ Clear game details (date, time, field, opponent)
- ✅ Field addresses included
- ⏳ Notifications for schedule changes

**Should Have:**
- ✅ Week Cards view (compact, mobile-friendly)
- ✅ Agenda view (chronological list)
- ⏳ Push notifications
- ⏳ Direct link to field navigation (Google Maps)
- ⏳ Weather integration

**Nice to Have:**
- ⏳ Roster integration
- ⏳ Photo sharing
- ⏳ Score tracking
- ⏳ Team messaging

### **Success Criteria**
1. **Accessibility:** View schedule without login
2. **Sync:** iCal updates within 15 minutes
3. **Mobile:** Calendar readable on phone (320px width)
4. **Clarity:** Field address and directions clear
5. **Reliability:** <1% missed games due to schedule confusion

### **Pain Points Addressed**
- ✅ **Schedule access complexity** → Public calendar view
- ✅ **Manual calendar updates** → iCal auto-sync
- ✅ **Mobile usability** → Week Cards & Agenda views
- ⏳ **Late notifications** → Notification system exists, needs reliability

### **Contract Evaluation Questions**
- [ ] Can Robert view schedule without creating account?
- [ ] Does iCal subscription update automatically?
- [ ] Is calendar usable on mobile without horizontal scrolling?
- [ ] Are field addresses visible and copyable?
- [ ] Do schedule changes propagate to subscribed calendars quickly?

---

## 👔 **PERSONA 5: David - Multi-League Administrator**

### **Profile**
- **Role:** GlobalAdmin
- **Background:** 35-year-old youth sports organization director
- **Experience:** Manages 12 leagues across 3 states
- **Time:** Full-time role (40+ hours/week)
- **Context:** Oversees regional sports scheduling platform

### **Goals & Motivations**
1. **Primary:** Ensure all leagues can self-manage schedules efficiently
2. **Secondary:** Monitor system health and usage across leagues
3. **Tertiary:** Provide support and best practices to league admins

### **Key Use Cases**

**UC5.1: Create New League**
- **Frequency:** 4-6 times per year (new seasons/regions)
- **Workflow:**
  1. Admin page → Global Admin section
  2. Click "Create League"
  3. Enter league name, code, region
  4. Set league-level settings (season dates, game length)
  5. Import initial field list (CSV)
  6. Create divisions (10U, 12U, etc.)
  7. Invite first league admin
  8. League admin takes over from there

**UC5.2: Monitor System Usage**
- **Frequency:** Weekly
- **Workflow:**
  1. Access Debug page (Global Admin only)
  2. View:
     - Active leagues
     - Schedule generation metrics
     - Error rates
     - User feedback scores
  3. Identify struggling leagues
  4. Reach out with support

**UC5.3: Troubleshoot League Issues**
- **Frequency:** As needed (1-2 times per week)
- **Workflow:**
  1. League admin reports issue
  2. David accesses their league (auto-elevated to LeagueAdmin)
  3. Reproduce issue in their context
  4. Fix data or guide admin to solution
  5. Document in knowledge base

**UC5.4: Analyze Schedule Quality Across Leagues**
- **Frequency:** Monthly
- **Workflow:**
  1. Query schedule feedback data
  2. Analyze which leagues use "Generate 4 Options"
  3. Identify patterns in high-quality schedules
  4. Share best practices with league admins
  5. Improve algorithm based on feedback

### **Requirements**

**Must Have:**
- ✅ Global admin role with cross-league access
- ✅ League creation workflow
- ✅ Debug page with system metrics
- ✅ Feedback capture for quality analysis
- ⏳ Usage analytics dashboard

**Should Have:**
- ⏳ League health monitoring
- ⏳ Best practices documentation
- ⏳ Automated alerts for struggling leagues
- ⏳ Bulk operations (update all leagues)

**Nice to Have:**
- ⏳ League templates (copy working league to new region)
- ⏳ Performance benchmarking
- ⏳ A/B testing framework

### **Success Criteria**
1. **Scaling:** Support 50+ leagues on single platform
2. **Self-Service:** 90%+ of leagues never need support
3. **Quality:** Average schedule quality score >75/100
4. **Adoption:** 80%+ of leagues use "Generate 4 Options"
5. **Data:** Feedback captured on 90%+ of schedule generations

### **Pain Points Addressed**
- ✅ **No visibility into system usage** → Feedback capture
- ✅ **Can't identify best practices** → Quality metrics tracked
- ⏳ **Manual support for each league** → Self-service tools

### **Contract Evaluation Questions**
- [ ] Can David create a new league in < 15 minutes?
- [ ] Does feedback data show which schedules users prefer?
- [ ] Can David access any league to troubleshoot without permission request?
- [ ] Are system-wide metrics visible in debug page?
- [ ] Is feedback data structured for ML training?

---

## 🎓 **PERSONA 6: Emily - Assistant Coach / Team Parent Coordinator**

### **Profile**
- **Role:** Coach (delegated permissions)
- **Background:** 42-year-old parent, assists head coach
- **Experience:** 2nd year assisting, handles logistics
- **Time:** 5-8 hours/week (coordinate carpools, field setup, snacks)
- **Context:** Assistant coach for Rockets, manages parent communication

### **Goals & Motivations**
1. **Primary:** Coordinate logistics (carpools, field directions, snack schedule)
2. **Secondary:** Communicate schedule to parents quickly
3. **Tertiary:** Help head coach with game swap requests

### **Key Use Cases**

**UC6.1: Weekly Schedule Communication**
- **Frequency:** Weekly
- **Workflow:**
  1. View Calendar page (filtered to Rockets)
  2. See upcoming week's games
  3. Export Week Card view (screenshot or print)
  4. Email to parent group
  5. Include field addresses and times

**UC6.2: Carpool Coordination**
- **Frequency:** Before each game
- **Workflow:**
  1. Check calendar for game details
  2. See field location and time
  3. Create carpool schedule
  4. Share with parents
  5. Confirm attendance

**UC6.3: Field Setup Scheduling**
- **Frequency:** Before home games
- **Workflow:**
  1. Filter calendar to home games only
  2. Identify which parents arrive early
  3. Coordinate field setup (bases, chalk lines)
  4. Assign snack duty

**UC6.4: Monitor Practice Requests**
- **Frequency:** Weekly
- **Workflow:**
  1. Coach Onboarding page → Practice Requests
  2. See status of pending requests
  3. When approved, communicate to parents
  4. Add practice times to team calendar

### **Requirements**

**Must Have:**
- ✅ Same permissions as head coach (shared team assignment)
- ✅ Filter calendar to own team
- ✅ Export schedule (iCal, CSV, print-friendly)
- ✅ Field addresses visible
- ✅ Week Cards view (compact for sharing)

**Should Have:**
- ⏳ Home/away filter
- ⏳ Print-optimized view
- ⏳ Parent contact list integration
- ⏳ Automated parent notifications

**Nice to Have:**
- ⏳ Carpool scheduling feature
- ⏳ Snack schedule generator
- ⏳ Field setup checklist
- ⏳ Attendance tracking

### **Success Criteria**
1. **Communication:** Send weekly schedule to parents in < 5 minutes
2. **Export:** One-click export to parent-friendly format
3. **Clarity:** Field locations clear and accurate
4. **Efficiency:** Coordinate logistics without manual data entry

### **Pain Points Addressed**
- ✅ **Manual schedule formatting** → Week Cards export
- ✅ **Field address lookup** → Included in calendar
- ✅ **Home/away confusion** → Clear display in calendar
- ⏳ **Parent notification overhead** → Needs automation

### **Contract Evaluation Questions**
- [ ] Can Emily export a print-friendly week schedule in < 1 minute?
- [ ] Are field addresses copy-pasteable for carpools?
- [ ] Can Emily distinguish home games from away games easily?
- [ ] Is the calendar view sharable via screenshot?
- [ ] Can Emily access same features as head coach?

---

## 🔧 **PERSONA 7: Alex - League System Integrator**

### **Profile**
- **Role:** LeagueAdmin (technical user)
- **Background:** 33-year-old IT professional, volunteers as league tech lead
- **Experience:** Software developer by day, league admin by night
- **Time:** 10-15 hours at season start, 2-3 hours/week during season
- **Context:** Integrates SportSch with SportsEngine, team websites, and analytics

### **Goals & Motivations**
1. **Primary:** Automate schedule distribution to multiple platforms
2. **Secondary:** Maintain data quality and consistency
3. **Tertiary:** Provide analytics to league board (attendance, usage, quality)

### **Key Use Cases**

**UC7.1: Export Schedule for SportsEngine**
- **Frequency:** After each schedule generation + updates
- **Workflow:**
  1. Generate schedule in wizard
  2. Calendar page → Export Schedule
  3. Select format: "SportsEngine"
  4. Download CSV
  5. Upload to SportsEngine admin panel
  6. Verify games appear correctly

**UC7.2: Bulk Data Import (Fields, Teams, Divisions)**
- **Frequency:** Season start + updates
- **Workflow:**
  1. Prepare CSV files (fields, teams, divisions)
  2. Manage → Fields → Import CSV
  3. Review validation errors
  4. Fix issues in CSV
  5. Re-import
  6. Verify in system

**UC7.3: API Integration (Webhooks, iCal)**
- **Frequency:** One-time setup
- **Workflow:**
  1. Generate iCal subscription URL
  2. Configure webhook notifications (future)
  3. Embed calendar in team website (iframe)
  4. Test auto-updates
  5. Monitor integration health

**UC7.4: Schedule Quality Analysis**
- **Frequency:** Monthly
- **Workflow:**
  1. Access feedback data (if API available)
  2. Analyze:
     - Which schedules had 0 unscheduled matchups?
     - Average quality scores by constraint settings
     - Guest game balance distribution
     - Parent satisfaction (proxy: complaints)
  3. Report to board
  4. Adjust defaults for next season

### **Requirements**

**Must Have:**
- ✅ CSV export for SportsEngine
- ✅ CSV import for fields
- ✅ iCal subscription URLs
- ✅ Feedback data capture
- ⏳ API documentation

**Should Have:**
- ⏳ Webhook notifications for schedule changes
- ⏳ REST API for external integrations
- ⏳ Bulk export (all divisions at once)
- ⏳ Analytics dashboard

**Nice to Have:**
- ⏳ GraphQL API
- ⏳ Real-time updates (WebSocket)
- ⏳ Mobile app
- ⏳ Embeddable widgets

### **Success Criteria**
1. **Export:** SportsEngine CSV format 100% compatible
2. **Import:** <5% error rate on field/team imports
3. **API:** iCal syncs within 15 minutes
4. **Data:** Feedback data queryable for analysis
5. **Reliability:** 99.9% uptime

### **Pain Points Addressed**
- ✅ **Manual SportsEngine data entry** → CSV export
- ✅ **Field data management** → CSV import/export
- ✅ **Calendar integration** → iCal subscription
- ✅ **Quality tracking** → Feedback capture
- ⏳ **Real-time updates** → Needs webhook/API

### **Contract Evaluation Questions**
- [ ] Does exported CSV work in SportsEngine without manual edits?
- [ ] Can Alex import 50+ fields without errors?
- [ ] Does iCal subscription include all schedule changes?
- [ ] Is feedback data structured for SQL/pandas analysis?
- [ ] Are there rate limits on API endpoints?

---

## 🏅 **PERSONA 8: Maria - Division Coordinator (Special Role)**

### **Profile**
- **Role:** LeagueAdmin (division-focused)
- **Background:** 48-year-old former coach, now coordinates entire 12U division
- **Experience:** 8 years coaching, 2 years as division coordinator
- **Time:** 15-20 hours/week during season
- **Context:** Manages 6 teams in 12U division, coordinates with other divisions

### **Goals & Motivations**
1. **Primary:** Ensure all teams in division have balanced schedules
2. **Secondary:** Manage guest games for odd team counts (5 or 7 teams)
3. **Tertiary:** Coordinate cross-division play for competitive balance

### **Key Use Cases**

**UC8.1: Balance Schedule Across Division**
- **Frequency:** Season setup
- **Workflow:**
  1. Run Season Wizard for 12U division
  2. Set constraints for balanced play:
     - Equal games per team (13 target)
     - Even home/away distribution
     - Guest games if odd team count
  3. Click "Generate 4 Options"
  4. Compare options for team balance (spread metric)
  5. Select schedule with lowest team spread
  6. Verify guest game balance warning is absent
  7. Apply schedule

**UC8.2: Manage Guest Games (Odd Team Count)**
- **Frequency:** Ongoing for divisions with 5, 7, 9, 11, or 13 teams
- **Workflow:**
  1. Configure guest games in wizard (1 per week)
  2. Set guest anchors (preferred days/times)
  3. Generate schedule
  4. Verify guest games:
     - NOT in week 1
     - NOT in bracket weeks
     - Evenly spread across teams (max spread ≤ 1)
  5. If imbalanced, regenerate with different seed
  6. Check warning: "GUEST_GAMES_IMBALANCED"

**UC8.3: Coordinate Cross-Division Games**
- **Frequency:** 2-3 times per season
- **Workflow:**
  1. Identify teams needing additional competition
  2. Coordinate with 10U or 14U coordinator
  3. Use Request Games feature:
     - Add away games at other division's fields
     - Specify opponent from other division
  4. Include in schedule preview
  5. Verify constraints still met

**UC8.4: Monitor Division Health**
- **Frequency:** Weekly
- **Workflow:**
  1. View Calendar filtered to division
  2. Check metrics:
     - All teams have confirmed games
     - No teams with excessive gaps (idle weeks)
     - Guest games distributed
  3. Address issues proactively

### **Requirements**

**Must Have:**
- ✅ Division-level scheduling (wizard)
- ✅ Guest game management (avoid week 1 & bracket)
- ✅ Guest game balance validation (spread ≤ 1)
- ✅ Team balance metrics (load spread)
- ✅ Cross-division game support (request games)

**Should Have:**
- ⏳ Division health dashboard
- ⏳ Idle gap warnings
- ⏳ Automated balance reports
- ⏳ Guest game utilization metrics

**Nice to Have:**
- ⏳ Cross-division scheduling automation
- ⏳ Competitive balance analysis
- ⏳ Division standings tracker

### **Success Criteria**
1. **Balance:** Team game spread ≤ 1 across division
2. **Guest Games:** All teams get guest games if odd team count
3. **Coverage:** 100% of teams meet 12-game minimum
4. **Quality:** Schedule quality >80/100 on first try
5. **Efficiency:** Setup division schedule in < 1 hour

### **Pain Points Addressed**
- ✅ **Guest game placement errors** → Week 1 & bracket exclusion
- ✅ **Team balance manually verified** → Automated metrics
- ✅ **Cross-division coordination** → Request games feature
- ✅ **Iteration required for balance** → Generate 4 Options

### **Contract Evaluation Questions**
- [ ] Can Maria ensure guest games are evenly distributed?
- [ ] Does the quality score prioritize team balance?
- [ ] Can Maria add cross-division games without breaking constraints?
- [ ] Are guest game warnings actionable?
- [ ] Can Maria compare schedules by balance metrics?

---

## 📊 **PERSONA SUMMARY MATRIX**

| Persona | Role | Primary Goal | Key Feature | Success Metric |
|---------|------|--------------|-------------|----------------|
| **Sarah (Commissioner)** | LeagueAdmin | Fair balanced schedules | Generate 4 Options | <30 min setup time |
| **Mike (Coach)** | Coach | Coordinate games/practices | Request/Approve workflow | <2 min swap request |
| **Jessica (Tournament)** | LeagueAdmin | External game scheduling | Request Games feature | <10 min tournament setup |
| **Robert/Lisa (Parents)** | Viewer | Know when/where games are | iCal + Calendar View | 0 missed games |
| **David (Multi-League)** | GlobalAdmin | Platform health | Feedback Capture | >75 avg quality score |
| **Maria (Division)** | LeagueAdmin | Division balance | Guest Game Balance | Team spread ≤ 1 |

---

## 🎯 **CONTRACT REQUIREMENTS BY PERSONA**

### **Critical Path (Must Work)**

**Sarah (Commissioner):**
1. ✅ Season setup in < 30 minutes
2. ✅ 13 games default
3. ✅ Generate 4 Options working
4. ✅ Guest games excluded from week 1 & bracket
5. ✅ Export to SportsEngine format

**Mike (Coach):**
1. ✅ Team assignment enforced
2. ✅ Request workflow functional
3. ✅ Can only modify own slots
4. ✅ Notifications on request status
5. ✅ Calendar view usable

**Jessica (Tournament):**
1. ✅ Request games UI complete
2. ✅ Backend integration working
3. ✅ Validation prevents errors
4. ✅ Preview highlighting visible
5. ✅ Constraints respected

**Robert/Lisa (Parents):**
1. ✅ Public calendar access
2. ✅ iCal subscription works
3. ✅ Mobile-responsive
4. ⏳ Notifications for changes

**David (Multi-League):**
1. ✅ Global admin access
2. ✅ Feedback capture working
3. ⏳ Usage analytics dashboard
4. ⏳ League creation workflow

**Maria (Division):**
1. ✅ Guest game balance warnings
2. ✅ Team spread metrics
3. ✅ Generate 4 Options
4. ✅ Cross-division games (request games)

---

## 📋 **EVALUATION CHECKLIST FOR EACH ITERATION**

Before deploying features, verify against personas:

### **Functional Requirements:**
- [ ] Does it solve the persona's primary goal?
- [ ] Can the persona complete their workflow in stated time?
- [ ] Are success criteria measurable and met?
- [ ] Are pain points actually addressed?

### **Usability Requirements:**
- [ ] Can non-technical users (Sarah, Mike) use without training?
- [ ] Is mobile experience acceptable (Robert/Lisa)?
- [ ] Are error messages helpful and actionable?
- [ ] Is the happy path < 5 clicks?

### **Security Requirements:**
- [ ] Are permissions properly enforced?
- [ ] Can users only access their authorized data?
- [ ] Are team assignments validated?
- [ ] Are league boundaries respected?

### **Performance Requirements:**
- [ ] Season setup (Sarah): < 30 minutes
- [ ] Game request (Mike): < 2 minutes
- [ ] Schedule export (Alex): < 1 minute
- [ ] Calendar load (Robert): < 3 seconds

### **Quality Requirements:**
- [ ] Schedule quality score > 75/100
- [ ] Team balance spread ≤ 1
- [ ] Guest game balance spread ≤ 1
- [ ] 0 hard rule violations
- [ ] <5% user complaints

---

## 🎯 **USING THESE PERSONAS**

### **During Planning:**
- Map new features to personas
- Ask: "Which persona needs this? Why?"
- Prioritize features that serve multiple personas

### **During Design:**
- Walk through persona's workflow
- Verify each step is intuitive
- Check time constraints are met

### **During Development:**
- Reference persona's requirements
- Test with persona's context
- Validate success criteria

### **During Testing:**
- Role-play as each persona
- Verify contract questions answered "yes"
- Check edge cases for each workflow

### **During Review:**
- Score each persona's satisfaction
- Identify gaps in persona coverage
- Update personas based on real usage

---

**These personas serve as living contracts. Update them as you learn more about your users.**

---

## 📝 **CHANGELOG**

- **2026-03-05:** Status correction - Phase 1 Item 2 (guest game auto-balancer) is still pending.
- **2026-03-05:** Status correction - Phase 1 Item 4 (smart schedule reminders) is still pending.
- **2026-03-03:** Initial creation based on codebase analysis
- Document personas as features evolve
- Add new personas as user types emerge
- Update requirements based on feedback

---

**Use these personas to keep development aligned with real user needs!**
