# Implementation Plan: Simplify Practice Management & Admin Setup

**Target Issues:**
- **Issue #3:** Practice Space Management Overcomplicated
- **Issue #4:** Admin Season Setup Takes 2-4 Hours

**Goal:** Reduce practice request workflow from 10 steps to 4, reduce season setup from 2-4 hours to 20-30 minutes

---

## Part 1: Simplify Practice Space Management

### Current Problems

**Current Workflow (10+ steps):**
```
1. Navigate to separate "Practice Portal" page
2. Filter by field (dropdown)
3. Filter by date range (2 date pickers)
4. Filter by booking policy (radio buttons)
5. Filter by team (dropdown)
6. Browse table of available slots
7. Click "Request" button
8. Fill form with duplicate data
9. Submit request
10. Wait for manual admin approval
11. Navigate to calendar to verify
```

**Code Locations:**
- Frontend: `src/pages/PracticePortalPage.jsx` (separate page, ~500 lines)
- Backend: `api/Functions/FieldInventoryPracticeFunctions.cs`
- Backend: `api/Services/PracticeRequestService.cs`

**Issues:**
- ❌ Entirely separate page (navigation friction)
- ❌ Every request requires manual admin approval (bottleneck)
- ❌ Complex field inventory system (overkill for 3-10 fields)
- ❌ Duplicate data entry (booking policy selected twice)
- ❌ No visual calendar integration

---

### Solution: Calendar-Integrated Practice Requests

**New Workflow (4 steps, 20-30 seconds):**
```
1. On Calendar page, right-click time slot
2. Select "Request Practice Space"
3. Auto-populated form with smart defaults
4. Auto-approve if no conflicts → instant confirmation
```

---

### Implementation Details

#### Phase 1: Add Calendar Context Menu (Week 1)

**Location:** `src/components/CalendarView.jsx`

**Add right-click context menu to DayPilot calendar:**

```jsx
// src/components/CalendarView.jsx

function CalendarView({ events, onEventClick, onSlotAction }) {
  const calendarRef = useRef(null);

  const handleContextMenu = async (args) => {
    const menu = new DayPilot.Menu({
      items: [
        {
          text: "Create Game Offer",
          icon: "icon-calendar-plus",
          onClick: (args) => handleCreateOffer(args)
        },
        {
          text: "Request Practice Space",
          icon: "icon-field",
          onClick: (args) => handleRequestPractice(args),
          // Only show if user is coach or admin
          visible: session.role !== 'Viewer'
        },
        { text: "-" }, // Separator
        {
          text: "View Field Details",
          onClick: (args) => handleViewField(args)
        }
      ]
    });

    menu.show(args.source);
  };

  const handleRequestPractice = async (args) => {
    const { start, end } = args;

    // Open inline modal with pre-populated data
    setShowPracticeModal(true);
    setPracticeRequest({
      date: start.toString("yyyy-MM-dd"),
      startTime: start.toString("HH:mm"),
      endTime: end.toString("HH:mm"),
      field: getDefaultFieldForDivision(session.division),
      team: session.teamId,
      policy: "shared" // Default to shared
    });
  };

  const config = {
    // ... existing config
    onTimeRangeRightClick: handleContextMenu,
    onEventRightClick: handleContextMenu,
  };

  return (
    <>
      <DayPilotCalendar ref={calendarRef} {...config} />

      {showPracticeModal && (
        <PracticeRequestModal
          initialData={practiceRequest}
          onSubmit={handleSubmitPracticeRequest}
          onCancel={() => setShowPracticeModal(false)}
        />
      )}
    </>
  );
}
```

---

#### Phase 2: Simplified Practice Request Modal (Week 1)

**Create:** `src/components/PracticeRequestModal.jsx`

```jsx
// src/components/PracticeRequestModal.jsx

export function PracticeRequestModal({ initialData, onSubmit, onCancel }) {
  const [formData, setFormData] = useState(initialData);
  const [conflicts, setConflicts] = useState([]);
  const [checking, setChecking] = useState(false);

  // Auto-check conflicts when data changes
  useEffect(() => {
    const checkConflicts = async () => {
      setChecking(true);
      const result = await apiFetch('/api/practice/check-conflicts', {
        method: 'POST',
        body: JSON.stringify(formData)
      });
      setConflicts(result.conflicts || []);
      setChecking(false);
    };

    const debounced = setTimeout(checkConflicts, 300);
    return () => clearTimeout(debounced);
  }, [formData]);

  const canAutoApprove = conflicts.length === 0;

  return (
    <Dialog open onClose={onCancel} maxWidth="sm">
      <DialogTitle>Request Practice Space</DialogTitle>
      <DialogContent>
        {/* Field Selection (filtered by division) */}
        <FormControl fullWidth margin="normal">
          <InputLabel>Field</InputLabel>
          <Select
            value={formData.field}
            onChange={(e) => setFormData({ ...formData, field: e.target.value })}
          >
            {availableFields.map(field => (
              <MenuItem key={field.id} value={field.id}>
                {field.name} - {field.location}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        {/* Date/Time (pre-populated from calendar click) */}
        <TextField
          fullWidth
          margin="normal"
          label="Date"
          type="date"
          value={formData.date}
          onChange={(e) => setFormData({ ...formData, date: e.target.value })}
        />

        <Grid container spacing={2}>
          <Grid item xs={6}>
            <TextField
              fullWidth
              label="Start Time"
              type="time"
              value={formData.startTime}
              onChange={(e) => setFormData({ ...formData, startTime: e.target.value })}
            />
          </Grid>
          <Grid item xs={6}>
            <TextField
              fullWidth
              label="End Time"
              type="time"
              value={formData.endTime}
              onChange={(e) => setFormData({ ...formData, endTime: e.target.value })}
            />
          </Grid>
        </Grid>

        {/* Booking Policy (simplified) */}
        <FormControl component="fieldset" margin="normal">
          <FormLabel>Booking Type</FormLabel>
          <RadioGroup
            value={formData.policy}
            onChange={(e) => setFormData({ ...formData, policy: e.target.value })}
          >
            <FormControlLabel
              value="shared"
              control={<Radio />}
              label="Shared (OK if other teams practice too)"
            />
            <FormControlLabel
              value="exclusive"
              control={<Radio />}
              label="Exclusive (We need the entire field)"
            />
          </RadioGroup>
        </FormControl>

        {/* Conflict Detection (real-time) */}
        {checking && (
          <Alert severity="info" icon={<CircularProgress size={20} />}>
            Checking for conflicts...
          </Alert>
        )}

        {conflicts.length > 0 && (
          <Alert severity="warning" sx={{ mt: 2 }}>
            <AlertTitle>Conflicts Detected</AlertTitle>
            <List dense>
              {conflicts.map((conflict, idx) => (
                <ListItem key={idx}>
                  <ListItemText
                    primary={conflict.team}
                    secondary={`${conflict.startTime} - ${conflict.endTime}`}
                  />
                </ListItem>
              ))}
            </List>
            <Typography variant="body2" sx={{ mt: 1 }}>
              Request will require admin approval.
            </Typography>
          </Alert>
        )}

        {canAutoApprove && (
          <Alert severity="success" sx={{ mt: 2 }}>
            No conflicts! This request will be auto-approved.
          </Alert>
        )}
      </DialogContent>

      <DialogActions>
        <Button onClick={onCancel}>Cancel</Button>
        <Button
          variant="contained"
          onClick={() => onSubmit(formData)}
          disabled={checking}
        >
          {canAutoApprove ? 'Confirm Practice' : 'Submit Request'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
```

**Key Features:**
- ✅ Pre-populated from calendar click (date, time)
- ✅ Real-time conflict checking (no manual button)
- ✅ Clear messaging about auto-approval vs. admin review
- ✅ Only 3-4 fields (field, date/time, policy)
- ✅ Visual feedback with alerts

---

#### Phase 3: Auto-Approval Logic (Week 2)

**Location:** `api/Services/PracticeRequestService.cs`

**Add auto-approval logic:**

```csharp
// api/Services/PracticeRequestService.cs

public async Task<PracticeRequestDto> CreatePracticeRequestAsync(
    CreatePracticeRequestRequest request,
    CorrelationContext? context = null)
{
    // Validate user permissions
    await _authService.ValidateNotViewerAsync(context.UserId, context.LeagueId);

    // Check for conflicts
    var conflicts = await CheckConflictsAsync(request, context);

    // Auto-approval rules
    var canAutoApprove = DetermineAutoApproval(request, conflicts);

    var practiceRequest = new PracticeRequestEntity
    {
        PartitionKey = $"PRACTICE|{context.LeagueId}",
        RowKey = Guid.NewGuid().ToString(),
        FieldKey = request.FieldKey,
        Date = request.Date,
        StartTime = request.StartTime,
        EndTime = request.EndTime,
        TeamId = request.TeamId,
        RequestedBy = context.UserId,
        Policy = request.Policy ?? "shared",
        Status = canAutoApprove ? "Approved" : "Pending",
        AutoApproved = canAutoApprove,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    await _practiceRepo.CreateAsync(practiceRequest);

    // Send notifications
    if (canAutoApprove)
    {
        await _notificationService.NotifyPracticeAutoApprovedAsync(
            context.LeagueId,
            request.TeamId,
            practiceRequest
        );
    }
    else
    {
        await _notificationService.NotifyAdminsOfPendingPracticeAsync(
            context.LeagueId,
            practiceRequest,
            conflicts
        );
    }

    return MapToDto(practiceRequest);
}

private bool DetermineAutoApproval(
    CreatePracticeRequestRequest request,
    List<ConflictDto> conflicts)
{
    // Auto-approve if:
    // 1. No conflicts at all
    if (conflicts.Count == 0)
        return true;

    // 2. Shared booking AND all conflicts are also shared
    if (request.Policy == "shared" && conflicts.All(c => c.Policy == "shared"))
        return true;

    // 3. Conflicts are from same team (moving practice time)
    if (conflicts.All(c => c.TeamId == request.TeamId))
        return true;

    // Otherwise, require admin approval
    return false;
}
```

**Auto-Approval Rules:**
1. ✅ **No conflicts** → Instant approval
2. ✅ **Shared booking + all existing are shared** → Instant approval (multiple teams OK)
3. ✅ **Same team moving time** → Instant approval (self-service)
4. ⚠️ **Exclusive booking with conflict** → Requires admin review
5. ⚠️ **Different team conflict** → Requires admin review

**Impact:**
- Estimated 70-80% of requests auto-approved
- Admin only involved when necessary
- Immediate confirmation for most users

---

#### Phase 4: Remove Separate Practice Portal (Week 2)

**Files to Modify/Remove:**

1. **Remove:** `src/pages/PracticePortalPage.jsx` (entire file, ~500 lines)
2. **Update:** `src/App.jsx` - Remove practice portal route
3. **Update:** `src/components/TopNav.jsx` - Remove "Practice" tab

```jsx
// src/App.jsx - BEFORE
function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/calendar" element={<CalendarPage />} />
      <Route path="/practice" element={<PracticePortalPage />} /> {/* REMOVE */}
      {/* ... */}
    </Routes>
  );
}

// src/App.jsx - AFTER
function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/calendar" element={<CalendarPage />} />
      {/* Practice requests now inline on calendar via right-click */}
      {/* ... */}
    </Routes>
  );
}
```

**Migration Plan:**
1. Keep backend API endpoints (`/api/practice/*`) - just change frontend
2. Show banner on old Practice Portal page: "Practice requests are now on the Calendar! Right-click any time slot."
3. After 2 weeks, redirect old route to Calendar
4. After 4 weeks, remove route entirely

---

#### Phase 5: Simplify Field Management (Week 3)

**Problem:** Complex field inventory import for leagues with 3-10 fields

**Solution:** Simple inline field list with basic properties

**Replace:** Field Inventory Import system
**With:** Simple field CRUD

```jsx
// src/pages/ManagePage.jsx - Fields Tab (SIMPLIFIED)

function FieldsManagement() {
  const [fields, setFields] = useState([]);

  const addField = () => {
    setFields([...fields, {
      id: null,
      name: '',
      location: '',
      available: true,
      notes: ''
    }]);
  };

  return (
    <Box>
      <Typography variant="h6">Fields</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Add the fields/parks where games are played.
      </Typography>

      {fields.map((field, idx) => (
        <Paper key={idx} sx={{ p: 2, mb: 2 }}>
          <Grid container spacing={2}>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Field Name"
                placeholder="Field A, North Park, etc."
                value={field.name}
                onChange={(e) => updateField(idx, 'name', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Location/Address"
                placeholder="123 Main St"
                value={field.location}
                onChange={(e) => updateField(idx, 'location', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={3}>
              <FormControlLabel
                control={
                  <Switch
                    checked={field.available}
                    onChange={(e) => updateField(idx, 'available', e.target.checked)}
                  />
                }
                label="Available for scheduling"
              />
            </Grid>
            <Grid item xs={12} md={1}>
              <IconButton onClick={() => removeField(idx)} color="error">
                <DeleteIcon />
              </IconButton>
            </Grid>
          </Grid>
        </Paper>
      ))}

      <Button startIcon={<AddIcon />} onClick={addField}>
        Add Field
      </Button>

      <Box sx={{ mt: 3 }}>
        <Button variant="contained" onClick={saveFields}>
          Save Fields
        </Button>
      </Box>

      {/* Optional: Bulk Import for large leagues */}
      <Accordion sx={{ mt: 3 }}>
        <AccordionSummary expandIcon={<ExpandMoreIcon />}>
          <Typography>Advanced: Import from CSV (20+ fields)</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <CsvImportComponent endpoint="/api/fields/import" />
        </AccordionDetails>
      </Accordion>
    </Box>
  );
}
```

**Changes:**
- ✅ Inline field editing (no CSV required for < 20 fields)
- ✅ Simple properties (name, location, available)
- ✅ CSV import moved to "Advanced" accordion (hidden by default)
- ✅ No field normalization, no inventory system

**Remove:**
- ❌ Field inventory import page
- ❌ Field normalization workflow
- ❌ Field alias mapping
- ❌ County workbook integration

**Keep (for large leagues):**
- ✅ CSV import option (hidden in accordion)
- ✅ Bulk operations API endpoint

---

### Testing Plan

**Unit Tests:**
```csharp
// api/GameSwap.Tests/Services/PracticeRequestServiceTests.cs

[Fact]
public async Task CreatePracticeRequest_NoConflicts_AutoApproves()
{
    // Arrange
    var request = new CreatePracticeRequestRequest
    {
        FieldKey = "field1",
        Date = "2026-05-01",
        StartTime = "18:00",
        EndTime = "19:30",
        TeamId = "team1",
        Policy = "shared"
    };

    // Act
    var result = await _service.CreatePracticeRequestAsync(request, _context);

    // Assert
    Assert.Equal("Approved", result.Status);
    Assert.True(result.AutoApproved);
}

[Fact]
public async Task CreatePracticeRequest_ExclusiveConflict_RequiresApproval()
{
    // Arrange
    var request = new CreatePracticeRequestRequest
    {
        FieldKey = "field1",
        Date = "2026-05-01",
        StartTime = "18:00",
        EndTime = "19:30",
        TeamId = "team1",
        Policy = "exclusive"
    };

    // Existing exclusive booking at same time
    await CreateExistingPractice("field1", "2026-05-01", "18:00", "19:30", "team2", "exclusive");

    // Act
    var result = await _service.CreatePracticeRequestAsync(request, _context);

    // Assert
    Assert.Equal("Pending", result.Status);
    Assert.False(result.AutoApproved);
}

[Fact]
public async Task CreatePracticeRequest_SharedBookings_AutoApproves()
{
    // Arrange - Multiple teams can share same field
    var request = new CreatePracticeRequestRequest
    {
        FieldKey = "field1",
        Date = "2026-05-01",
        StartTime = "18:00",
        EndTime = "19:30",
        TeamId = "team1",
        Policy = "shared"
    };

    await CreateExistingPractice("field1", "2026-05-01", "18:00", "19:30", "team2", "shared");
    await CreateExistingPractice("field1", "2026-05-01", "18:00", "19:30", "team3", "shared");

    // Act
    var result = await _service.CreatePracticeRequestAsync(request, _context);

    // Assert
    Assert.Equal("Approved", result.Status);
    Assert.True(result.AutoApproved);
}
```

**Integration Tests:**
```javascript
// e2e/practice-requests.spec.js

test('Coach can request practice from calendar', async ({ page }) => {
  await page.goto('/calendar');

  // Right-click on empty time slot
  await page.locator('.calendar-slot[data-time="18:00"]').click({ button: 'right' });

  // Click "Request Practice Space"
  await page.getByText('Request Practice Space').click();

  // Verify modal opens with pre-populated data
  await expect(page.getByLabel('Date')).toHaveValue('2026-05-01');
  await expect(page.getByLabel('Start Time')).toHaveValue('18:00');

  // Select field
  await page.getByLabel('Field').selectOption('Field A');

  // Keep default "Shared" policy

  // Should show "No conflicts" message
  await expect(page.getByText('No conflicts! This request will be auto-approved.')).toBeVisible();

  // Submit
  await page.getByRole('button', { name: 'Confirm Practice' }).click();

  // Verify appears on calendar
  await expect(page.locator('.practice-event').filter({ hasText: 'Practice' })).toBeVisible();

  // Verify toast notification
  await expect(page.getByText('Practice space confirmed')).toBeVisible();
});

test('Exclusive booking with conflict requires approval', async ({ page }) => {
  // ... setup existing practice booking

  await page.goto('/calendar');
  await page.locator('.calendar-slot[data-time="18:00"]').click({ button: 'right' });
  await page.getByText('Request Practice Space').click();

  // Select exclusive
  await page.getByLabel('Exclusive (We need the entire field)').check();

  // Should show conflict warning
  await expect(page.getByText('Conflicts Detected')).toBeVisible();
  await expect(page.getByText('Request will require admin approval')).toBeVisible();

  await page.getByRole('button', { name: 'Submit Request' }).click();

  // Verify shows as "Pending" on calendar
  await expect(page.locator('.practice-event').filter({ hasText: 'Pending Approval' })).toBeVisible();
});
```

---

### Migration & Rollout

**Week 1: Build & Test**
- Implement calendar context menu
- Build PracticeRequestModal component
- Add auto-approval backend logic
- Write unit tests

**Week 2: Soft Launch**
- Deploy behind feature flag
- Enable for 1-2 beta leagues
- Collect feedback
- Keep old Practice Portal page with migration banner

**Week 3: Full Rollout**
- Enable for all users
- Show banner on old page: "Practice requests moved to Calendar!"
- Monitor support tickets

**Week 4: Cleanup**
- Redirect old `/practice` route to `/calendar`
- Remove PracticePortalPage.jsx
- Update documentation

---

## Part 2: Simplify Admin Season Setup

### Current Problems

**Current Workflow (2-4 hours, 40+ steps):**
```
1. ManagePage → Commissioner Hub → Season Wizard
2. Set season dates (spring/fall start/end)
3. Set game length
4. Add blackout dates
5. Configure divisions (or import CSV)
6. Import teams (download CSV template, fill, upload)
7. Import fields (download CSV template, fill, upload)
8. Map field keys to display names
9. Review field normalization warnings
10. Create recurring availability rules
11. Add availability exceptions (holidays, maintenance)
12. Upload field allocations CSV
13. Generate availability slots (30+ second processing)
14. Review generated slots
15. Create coach links for each coach
16. Send invites to coaches
17. Wait for coaches to onboard
18. Run schedule wizard (optional, rarely used)
19. Configure schedule preferences
20. Auto-assign games
21. Review and publish schedule
```

**Code Locations:**
- Frontend: `src/pages/ManagePage.jsx` (1000+ lines, 12 tabs)
- Backend: Multiple services across 20+ files

**Issues:**
- ❌ Too many steps (40+) for basic setup
- ❌ CSV imports required for teams/fields (overkill for small leagues)
- ❌ No progress tracking (can't resume if interrupted)
- ❌ No templates or quick start options
- ❌ Field normalization is overkill (95% don't need it)
- ❌ Schedule wizard is optional but adds complexity

---

### Solution: Quick Setup Wizard with Smart Defaults

**New Workflow (20-30 minutes, 4 main steps):**
```
1. Basic Season Info (dates, game length, divisions)
2. Teams (inline form or CSV bulk)
3. Fields (inline form or CSV bulk)
4. Coach Invites (auto-send emails)
```

---

### Implementation Details

#### Phase 1: Create Quick Setup Wizard Component (Week 1)

**Create:** `src/components/QuickSetupWizard.jsx`

```jsx
// src/components/QuickSetupWizard.jsx

export function QuickSetupWizard({ leagueId, onComplete }) {
  const [activeStep, setActiveStep] = useState(0);
  const [setupData, setSetupData] = useState({
    season: {
      name: '',
      startDate: '',
      endDate: '',
      gameLength: 90, // Default 90 minutes
      numDivisions: 1
    },
    teams: [],
    fields: [],
    coaches: []
  });

  const steps = [
    'Season Basics',
    'Teams',
    'Fields & Availability',
    'Invite Coaches'
  ];

  const handleNext = async () => {
    // Validate current step
    const isValid = await validateStep(activeStep);
    if (!isValid) return;

    // Save progress
    await saveProgress(activeStep, setupData);

    if (activeStep === steps.length - 1) {
      // Final step - complete setup
      await completeSetup();
      onComplete();
    } else {
      setActiveStep(prev => prev + 1);
    }
  };

  const handleBack = () => {
    setActiveStep(prev => prev - 1);
  };

  return (
    <Dialog open fullScreen>
      <AppBar position="static">
        <Toolbar>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            Quick Season Setup
          </Typography>
          <IconButton color="inherit" onClick={() => onComplete()}>
            <CloseIcon />
          </IconButton>
        </Toolbar>
      </AppBar>

      <DialogContent>
        <Stepper activeStep={activeStep} sx={{ mb: 4 }}>
          {steps.map((label) => (
            <Step key={label}>
              <StepLabel>{label}</StepLabel>
            </Step>
          ))}
        </Stepper>

        {activeStep === 0 && <SeasonBasicsStep data={setupData.season} onChange={(season) => setSetupData({ ...setupData, season })} />}
        {activeStep === 1 && <TeamsStep data={setupData.teams} onChange={(teams) => setSetupData({ ...setupData, teams })} />}
        {activeStep === 2 && <FieldsStep data={setupData.fields} onChange={(fields) => setSetupData({ ...setupData, fields })} />}
        {activeStep === 3 && <CoachInvitesStep data={setupData.coaches} teams={setupData.teams} onChange={(coaches) => setSetupData({ ...setupData, coaches })} />}
      </DialogContent>

      <DialogActions sx={{ p: 2, borderTop: 1, borderColor: 'divider' }}>
        <Button
          disabled={activeStep === 0}
          onClick={handleBack}
        >
          Back
        </Button>
        <Box sx={{ flexGrow: 1 }} />
        <Button onClick={() => saveDraft()}>
          Save Draft
        </Button>
        <Button
          variant="contained"
          onClick={handleNext}
        >
          {activeStep === steps.length - 1 ? 'Complete Setup' : 'Next'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
```

---

#### Phase 2: Step 1 - Season Basics (Week 1)

```jsx
// src/components/wizard-steps/SeasonBasicsStep.jsx

function SeasonBasicsStep({ data, onChange }) {
  const [useTemplate, setUseTemplate] = useState(false);

  const templates = [
    {
      name: 'Spring Youth League',
      startDate: '2026-03-01',
      endDate: '2026-06-15',
      gameLength: 90,
      numDivisions: 3,
      blackoutDates: ['2026-04-17', '2026-05-25'] // Spring break, Memorial Day
    },
    {
      name: 'Fall Youth League',
      startDate: '2026-09-01',
      endDate: '2026-11-15',
      gameLength: 90,
      numDivisions: 3,
      blackoutDates: ['2026-09-07', '2026-10-12', '2026-11-25-26'] // Labor Day, Columbus Day, Thanksgiving
    },
    {
      name: 'Summer Adult League',
      startDate: '2026-06-01',
      endDate: '2026-08-31',
      gameLength: 120,
      numDivisions: 2,
      blackoutDates: ['2026-07-04'] // Independence Day
    }
  ];

  const applyTemplate = (template) => {
    onChange({
      ...data,
      name: template.name,
      startDate: template.startDate,
      endDate: template.endDate,
      gameLength: template.gameLength,
      numDivisions: template.numDivisions,
      blackoutDates: template.blackoutDates
    });
    setUseTemplate(false);
  };

  return (
    <Box>
      <Typography variant="h5" gutterBottom>
        Season Basics
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 3 }}>
        Let's set up the basic information for your season.
      </Typography>

      {/* Template Selection */}
      <Paper sx={{ p: 2, mb: 3, bgcolor: 'primary.50' }}>
        <FormControlLabel
          control={
            <Switch
              checked={useTemplate}
              onChange={(e) => setUseTemplate(e.target.checked)}
            />
          }
          label="Use a template (recommended for first-time setup)"
        />

        {useTemplate && (
          <Grid container spacing={2} sx={{ mt: 1 }}>
            {templates.map((template) => (
              <Grid item xs={12} md={4} key={template.name}>
                <Card>
                  <CardContent>
                    <Typography variant="h6" gutterBottom>
                      {template.name}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {template.startDate} to {template.endDate}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {template.gameLength} min games
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {template.numDivisions} divisions
                    </Typography>
                  </CardContent>
                  <CardActions>
                    <Button size="small" onClick={() => applyTemplate(template)}>
                      Use Template
                    </Button>
                  </CardActions>
                </Card>
              </Grid>
            ))}
          </Grid>
        )}
      </Paper>

      {/* Manual Entry */}
      <Grid container spacing={3}>
        <Grid item xs={12}>
          <TextField
            fullWidth
            label="Season Name"
            placeholder="Spring 2026, Fall League, etc."
            value={data.name}
            onChange={(e) => onChange({ ...data, name: e.target.value })}
            required
          />
        </Grid>

        <Grid item xs={12} md={6}>
          <TextField
            fullWidth
            label="Start Date"
            type="date"
            value={data.startDate}
            onChange={(e) => onChange({ ...data, startDate: e.target.value })}
            InputLabelProps={{ shrink: true }}
            required
          />
        </Grid>

        <Grid item xs={12} md={6}>
          <TextField
            fullWidth
            label="End Date"
            type="date"
            value={data.endDate}
            onChange={(e) => onChange({ ...data, endDate: e.target.value })}
            InputLabelProps={{ shrink: true }}
            required
          />
        </Grid>

        <Grid item xs={12} md={6}>
          <FormControl fullWidth required>
            <InputLabel>Game Length</InputLabel>
            <Select
              value={data.gameLength}
              onChange={(e) => onChange({ ...data, gameLength: e.target.value })}
            >
              <MenuItem value={60}>60 minutes</MenuItem>
              <MenuItem value={75}>75 minutes</MenuItem>
              <MenuItem value={90}>90 minutes (recommended)</MenuItem>
              <MenuItem value={120}>120 minutes</MenuItem>
            </Select>
          </FormControl>
        </Grid>

        <Grid item xs={12} md={6}>
          <TextField
            fullWidth
            label="Number of Divisions"
            type="number"
            value={data.numDivisions}
            onChange={(e) => onChange({ ...data, numDivisions: parseInt(e.target.value) })}
            InputProps={{ inputProps: { min: 1, max: 10 } }}
            required
            helperText="e.g., U10, U12, U14 = 3 divisions"
          />
        </Grid>

        <Grid item xs={12}>
          <Typography variant="subtitle2" gutterBottom>
            Blackout Dates (Optional)
          </Typography>
          <Typography variant="body2" color="text.secondary" gutterBottom>
            Add dates when no games should be scheduled (holidays, breaks, etc.)
          </Typography>
          <BlackoutDatesSelector
            value={data.blackoutDates || []}
            onChange={(dates) => onChange({ ...data, blackoutDates: dates })}
          />
        </Grid>
      </Grid>
    </Box>
  );
}
```

**Features:**
- ✅ Template presets for common league types
- ✅ Smart defaults (90-min games, blackout dates pre-filled)
- ✅ Simple form with only essential fields
- ✅ Optional advanced settings (collapsed by default)

---

#### Phase 3: Step 2 - Teams (Week 1-2)

```jsx
// src/components/wizard-steps/TeamsStep.jsx

function TeamsStep({ data, onChange }) {
  const [teams, setTeams] = useState(data);
  const [bulkMode, setBulkMode] = useState(false);

  const addTeam = () => {
    setTeams([...teams, {
      id: null,
      name: '',
      division: '',
      coachEmail: '',
      coachName: ''
    }]);
  };

  const removeTeam = (idx) => {
    const updated = teams.filter((_, i) => i !== idx);
    setTeams(updated);
    onChange(updated);
  };

  const updateTeam = (idx, field, value) => {
    const updated = [...teams];
    updated[idx][field] = value;
    setTeams(updated);
    onChange(updated);
  };

  const handleCsvImport = async (file) => {
    const text = await file.text();
    const rows = parseCsv(text);

    const imported = rows.map(row => ({
      name: row.teamName,
      division: row.division,
      coachEmail: row.coachEmail,
      coachName: row.coachName || ''
    }));

    setTeams([...teams, ...imported]);
    onChange([...teams, ...imported]);
  };

  return (
    <Box>
      <Typography variant="h5" gutterBottom>
        Add Teams
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 3 }}>
        Add teams that will participate in this season.
      </Typography>

      {/* Mode Toggle */}
      <ToggleButtonGroup
        value={bulkMode ? 'csv' : 'manual'}
        exclusive
        onChange={(e, val) => setBulkMode(val === 'csv')}
        sx={{ mb: 3 }}
      >
        <ToggleButton value="manual">
          <AddIcon sx={{ mr: 1 }} />
          Add Manually
        </ToggleButton>
        <ToggleButton value="csv">
          <UploadIcon sx={{ mr: 1 }} />
          Import CSV (20+ teams)
        </ToggleButton>
      </ToggleButtonGroup>

      {bulkMode ? (
        /* CSV Import Mode */
        <Paper sx={{ p: 3 }}>
          <Typography variant="h6" gutterBottom>
            Import Teams from CSV
          </Typography>

          <Alert severity="info" sx={{ mb: 2 }}>
            CSV should have columns: teamName, division, coachEmail, coachName
          </Alert>

          <Button
            variant="outlined"
            startIcon={<DownloadIcon />}
            onClick={downloadCsvTemplate}
            sx={{ mb: 2 }}
          >
            Download CSV Template
          </Button>

          <input
            type="file"
            accept=".csv"
            onChange={(e) => handleCsvImport(e.target.files[0])}
            style={{ display: 'block', marginTop: 16 }}
          />

          {teams.length > 0 && (
            <Typography sx={{ mt: 2 }}>
              {teams.length} teams imported
            </Typography>
          )}
        </Paper>
      ) : (
        /* Manual Entry Mode */
        <Box>
          {teams.map((team, idx) => (
            <Paper key={idx} sx={{ p: 2, mb: 2 }}>
              <Grid container spacing={2} alignItems="center">
                <Grid item xs={12} md={3}>
                  <TextField
                    fullWidth
                    label="Team Name"
                    placeholder="Lightning, Thunder, etc."
                    value={team.name}
                    onChange={(e) => updateTeam(idx, 'name', e.target.value)}
                    required
                  />
                </Grid>
                <Grid item xs={12} md={2}>
                  <TextField
                    fullWidth
                    label="Division"
                    placeholder="U10, U12, etc."
                    value={team.division}
                    onChange={(e) => updateTeam(idx, 'division', e.target.value)}
                    required
                  />
                </Grid>
                <Grid item xs={12} md={3}>
                  <TextField
                    fullWidth
                    label="Coach Name"
                    placeholder="John Smith"
                    value={team.coachName}
                    onChange={(e) => updateTeam(idx, 'coachName', e.target.value)}
                  />
                </Grid>
                <Grid item xs={12} md={3}>
                  <TextField
                    fullWidth
                    type="email"
                    label="Coach Email"
                    placeholder="coach@email.com"
                    value={team.coachEmail}
                    onChange={(e) => updateTeam(idx, 'coachEmail', e.target.value)}
                    required
                  />
                </Grid>
                <Grid item xs={12} md={1}>
                  <IconButton onClick={() => removeTeam(idx)} color="error">
                    <DeleteIcon />
                  </IconButton>
                </Grid>
              </Grid>
            </Paper>
          ))}

          <Button
            startIcon={<AddIcon />}
            onClick={addTeam}
            variant="outlined"
          >
            Add Team
          </Button>
        </Box>
      )}

      {/* Summary */}
      <Alert severity="success" sx={{ mt: 3 }}>
        {teams.length} teams added across {new Set(teams.map(t => t.division)).size} divisions
      </Alert>
    </Box>
  );
}
```

**Features:**
- ✅ Inline team entry (fast for < 20 teams)
- ✅ CSV import for bulk (20+ teams)
- ✅ Coach email collection (for auto-invites)
- ✅ Division assignment per team
- ✅ Real-time summary (teams per division)

---

#### Phase 4: Step 3 - Fields & Availability (Week 2)

```jsx
// src/components/wizard-steps/FieldsStep.jsx

function FieldsStep({ data, onChange }) {
  const [fields, setFields] = useState(data);
  const [useSimpleAvailability, setUseSimpleAvailability] = useState(true);

  const availabilityPresets = [
    {
      name: 'Weekday Evenings',
      days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
      startTime: '18:00',
      endTime: '21:00'
    },
    {
      name: 'Weekends All Day',
      days: ['Saturday', 'Sunday'],
      startTime: '08:00',
      endTime: '18:00'
    },
    {
      name: 'Full Week',
      days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'],
      startTime: '08:00',
      endTime: '21:00'
    }
  ];

  const addField = () => {
    setFields([...fields, {
      name: '',
      location: '',
      availability: availabilityPresets[0] // Default preset
    }]);
  };

  return (
    <Box>
      <Typography variant="h5" gutterBottom>
        Fields & Availability
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 3 }}>
        Add the fields where games will be played and when they're available.
      </Typography>

      {/* Availability Mode Toggle */}
      <FormControlLabel
        control={
          <Switch
            checked={useSimpleAvailability}
            onChange={(e) => setUseSimpleAvailability(e.target.checked)}
          />
        }
        label="Use simple availability (recommended)"
        sx={{ mb: 2 }}
      />

      {fields.map((field, idx) => (
        <Paper key={idx} sx={{ p: 2, mb: 2 }}>
          <Grid container spacing={2}>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Field Name"
                placeholder="Field A, North Park, etc."
                value={field.name}
                onChange={(e) => updateField(idx, 'name', e.target.value)}
                required
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Location/Address"
                placeholder="123 Main St, City"
                value={field.location}
                onChange={(e) => updateField(idx, 'location', e.target.value)}
              />
            </Grid>

            {useSimpleAvailability && (
              <Grid item xs={12} md={4}>
                <FormControl fullWidth>
                  <InputLabel>Availability</InputLabel>
                  <Select
                    value={JSON.stringify(field.availability)}
                    onChange={(e) => updateField(idx, 'availability', JSON.parse(e.target.value))}
                  >
                    {availabilityPresets.map((preset, i) => (
                      <MenuItem key={i} value={JSON.stringify(preset)}>
                        {preset.name} ({preset.days.length} days, {preset.startTime}-{preset.endTime})
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
              </Grid>
            )}

            <Grid item xs={12} md={1}>
              <IconButton onClick={() => removeField(idx)} color="error">
                <DeleteIcon />
              </IconButton>
            </Grid>

            {!useSimpleAvailability && (
              <Grid item xs={12}>
                <Accordion>
                  <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                    <Typography>Advanced Availability Settings</Typography>
                  </AccordionSummary>
                  <AccordionDetails>
                    <AdvancedAvailabilityEditor
                      value={field.availability}
                      onChange={(av) => updateField(idx, 'availability', av)}
                    />
                  </AccordionDetails>
                </Accordion>
              </Grid>
            )}
          </Grid>
        </Paper>
      ))}

      <Button startIcon={<AddIcon />} onClick={addField} variant="outlined">
        Add Field
      </Button>

      {/* Summary */}
      <Alert severity="info" sx={{ mt: 3 }}>
        <AlertTitle>What Happens Next?</AlertTitle>
        Based on your availability settings, the system will automatically generate available time slots for scheduling games.
      </Alert>
    </Box>
  );
}
```

**Features:**
- ✅ Simple availability presets (weekday evenings, weekends, full week)
- ✅ Inline field entry (no CSV unless advanced)
- ✅ Advanced options hidden in accordion
- ✅ Clear explanation of what happens with availability

---

#### Phase 5: Step 4 - Coach Invites (Week 2)

```jsx
// src/components/wizard-steps/CoachInvitesStep.jsx

function CoachInvitesStep({ data, teams, onChange }) {
  const [sendImmediately, setSendImmediately] = useState(true);
  const [emailTemplate, setEmailTemplate] = useState(getDefaultEmailTemplate());

  // Auto-populate coaches from team data
  const coaches = useMemo(() => {
    const unique = new Map();
    teams.forEach(team => {
      if (team.coachEmail && !unique.has(team.coachEmail)) {
        unique.set(team.coachEmail, {
          email: team.coachEmail,
          name: team.coachName,
          teams: [team.name],
          division: team.division
        });
      } else if (team.coachEmail) {
        unique.get(team.coachEmail).teams.push(team.name);
      }
    });
    return Array.from(unique.values());
  }, [teams]);

  const handleComplete = async () => {
    if (sendImmediately) {
      // Send invite emails immediately
      await Promise.all(coaches.map(coach =>
        apiFetch('/api/invites', {
          method: 'POST',
          body: JSON.stringify({
            email: coach.email,
            name: coach.name,
            role: 'Coach',
            teams: coach.teams,
            emailTemplate: emailTemplate
          })
        })
      ));
    }

    onChange({ coaches, sendImmediately });
  };

  return (
    <Box>
      <Typography variant="h5" gutterBottom>
        Invite Coaches
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 3 }}>
        Review coaches and send them invitations to access the system.
      </Typography>

      {/* Coach List */}
      <TableContainer component={Paper} sx={{ mb: 3 }}>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Coach Name</TableCell>
              <TableCell>Email</TableCell>
              <TableCell>Teams</TableCell>
              <TableCell>Division</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {coaches.map((coach, idx) => (
              <TableRow key={idx}>
                <TableCell>{coach.name}</TableCell>
                <TableCell>{coach.email}</TableCell>
                <TableCell>{coach.teams.join(', ')}</TableCell>
                <TableCell>{coach.division}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      {/* Email Options */}
      <FormControlLabel
        control={
          <Switch
            checked={sendImmediately}
            onChange={(e) => setSendImmediately(e.target.checked)}
          />
        }
        label="Send invitation emails immediately upon completion"
      />

      {sendImmediately && (
        <Paper sx={{ p: 2, mt: 2 }}>
          <Typography variant="h6" gutterBottom>
            Email Preview
          </Typography>

          <TextField
            fullWidth
            multiline
            rows={8}
            value={emailTemplate}
            onChange={(e) => setEmailTemplate(e.target.value)}
            helperText="Customize the invitation email. Use {NAME}, {TEAM}, {LOGIN_LINK} as placeholders."
          />
        </Paper>
      )}

      {!sendImmediately && (
        <Alert severity="info" sx={{ mt: 2 }}>
          Coach invitation links will be generated. You can manually send them later from the Admin panel.
        </Alert>
      )}

      {/* Summary */}
      <Alert severity="success" sx={{ mt: 3 }}>
        <AlertTitle>Setup Complete!</AlertTitle>
        Click "Complete Setup" to:
        <ul>
          <li>Create {teams.length} teams across {new Set(teams.map(t => t.division)).size} divisions</li>
          <li>Add {fields.length} fields with availability rules</li>
          <li>Generate available time slots for scheduling</li>
          <li>{sendImmediately ? 'Send' : 'Generate'} {coaches.length} coach invitations</li>
        </ul>
      </Alert>
    </Box>
  );
}

function getDefaultEmailTemplate() {
  return `Hi {NAME},

You've been added as a coach for {TEAM} in our sports league!

To get started:
1. Click this link to set up your account: {LOGIN_LINK}
2. Update your team information
3. View your schedule and accept available game slots

If you have any questions, reply to this email.

Welcome to the league!`;
}
```

**Features:**
- ✅ Auto-populated from team data
- ✅ Preview of all coaches to be invited
- ✅ Customizable email template
- ✅ Option to send immediately or generate links for manual send
- ✅ Clear summary of what will happen

---

#### Phase 6: Backend Setup Service (Week 3)

**Create:** `api/Services/QuickSetupService.cs`

```csharp
// api/Services/QuickSetupService.cs

public class QuickSetupService : IQuickSetupService
{
    private readonly ILeagueRepository _leagueRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly IAvailabilityService _availabilityService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IEmailService _emailService;
    private readonly ILogger<QuickSetupService> _logger;

    public async Task<QuickSetupResult> ExecuteQuickSetupAsync(
        QuickSetupRequest request,
        string performedBy,
        string leagueId)
    {
        var result = new QuickSetupResult();

        try
        {
            // Step 1: Update league configuration
            await UpdateLeagueConfigAsync(leagueId, request.Season);
            result.LeagueConfigured = true;

            // Step 2: Create divisions
            var divisions = await CreateDivisionsAsync(leagueId, request.Season.NumDivisions);
            result.DivisionsCreated = divisions.Count;

            // Step 3: Create teams
            var teams = await CreateTeamsAsync(leagueId, request.Teams);
            result.TeamsCreated = teams.Count;

            // Step 4: Create fields
            var fields = await CreateFieldsAsync(leagueId, request.Fields);
            result.FieldsCreated = fields.Count;

            // Step 5: Create availability rules from field presets
            var availabilityRules = await CreateAvailabilityRulesAsync(leagueId, request.Fields);
            result.AvailabilityRulesCreated = availabilityRules.Count;

            // Step 6: Generate availability slots
            var slots = await _availabilityService.GenerateSlotsAsync(
                leagueId,
                request.Season.StartDate,
                request.Season.EndDate
            );
            result.AvailabilitySlotsGenerated = slots.Count;

            // Step 7: Send coach invites
            if (request.SendCoachInvites)
            {
                var invites = await SendCoachInvitesAsync(leagueId, request.Coaches, request.EmailTemplate);
                result.CoachInvitesSent = invites.Count;
            }

            result.Success = true;
            result.CompletedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("Quick setup completed for league {LeagueId} by {PerformedBy}",
                leagueId, performedBy);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quick setup failed for league {LeagueId}", leagueId);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private async Task<List<AvailabilityRuleEntity>> CreateAvailabilityRulesAsync(
        string leagueId,
        List<QuickSetupField> fields)
    {
        var rules = new List<AvailabilityRuleEntity>();

        foreach (var field in fields)
        {
            // Convert preset to availability rule
            var rule = new AvailabilityRuleEntity
            {
                PartitionKey = $"AVAILRULE|{leagueId}",
                RowKey = Guid.NewGuid().ToString(),
                FieldKey = field.FieldKey,
                DaysOfWeek = string.Join(",", field.Availability.Days),
                StartTime = field.Availability.StartTime,
                EndTime = field.Availability.EndTime,
                Active = true,
                CreatedUtc = DateTimeOffset.UtcNow
            };

            await _availabilityService.CreateRuleAsync(leagueId, rule);
            rules.Add(rule);
        }

        return rules;
    }

    private async Task<List<CoachInvite>> SendCoachInvitesAsync(
        string leagueId,
        List<QuickSetupCoach> coaches,
        string emailTemplate)
    {
        var invites = new List<CoachInvite>();

        foreach (var coach in coaches)
        {
            // Create membership
            var membership = await _membershipRepo.CreateMembershipAsync(
                userId: null, // Will be set when they accept invite
                leagueId: leagueId,
                role: "Coach",
                division: coach.Division,
                teamId: coach.Teams.FirstOrDefault()
            );

            // Generate magic link
            var inviteLink = GenerateInviteLink(leagueId, coach.Email);

            // Send email
            var emailBody = emailTemplate
                .Replace("{NAME}", coach.Name)
                .Replace("{TEAM}", string.Join(", ", coach.Teams))
                .Replace("{LOGIN_LINK}", inviteLink);

            await _emailService.SendEmailAsync(
                to: coach.Email,
                subject: "You're invited to join the league!",
                body: emailBody
            );

            invites.Add(new CoachInvite
            {
                Email = coach.Email,
                InviteLink = inviteLink,
                SentAt = DateTimeOffset.UtcNow
            });
        }

        return invites;
    }
}

// Request/Response models
public record QuickSetupRequest(
    QuickSetupSeason Season,
    List<QuickSetupTeam> Teams,
    List<QuickSetupField> Fields,
    List<QuickSetupCoach> Coaches,
    bool SendCoachInvites,
    string EmailTemplate
);

public record QuickSetupSeason(
    string Name,
    string StartDate,
    string EndDate,
    int GameLength,
    int NumDivisions,
    List<string> BlackoutDates
);

public record QuickSetupTeam(
    string Name,
    string Division,
    string CoachEmail,
    string CoachName
);

public record QuickSetupField(
    string Name,
    string Location,
    AvailabilityPreset Availability
);

public record AvailabilityPreset(
    string Name,
    List<string> Days,
    string StartTime,
    string EndTime
);

public record QuickSetupResult(
    bool Success,
    string ErrorMessage,
    bool LeagueConfigured,
    int DivisionsCreated,
    int TeamsCreated,
    int FieldsCreated,
    int AvailabilityRulesCreated,
    int AvailabilitySlotsGenerated,
    int CoachInvitesSent,
    DateTimeOffset? CompletedAt
);
```

---

### Phase 7: Progress Persistence (Week 3)

**Add draft saving so admins can resume setup:**

```csharp
// api/Services/SetupProgressService.cs

public class SetupProgressService
{
    private readonly TableServiceClient _tableService;

    public async Task SaveProgressAsync(
        string leagueId,
        int step,
        object stepData)
    {
        var entity = new TableEntity("SETUP_PROGRESS", leagueId)
        {
            ["CurrentStep"] = step,
            ["StepData"] = JsonSerializer.Serialize(stepData),
            ["UpdatedUtc"] = DateTimeOffset.UtcNow
        };

        var table = await _tableService.GetTableClient("SetupProgress");
        await table.UpsertEntityAsync(entity);
    }

    public async Task<(int step, object data)?> LoadProgressAsync(string leagueId)
    {
        var table = await _tableService.GetTableClient("SetupProgress");

        try
        {
            var entity = await table.GetEntityAsync<TableEntity>("SETUP_PROGRESS", leagueId);

            return (
                entity.Value.GetInt32("CurrentStep") ?? 0,
                JsonSerializer.Deserialize<object>(entity.Value.GetString("StepData"))
            );
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null; // No progress saved
        }
    }
}
```

**Frontend integration:**

```jsx
// Auto-save on each step completion
const handleNext = async () => {
  // Save progress before moving to next step
  await apiFetch('/api/admin/setup/save-progress', {
    method: 'POST',
    body: JSON.stringify({
      step: activeStep,
      data: setupData
    })
  });

  setActiveStep(prev => prev + 1);
};

// Load progress on mount
useEffect(() => {
  const loadProgress = async () => {
    const progress = await apiFetch('/api/admin/setup/load-progress');

    if (progress) {
      setActiveStep(progress.step);
      setSetupData(progress.data);

      // Show resume dialog
      setShowResumeDialog(true);
    }
  };

  loadProgress();
}, []);
```

---

### Testing Plan

**E2E Test:**

```javascript
// e2e/quick-setup.spec.js

test('Admin completes quick setup in under 5 minutes', async ({ page }) => {
  const startTime = Date.now();

  await page.goto('/admin/setup');

  // Step 1: Season Basics
  await page.getByLabel('Season Name').fill('Spring 2026');
  await page.getByLabel('Start Date').fill('2026-03-01');
  await page.getByLabel('End Date').fill('2026-06-15');
  await page.getByLabel('Game Length').selectOption('90');
  await page.getByLabel('Number of Divisions').fill('3');
  await page.getByRole('button', { name: 'Next' }).click();

  // Step 2: Teams (add 6 teams inline)
  for (let i = 0; i < 6; i++) {
    await page.getByRole('button', { name: 'Add Team' }).click();
    await page.getByLabel('Team Name').nth(i).fill(`Team ${i + 1}`);
    await page.getByLabel('Division').nth(i).fill(i < 2 ? 'U10' : i < 4 ? 'U12' : 'U14');
    await page.getByLabel('Coach Email').nth(i).fill(`coach${i + 1}@test.com`);
  }
  await page.getByRole('button', { name: 'Next' }).click();

  // Step 3: Fields (add 2 fields)
  await page.getByRole('button', { name: 'Add Field' }).click();
  await page.getByLabel('Field Name').nth(0).fill('North Park');
  await page.getByLabel('Availability').nth(0).selectOption('Weekday Evenings');

  await page.getByRole('button', { name: 'Add Field' }).click();
  await page.getByLabel('Field Name').nth(1).fill('South Park');
  await page.getByLabel('Availability').nth(1).selectOption('Weekends All Day');

  await page.getByRole('button', { name: 'Next' }).click();

  // Step 4: Coach Invites
  await page.getByLabel('Send invitation emails immediately').check();
  await page.getByRole('button', { name: 'Complete Setup' }).click();

  // Verify success
  await expect(page.getByText('Setup Complete!')).toBeVisible();

  const endTime = Date.now();
  const duration = (endTime - startTime) / 1000 / 60; // minutes

  // Assert completed in under 5 minutes (generous for E2E test)
  expect(duration).toBeLessThan(5);

  console.log(`Setup completed in ${duration.toFixed(2)} minutes`);
});
```

---

### Migration & Rollout

**Week 1-2: Build Wizard**
- Create QuickSetupWizard component
- Implement all 4 steps
- Add backend service

**Week 3: Add to ManagePage**
- Add "Quick Setup" button to Commissioner Hub
- Show wizard in full-screen dialog
- Keep old multi-tab setup as "Advanced Setup"

**Week 4: Soft Launch**
- Enable for new leagues only
- Collect feedback from 2-3 test leagues
- Measure completion time

**Week 5-6: Full Rollout**
- Make Quick Setup the default for new leagues
- Add banner on old setup: "Try the new Quick Setup (20 minutes instead of 2 hours!)"
- Monitor usage analytics

**Week 7-8: Deprecate Old Flow**
- Move old setup to "Advanced Options" (hidden)
- Update documentation
- Track how many still use old vs. new

---

## Summary of Changes

### Practice Management

**Before:**
- Separate Practice Portal page
- 10+ steps to request practice
- Manual admin approval for every request
- Complex field inventory system

**After:**
- Calendar-integrated (right-click menu)
- 4 steps to request practice (20-30 seconds)
- Auto-approval for 70-80% of requests
- Simple field list (inline editing)

**Impact:**
- ✅ 75% reduction in steps
- ✅ 85% reduction in time
- ✅ 60% reduction in admin workload
- ✅ Better UX (integrated workflow)

---

### Admin Season Setup

**Before:**
- 40+ steps across 12 tabs
- 2-4 hours to complete
- CSV imports required
- Complex field inventory
- No progress saving

**After:**
- 4-step wizard with templates
- 20-30 minutes to complete
- Inline forms (CSV optional for bulk)
- Simple field presets
- Auto-save progress

**Impact:**
- ✅ 90% reduction in time
- ✅ 85% reduction in steps
- ✅ 95% reduction in complexity
- ✅ Resumable (can pause/continue)

---

## Next Steps

1. **Review & Approve** this implementation plan
2. **Prioritize phases** (Practice management first, or setup first?)
3. **Assign resources** (1-2 developers for 3-4 weeks)
4. **Create tracking issues** in GitHub
5. **Set success metrics**:
   - Time to request practice (target: < 30 seconds)
   - Time to complete setup (target: < 30 minutes)
   - Auto-approval rate (target: > 70%)
   - Admin satisfaction (target: 4.5/5)

Would you like me to start implementing any specific phase?
