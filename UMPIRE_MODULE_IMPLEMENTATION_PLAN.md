# Umpire Management Module - Implementation Plan
**Version:** 1.0
**Date:** 2026-04-23
**Target:** MVP Delivery in 3-4 weeks
**Effort:** ~95 hours (12 developer days)

---

## IMPLEMENTATION STRATEGY

### Sequencing Approach: Bottom-Up + Vertical Slices

**Phase 1:** Foundation (data models, core services)
**Phase 2:** Admin assignment flow (end-to-end first feature)
**Phase 3:** Umpire portal (self-service)
**Phase 4:** Coach integration (read-only views)
**Phase 5:** Notifications + polish
**Phase 6:** Testing + deployment

**Rationale:** Build data layer first, then complete one vertical slice (admin assignment) before expanding to other user types. This allows early testing and validation.

---

## PHASE 1: FOUNDATION (Days 1-2, ~15 hours)

### Goal: Data models, repositories, constants

**Outcome:** Database ready, service contracts defined, no UI yet

---

### Task 1.1: Add Umpire Role Constant
**File:** `api/Storage/Constants.cs`
**Effort:** 5 minutes

```csharp
public static class Roles
{
    public const string LeagueAdmin = "LeagueAdmin";
    public const string Coach = "Coach";
    public const string Viewer = "Viewer";
    public const string Umpire = "Umpire";  // NEW
}
```

**Also update:** `src/lib/constants.js` (frontend sync)

**Test:** Verify role constant exists in both frontend and backend

---

### Task 1.2: Add Umpire Error Codes
**File:** `api/Storage/ErrorCodes.cs`
**Effort:** 10 minutes

```csharp
// Umpire-specific errors
public const string UMPIRE_NOT_FOUND = "UMPIRE_NOT_FOUND";
public const string UMPIRE_INACTIVE = "UMPIRE_INACTIVE";
public const string UMPIRE_CONFLICT = "UMPIRE_CONFLICT";
public const string ASSIGNMENT_NOT_FOUND = "ASSIGNMENT_NOT_FOUND";
public const string INVALID_STATUS_TRANSITION = "INVALID_STATUS_TRANSITION";  // Reuse existing
public const string ALREADY_ASSIGNED = "ALREADY_ASSIGNED";
```

**Also update:** `src/lib/constants.js`

---

### Task 1.3: Create UmpireProfile Model
**File:** `api/Models/UmpireProfile.cs` (NEW)
**Effort:** 30 minutes

```csharp
namespace GameSwap.Functions.Models;

public class UmpireProfile
{
    public string LeagueId { get; set; } = default!;
    public string UmpireUserId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Phone { get; set; } = default!;
    public string? CertificationLevel { get; set; }
    public int? YearsExperience { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
```

**Dependencies:** None

---

### Task 1.4: Create Availability Rule Model
**File:** `api/Models/UmpireAvailability.cs` (NEW)
**Effort:** 20 minutes

```csharp
public class UmpireAvailability
{
    public string LeagueId { get; set; } = default!;
    public string UmpireUserId { get; set; } = default!;
    public string RuleId { get; set; } = default!;
    public string RuleType { get; set; } = default!;  // "Availability" or "Blackout"
    public string DateFrom { get; set; } = default!;
    public string DateTo { get; set; } = default!;
    public string? DaysOfWeek { get; set; }  // Phase 2: MVP uses null = all days
    public string? StartTime { get; set; }   // Phase 2: MVP uses null = all day
    public string? EndTime { get; set; }     // Phase 2: MVP uses null = all day
    public string? Reason { get; set; }
    public DateTime CreatedUtc { get; set; }
}
```

---

### Task 1.5: Create Game Umpire Assignment Model
**File:** `api/Models/GameUmpireAssignment.cs` (NEW)
**Effort:** 30 minutes

```csharp
public class GameUmpireAssignment
{
    public string LeagueId { get; set; } = default!;
    public string Division { get; set; } = default!;
    public string SlotId { get; set; } = default!;
    public string AssignmentId { get; set; } = default!;
    public string UmpireUserId { get; set; } = default!;
    public string? Position { get; set; }  // Phase 2 for multi-umpire
    public string Status { get; set; } = "Assigned";  // Assigned, Accepted, Declined, Cancelled

    // Denormalized game details (for umpire-scoped queries)
    public string GameDate { get; set; } = default!;
    public string StartTime { get; set; } = default!;
    public string EndTime { get; set; } = default!;
    public int StartMin { get; set; }
    public int EndMin { get; set; }
    public string FieldKey { get; set; } = default!;
    public string? FieldDisplayName { get; set; }
    public string? HomeTeamId { get; set; }
    public string? AwayTeamId { get; set; }

    public string AssignedBy { get; set; } = default!;
    public DateTime AssignedUtc { get; set; }
    public DateTime? ResponseUtc { get; set; }
    public string? DeclineReason { get; set; }
    public bool NoShowFlagged { get; set; } = false;
    public string? NoShowNotes { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
```

**Design Decision:** Denormalize game details into assignment table for fast umpire queries (don't need to join to Slots table).

---

### Task 1.6: Update Table Constants
**File:** `api/Storage/TableClients.cs`
**Effort:** 10 minutes

```csharp
public static class Tables
{
    // ... existing tables ...
    public const string UmpireProfiles = "GameSwapUmpireProfiles";
    public const string UmpireAvailability = "GameSwapUmpireAvailability";
    public const string GameUmpireAssignments = "GameSwapGameUmpireAssignments";
}
```

**Also:** Add table creation in `TableStartup.cs`

---

### Task 1.7: Create Repository Interfaces
**Files:** `api/Repositories/I*.cs` (NEW - 3 files)
**Effort:** 1 hour

**IUmpireProfileRepository:**
```csharp
Task<TableEntity?> GetUmpireAsync(string leagueId, string umpireUserId);
Task<List<TableEntity>> QueryUmpiresAsync(string leagueId, bool? activeOnly);
Task CreateUmpireAsync(TableEntity umpire);
Task UpdateUmpireAsync(TableEntity umpire, ETag etag);
```

**IUmpireAvailabilityRepository:**
```csharp
Task<List<TableEntity>> GetAvailabilityRulesAsync(string leagueId, string umpireUserId);
Task CreateAvailabilityRuleAsync(TableEntity rule);
Task DeleteAvailabilityRuleAsync(string leagueId, string umpireUserId, string ruleId);
```

**IGameUmpireAssignmentRepository:**
```csharp
Task<List<TableEntity>> GetAssignmentsByGameAsync(string leagueId, string division, string slotId);
Task<List<TableEntity>> GetAssignmentsByUmpireAsync(string leagueId, string umpireUserId, string? dateFrom, string? dateTo);
Task<TableEntity?> GetAssignmentAsync(string leagueId, string assignmentId);
Task CreateAssignmentAsync(TableEntity assignment);
Task UpdateAssignmentAsync(TableEntity assignment, ETag etag);
Task DeleteAssignmentAsync(string leagueId, string division, string slotId, string assignmentId);
```

---

### Task 1.8: Implement Repositories
**Files:** `api/Repositories/*.cs` (NEW - 3 files)
**Effort:** 3 hours

**Standard CRUD operations using Azure Table Storage.**

**Key patterns:**
- Partition key structures for efficient queries
- Use `ODataFilterBuilder` for filters
- Pagination support for large result sets
- ETag optimistic concurrency

**Dependencies:** Task 1.6 (table constants)

---

### Task 1.9: Update DI Registration
**File:** `api/Program.cs`
**Effort:** 10 minutes

```csharp
// Add repository registrations
services.AddScoped<IUmpireProfileRepository, UmpireProfileRepository>();
services.AddScoped<IUmpireAvailabilityRepository, UmpireAvailabilityRepository>();
services.AddScoped<IGameUmpireAssignmentRepository, GameUmpireAssignmentRepository>();

// Add service registrations (Task 2.x)
services.AddScoped<IUmpireService, UmpireService>();
services.AddScoped<IUmpireAssignmentService, UmpireAssignmentService>();
services.AddScoped<IUmpireAvailabilityService, UmpireAvailabilityService>();
```

---

### Phase 1 Deliverable:
✅ Data models defined
✅ Repositories implemented
✅ Constants updated
✅ DI configured
✅ No UI yet - foundation only

---

## PHASE 2: ADMIN ASSIGNMENT FLOW (Days 3-5, ~25 hours)

### Goal: Admin can assign umpires to games with conflict detection

**Outcome:** First vertical slice complete - umpire assignment works end-to-end

---

### Task 2.1: Create IUmpireService + Implementation
**Files:** `api/Services/IUmpireService.cs`, `UmpireService.cs` (NEW)
**Effort:** 2 hours

**Methods:**
```csharp
public interface IUmpireService
{
    Task<object> CreateUmpireAsync(CreateUmpireRequest request, CorrelationContext context);
    Task<object> GetUmpireAsync(string leagueId, string umpireUserId);
    Task<List<object>> QueryUmpiresAsync(string leagueId, UmpireQueryFilter filter);
    Task<object> UpdateUmpireAsync(string umpireUserId, UpdateUmpireRequest request, CorrelationContext context);
    Task DeactivateUmpireAsync(string umpireUserId, bool reassignFutureGames, CorrelationContext context);
}
```

**Business logic:**
- Authorization checks (LeagueAdmin required)
- Validation (email format, phone format)
- Active/inactive state management
- Deactivation → reassignment trigger

**Dependencies:** Task 1.8 (repositories)

---

### Task 2.2: Create IUmpireAssignmentService + Implementation
**Files:** `api/Services/IUmpireAssignmentService.cs`, `UmpireAssignmentService.cs` (NEW)
**Effort:** 4 hours

**CRITICAL METHODS:**

```csharp
// Core assignment
Task<object> AssignUmpireToGameAsync(AssignUmpireRequest request, CorrelationContext context);

// Conflict detection (REUSE team double-booking pattern)
Task<List<object>> CheckUmpireConflictsAsync(string umpireUserId, string gameDate, int startMin, int endMin, string? excludeSlotId);

// Umpire self-service
Task<object> UpdateAssignmentStatusAsync(string assignmentId, string newStatus, string? reason, CorrelationContext context);

// Admin management
Task RemoveAssignmentAsync(string assignmentId, CorrelationContext context);
Task<List<object>> GetUmpireAssignmentsAsync(string umpireUserId, AssignmentQueryFilter filter);
Task<List<object>> GetUnassignedGamesAsync(string leagueId, UnassignedGamesFilter filter);
```

**Key Implementation - AssignUmpireToGameAsync:**
```csharp
public async Task<object> AssignUmpireToGameAsync(AssignUmpireRequest request, CorrelationContext context)
{
    // 1. Validate umpire exists and is active
    var umpire = await _umpireRepo.GetUmpireAsync(request.LeagueId, request.UmpireUserId);
    if (umpire == null) throw new ApiGuards.HttpError(404, ErrorCodes.UMPIRE_NOT_FOUND, "Umpire not found");
    if (!(umpire.GetBoolean("IsActive") ?? false)) throw new ApiGuards.HttpError(400, ErrorCodes.UMPIRE_INACTIVE, "Umpire is inactive");

    // 2. Get game details
    var game = await _slotRepo.GetSlotAsync(request.LeagueId, request.Division, request.SlotId);
    if (game == null) throw new ApiGuards.HttpError(404, ErrorCodes.SLOT_NOT_FOUND, "Game not found");

    var gameDate = game.GetString("GameDate");
    var startTime = game.GetString("StartTime");
    var endTime = game.GetString("EndTime");
    var startMin = game.GetInt32("StartMin") ?? 0;
    var endMin = game.GetInt32("EndMin") ?? 0;

    // 3. CRITICAL: Check for umpire conflicts (double-booking prevention)
    var conflicts = await CheckUmpireConflictsAsync(request.UmpireUserId, gameDate, startMin, endMin, request.SlotId);
    if (conflicts.Any()) {
        throw new ApiGuards.HttpError(409, ErrorCodes.UMPIRE_CONFLICT,
            $"Umpire has conflicting assignment at {conflicts[0].StartTime}-{conflicts[0].EndTime} on {conflicts[0].Field}");
    }

    // 4. Create assignment
    var assignmentId = Guid.NewGuid().ToString("N");
    var assignment = new TableEntity($"UMPASSIGN|{request.LeagueId}|{request.Division}|{request.SlotId}", assignmentId)
    {
        ["LeagueId"] = request.LeagueId,
        ["Division"] = request.Division,
        ["SlotId"] = request.SlotId,
        ["AssignmentId"] = assignmentId,
        ["UmpireUserId"] = request.UmpireUserId,
        ["Status"] = "Assigned",
        ["AssignedBy"] = context.UserId,
        ["AssignedUtc"] = DateTimeOffset.UtcNow,
        // Denormalized game details
        ["GameDate"] = gameDate,
        ["StartTime"] = startTime,
        ["EndTime"] = endTime,
        ["StartMin"] = startMin,
        ["EndMin"] = endMin,
        ["FieldKey"] = game.GetString("FieldKey"),
        ["FieldDisplayName"] = game.GetString("DisplayName"),
        ["HomeTeamId"] = game.GetString("HomeTeamId"),
        ["AwayTeamId"] = game.GetString("AwayTeamId"),
        ["CreatedUtc"] = DateTime.UtcNow,
        ["UpdatedUtc"] = DateTime.UtcNow
    };

    await _assignmentRepo.CreateAssignmentAsync(assignment);

    // 5. Trigger notification (fire-and-forget)
    if (request.SendNotification) {
        _ = Task.Run(() => SendAssignmentNotificationAsync(request.UmpireUserId, assignment));
    }

    return MapAssignmentToDto(assignment);
}
```

**Dependencies:** Task 1.8 (repositories), Task 2.3 (slot repo access)

---

### Task 2.3: Update SlotService with Umpire Assignment Hook
**File:** `api/Services/SlotService.cs`
**Effort:** 1 hour

**When game is updated or cancelled:**
```csharp
// In UpdateSlotAsync or CancelSlotAsync
private async Task PropagateGameChangeToUmpireAssignmentsAsync(
    string leagueId,
    string division,
    string slotId,
    GameChangeDetails changes)
{
    var assignments = await _assignmentRepo.GetAssignmentsByGameAsync(leagueId, division, slotId);

    foreach (var assignment in assignments) {
        if (assignment.GetString("Status") == "Cancelled") continue;

        var umpireUserId = assignment.GetString("UmpireUserId");

        // If game cancelled
        if (changes.IsCancellation) {
            assignment["Status"] = "Cancelled";
            assignment["UpdatedUtc"] = DateTime.UtcNow;
            await _assignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);
            await NotifyUmpireGameCancelledAsync(umpireUserId, assignment);
            continue;
        }

        // If game rescheduled
        if (changes.DateOrTimeChanged) {
            // Check if umpire has conflict at new time
            var conflicts = await _umpireAssignmentService.CheckUmpireConflictsAsync(
                umpireUserId, changes.NewDate, changes.NewStartMin, changes.NewEndMin, slotId);

            if (conflicts.Any()) {
                // Unassign due to conflict
                assignment["Status"] = "Cancelled";
                assignment["DeclineReason"] = "Game rescheduled to time when umpire has conflict";
                await _assignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);
                await NotifyUmpireUnassignedDueToConflictAsync(umpireUserId, assignment, conflicts[0]);
            } else {
                // Update assignment with new game details
                assignment["GameDate"] = changes.NewDate;
                assignment["StartTime"] = changes.NewStartTime;
                assignment["EndTime"] = changes.NewEndTime;
                assignment["StartMin"] = changes.NewStartMin;
                assignment["EndMin"] = changes.NewEndMin;
                if (changes.FieldChanged) {
                    assignment["FieldKey"] = changes.NewFieldKey;
                    assignment["FieldDisplayName"] = changes.NewFieldDisplayName;
                }
                await _assignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);
                await NotifyUmpireGameChangedAsync(umpireUserId, assignment, changes);
            }
        }
    }
}
```

**Integration point:** Call this from existing `UpdateSlotAsync` and `CancelSlotAsync` methods.

**Dependencies:** Task 2.2 (assignment service)

---

### Task 2.4: Create Azure Functions for Umpire Admin APIs
**File:** `api/Functions/UmpireManagementFunctions.cs` (NEW)
**Effort:** 3 hours

**Endpoints:**
```csharp
[Function("CreateUmpire")]
[OpenApiOperation(operationId: "CreateUmpire", tags: new[] { "Umpires" })]
public async Task<HttpResponseData> CreateUmpire(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "umpires")] HttpRequestData req)

[Function("GetUmpires")]
[OpenApiOperation(operationId: "GetUmpires", tags: new[] { "Umpires" })]
public async Task<HttpResponseData> GetUmpires(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires")] HttpRequestData req)

[Function("UpdateUmpire")]
public async Task<HttpResponseData> UpdateUmpire(
    [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "umpires/{umpireUserId}")] HttpRequestData req,
    string umpireUserId)

[Function("DeactivateUmpire")]
public async Task<HttpResponseData> DeactivateUmpire(
    [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "umpires/{umpireUserId}")] HttpRequestData req,
    string umpireUserId)
```

**Standard pattern:**
- Extract league ID from header
- Get user identity
- Authorize (require LeagueAdmin)
- Call service
- Return standardized response

**Dependencies:** Task 2.1 (umpire service)

---

### Task 2.5: Create Azure Functions for Umpire Assignments
**File:** `api/Functions/UmpireAssignmentFunctions.cs` (NEW)
**Effort:** 3 hours

**Endpoints:**
```csharp
[Function("AssignUmpireToGame")]
[OpenApiOperation(operationId: "AssignUmpireToGame", tags: new[] { "UmpireAssignments" })]
public async Task<HttpResponseData> AssignUmpire(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "games/{division}/{slotId}/umpire-assignments")] HttpResponseData req,
    string division,
    string slotId)

[Function("GetGameUmpireAssignments")]
public async Task<HttpResponseData> GetGameAssignments(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "games/{division}/{slotId}/umpire-assignments")] HttpRequestData req,
    string division,
    string slotId)

[Function("UpdateAssignmentStatus")]
public async Task<HttpResponseData> UpdateStatus(
    [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "umpire-assignments/{assignmentId}/status")] HttpRequestData req,
    string assignmentId)

[Function("RemoveUmpireAssignment")]
public async Task<HttpResponseData> RemoveAssignment(
    [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "umpire-assignments/{assignmentId}")] HttpRequestData req,
    string assignmentId)

[Function("GetUnassignedGames")]
public async Task<HttpResponseData> GetUnassignedGames(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires/unassigned-games")] HttpRequestData req)
```

**Dependencies:** Task 2.2 (assignment service)

---

### Task 2.6: Create Admin Umpire Roster Section (Frontend)
**File:** `src/pages/admin/UmpireRosterSection.jsx` (NEW)
**Effort:** 3 hours

**Features:**
- Table of all umpires (sortable, searchable)
- "+ Add Umpire" button → modal
- Edit umpire → modal (reuse same component)
- Deactivate umpire → confirmation dialog
- Columns: Name, Certification, Phone, Email, Games This Week, Status, Actions

**Components:**
```jsx
function UmpireRosterSection({ leagueId }) {
  const [umpires, setUmpires] = useState([]);
  const [loading, setLoading] = useState(true);
  const [editingUmpire, setEditingUmpire] = useState(null);

  // Load umpires
  useEffect(() => {
    loadUmpires();
  }, [leagueId]);

  async function loadUmpires() {
    const data = await apiFetch('/api/umpires');
    setUmpires(data);
  }

  // Render table + modals
}
```

**Dependencies:** Task 2.4 (API endpoints)

---

### Task 2.7: Create Umpire Assignment Modal (Frontend)
**File:** `src/components/UmpireAssignModal.jsx` (NEW)
**Effort:** 4 hours

**CRITICAL COMPONENT** - Where conflict detection UI happens

```jsx
function UmpireAssignModal({ game, onClose, onAssigned }) {
  const [umpires, setUmpires] = useState([]);
  const [selectedUmpire, setSelectedUmpire] = useState(null);
  const [conflicts, setConflicts] = useState([]);
  const [checking, setChecking] = useState(false);

  // Load umpires on open
  useEffect(() => {
    loadUmpires();
  }, []);

  // Check conflicts when umpire selected
  useEffect(() => {
    if (selectedUmpire) {
      checkConflicts(selectedUmpire.umpireUserId);
    }
  }, [selectedUmpire]);

  async function checkConflicts(umpireUserId) {
    setChecking(true);
    const result = await apiFetch('/api/umpires/check-conflicts', {
      method: 'POST',
      body: JSON.stringify({
        umpireUserId,
        gameDate: game.gameDate,
        startTime: game.startTime,
        endTime: game.endTime,
        excludeSlotId: game.slotId
      })
    });
    setConflicts(result.conflicts || []);
    setChecking(false);
  }

  async function handleAssign() {
    if (conflicts.length > 0) {
      toast.error('Cannot assign - umpire has conflicting assignment');
      return;
    }

    await apiFetch(`/api/games/${game.division}/${game.slotId}/umpire-assignments`, {
      method: 'POST',
      body: JSON.stringify({
        umpireUserId: selectedUmpire.umpireUserId,
        sendNotification: true
      })
    });

    toast.success(`${selectedUmpire.name} assigned to game`);
    onAssigned();
    onClose();
  }

  return (
    <Modal>
      <h2>Assign Umpire to {game.homeTeamId} vs {game.awayTeamId}</h2>

      <Select
        options={umpires}
        value={selectedUmpire}
        onChange={setSelectedUmpire}
        getOptionLabel={(u) => u.name}
        placeholder="Select umpire..."
      />

      {checking && <div>Checking for conflicts...</div>}

      {conflicts.length > 0 && (
        <div className="alert alert-error">
          ⚠️ Conflict: {selectedUmpire.name} is assigned to another game
          {conflicts[0].startTime}-{conflicts[0].endTime} at {conflicts[0].field}
        </div>
      )}

      <div className="modal-actions">
        <button onClick={onClose}>Cancel</button>
        <button
          onClick={handleAssign}
          disabled={!selectedUmpire || conflicts.length > 0 || checking}
          className="btn btn-primary"
        >
          Assign Umpire
        </button>
      </div>
    </Modal>
  );
}
```

**Dependencies:** Task 2.5 (API), Task 2.8 (conflict check endpoint)

---

### Task 2.8: Create Conflict Check API Endpoint
**File:** `api/Functions/UmpireConflictFunctions.cs` (NEW)
**Effort:** 1 hour

```csharp
[Function("CheckUmpireConflicts")]
[OpenApiOperation(operationId: "CheckUmpireConflicts", tags: new[] { "UmpireAssignments" })]
public async Task<HttpResponseData> CheckConflicts(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "umpires/check-conflicts")] HttpRequestData req)
{
    var leagueId = ApiGuards.RequireLeagueId(req);
    var me = IdentityUtil.GetMe(req);
    await ApiGuards.RequireLeagueAdminAsync(_membershipRepo, me.UserId, leagueId);

    var body = await HttpUtil.ReadJsonAsync<CheckConflictRequest>(req);
    var conflicts = await _assignmentService.CheckUmpireConflictsAsync(
        body.UmpireUserId,
        body.GameDate,
        TimeUtil.ParseMinutes(body.StartTime),
        TimeUtil.ParseMinutes(body.EndTime),
        body.ExcludeSlotId);

    return ApiResponses.Ok(req, new { hasConflict = conflicts.Any(), conflicts });
}
```

**Use:** Called by frontend during umpire selection (real-time conflict preview)

---

### Task 2.9: Integrate Assignment into CalendarPage Game Detail
**File:** `src/pages/CalendarPage.jsx`
**Effort:** 2 hours

**Changes:**

1. When loading game details, fetch umpire assignment:
```jsx
async function loadGameWithUmpire(slotId) {
  const game = await apiFetch(`/api/slots/${division}/${slotId}`);
  const assignments = await apiFetch(`/api/games/${division}/${slotId}/umpire-assignments`);
  setSelectedGame({ ...game, umpireAssignments: assignments });
}
```

2. In game detail modal, add umpire section (conditional rendering):
```jsx
{isAdmin && (
  <div className="game-detail-section">
    <h3>Umpire Assignment</h3>
    {game.umpireAssignments?.length > 0 ? (
      <UmpireContactCard assignment={game.umpireAssignments[0]} showActions={isAdmin} />
    ) : (
      <button onClick={() => setShowAssignModal(true)}>+ Assign Umpire</button>
    )}
  </div>
)}

{showAssignModal && (
  <UmpireAssignModal
    game={selectedGame}
    onClose={() => setShowAssignModal(false)}
    onAssigned={() => reloadGame()}
  />
)}
```

**Dependencies:** Task 2.7 (modal), Task 2.5 (API)

---

### Task 2.10: Create Unassigned Games List
**File:** `src/pages/admin/UmpireAssignmentsSection.jsx` (NEW)
**Effort:** 2 hours

**Features:**
- Fetch `/api/umpires/unassigned-games`
- Table: Date | Time | Division | Teams | Field | Quick Assign
- Quick Assign: Inline dropdown + button
- Filter by division, date range
- Sort by date ascending (soonest first)
- Badge: Count of unassigned games

```jsx
function UmpireAssignmentsSection({ leagueId }) {
  const [unassignedGames, setUnassignedGames] = useState([]);

  async function quickAssign(game, umpireUserId) {
    await apiFetch(`/api/games/${game.division}/${game.slotId}/umpire-assignments`, {
      method: 'POST',
      body: JSON.stringify({ umpireUserId, sendNotification: true })
    });
    toast.success('Umpire assigned');
    loadUnassignedGames();  // Refresh
  }

  return (
    <div>
      <h2>Unassigned Games <span className="badge">{unassignedGames.length}</span></h2>
      <table>
        <thead>
          <tr>
            <th>Date</th>
            <th>Time</th>
            <th>Division</th>
            <th>Teams</th>
            <th>Field</th>
            <th>Quick Assign</th>
          </tr>
        </thead>
        <tbody>
          {unassignedGames.map(game => (
            <tr key={game.slotId}>
              <td>{game.gameDate}</td>
              <td>{game.startTime}</td>
              <td>{game.division}</td>
              <td>{game.homeTeamId} vs {game.awayTeamId}</td>
              <td>{game.fieldDisplayName}</td>
              <td>
                <UmpireQuickAssignDropdown game={game} onAssign={quickAssign} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
```

**Dependencies:** Task 2.5 (API)

---

### Task 2.11: Add Umpire Tab to Admin Page
**File:** `src/pages/AdminPage.jsx`
**Effort:** 30 minutes

```jsx
const ADMIN_TABS = {
  ACCESS: 'access',
  UMPIRES: 'umpires',  // NEW
  GLOBAL: 'global'
};

// In render:
{activeTab === ADMIN_TABS.UMPIRES && (
  <>
    <UmpireRosterSection leagueId={leagueId} />
    <UmpireAssignmentsSection leagueId={leagueId} />
  </>
)}
```

**Dependencies:** Tasks 2.6, 2.10

---

### Phase 2 Deliverable:
✅ Admin can create umpires
✅ Admin can assign umpires to games
✅ Conflict detection prevents double-booking
✅ Unassigned games list shows gaps
✅ Assignments visible in calendar
✅ Game changes propagate to assignments

**Ready for:** Internal testing of admin workflow

---

## PHASE 3: UMPIRE PORTAL (Days 6-8, ~20 hours)

### Goal: Umpires can view and respond to assignments

---

### Task 3.1: Create Umpire Dashboard API Endpoint
**File:** `api/Functions/UmpireSelfServiceFunctions.cs` (NEW)
**Effort:** 2 hours

```csharp
[Function("GetMyAssignments")]
[OpenApiOperation(operationId: "GetMyUmpireAssignments", tags: new[] { "UmpireSelfService" })]
public async Task<HttpResponseData> GetMyAssignments(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires/me/assignments")] HttpRequestData req)
{
    var leagueId = ApiGuards.RequireLeagueId(req);
    var me = IdentityUtil.GetMe(req);

    // Verify user is an umpire in this league
    var umpire = await _umpireRepo.GetUmpireAsync(leagueId, me.UserId);
    if (umpire == null) {
        return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
            "You are not registered as an umpire in this league");
    }

    var status = ApiGuards.GetQueryParam(req, "status");  // Assigned, Accepted, etc.
    var dateFrom = ApiGuards.GetQueryParam(req, "dateFrom");

    var assignments = await _assignmentService.GetUmpireAssignmentsAsync(me.UserId, new AssignmentQueryFilter {
        LeagueId = leagueId,
        Status = status,
        DateFrom = dateFrom
    });

    return ApiResponses.Ok(req, assignments);
}

[Function("GetMyDashboard")]
public async Task<HttpResponseData> GetMyDashboard(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "umpires/me/dashboard")] HttpRequestData req)
{
    var leagueId = ApiGuards.RequireLeagueId(req);
    var me = IdentityUtil.GetMe(req);

    var umpire = await _umpireRepo.GetUmpireAsync(leagueId, me.UserId);
    if (umpire == null) {
        return ApiResponses.Error(req, HttpStatusCode.Forbidden, ErrorCodes.FORBIDDEN,
            "Not registered as umpire");
    }

    // Get summary counts
    var allAssignments = await _assignmentService.GetUmpireAssignmentsAsync(me.UserId, new AssignmentQueryFilter {
        LeagueId = leagueId,
        DateFrom = DateTime.UtcNow.ToString("yyyy-MM-dd")
    });

    var pending = allAssignments.Count(a => a.Status == "Assigned");
    var upcoming = allAssignments.Count(a => a.Status == "Accepted");

    var dashboard = new {
        umpire = MapUmpireToDto(umpire),
        pendingCount = pending,
        upcomingCount = upcoming,
        thisWeek = allAssignments.Count(a => IsThisWeek(a.GameDate)),
        thisMonth = allAssignments.Count(a => IsThisMonth(a.GameDate))
    };

    return ApiResponses.Ok(req, dashboard);
}
```

---

### Task 3.2: Create Umpire Dashboard Page (Frontend)
**File:** `src/pages/UmpireDashboard.jsx` (NEW)
**Effort:** 4 hours

```jsx
export default function UmpireDashboard({ leagueId, me }) {
  const [dashboard, setDashboard] = useState(null);
  const [pendingAssignments, setPendingAssignments] = useState([]);
  const [upcomingAssignments, setUpcomingAssignments] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadDashboard();
    loadAssignments();
  }, [leagueId]);

  async function loadDashboard() {
    const data = await apiFetch('/api/umpires/me/dashboard');
    setDashboard(data);
  }

  async function loadAssignments() {
    const pending = await apiFetch('/api/umpires/me/assignments?status=Assigned');
    const upcoming = await apiFetch('/api/umpires/me/assignments?status=Accepted');
    setPendingAssignments(pending);
    setUpcomingAssignments(upcoming);
    setLoading(false);
  }

  async function handleAccept(assignmentId) {
    await apiFetch(`/api/umpire-assignments/${assignmentId}/status`, {
      method: 'PATCH',
      body: JSON.stringify({ status: 'Accepted' })
    });
    toast.success('Assignment accepted');
    loadAssignments();  // Refresh
  }

  async function handleDecline(assignmentId, reason) {
    await apiFetch(`/api/umpire-assignments/${assignmentId}/status`, {
      method: 'PATCH',
      body: JSON.stringify({ status: 'Declined', declineReason: reason })
    });
    toast.success('Assignment declined. Admin notified.');
    loadAssignments();
  }

  if (loading) return <StatusCard title="Loading..." />;

  return (
    <div className="umpire-dashboard">
      {/* Stats Cards */}
      <div className="stats-row">
        <StatCard title="Pending" count={dashboard.pendingCount} icon="⏳" />
        <StatCard title="This Week" count={dashboard.thisWeek} icon="📅" />
        <StatCard title="This Month" count={dashboard.thisMonth} icon="📊" />
      </div>

      {/* Pending Assignments (highlighted) */}
      {pendingAssignments.length > 0 && (
        <section className="section-pending">
          <h2>Pending Assignments</h2>
          {pendingAssignments.map(assignment => (
            <UmpireAssignmentCard
              key={assignment.assignmentId}
              assignment={assignment}
              onAccept={() => handleAccept(assignment.assignmentId)}
              onDecline={(reason) => handleDecline(assignment.assignmentId, reason)}
              showActions={true}
            />
          ))}
        </section>
      )}

      {/* Upcoming Confirmed */}
      <section className="section-upcoming">
        <h2>Upcoming Games</h2>
        {upcomingAssignments.length === 0 ? (
          <p>No upcoming assignments</p>
        ) : (
          upcomingAssignments.map(assignment => (
            <UmpireAssignmentCard
              key={assignment.assignmentId}
              assignment={assignment}
              showActions={false}
            />
          ))
        )}
      </section>
    </div>
  );
}
```

---

### Task 3.3: Create Umpire Assignment Card Component
**File:** `src/components/UmpireAssignmentCard.jsx` (NEW)
**Effort:** 2 hours

```jsx
export default function UmpireAssignmentCard({ assignment, onAccept, onDecline, showActions }) {
  const [showDeclineModal, setShowDeclineModal] = useState(false);

  return (
    <div className={`assignment-card assignment-card--${assignment.status.toLowerCase()}`}>
      <div className="assignment-header">
        <div className="assignment-teams">
          {assignment.homeTeamId} vs {assignment.awayTeamId}
        </div>
        <span className={`badge badge-${getStatusColor(assignment.status)}`}>
          {assignment.status}
        </span>
      </div>

      <div className="assignment-details">
        <div className="detail-row">
          <span className="icon">📅</span>
          <span>{formatDate(assignment.gameDate)}</span>
        </div>
        <div className="detail-row">
          <span className="icon">🕒</span>
          <span>{assignment.startTime} - {assignment.endTime}</span>
        </div>
        <div className="detail-row">
          <span className="icon">📍</span>
          <span>{assignment.fieldDisplayName || assignment.fieldKey}</span>
        </div>
      </div>

      {showActions && assignment.status === 'Assigned' && (
        <div className="assignment-actions">
          <button onClick={onAccept} className="btn btn-success">
            ✓ Accept
          </button>
          <button onClick={() => setShowDeclineModal(true)} className="btn btn-secondary">
            ✗ Decline
          </button>
        </div>
      )}

      {showDeclineModal && (
        <DeclineAssignmentModal
          assignment={assignment}
          onConfirm={(reason) => {
            onDecline(reason);
            setShowDeclineModal(false);
          }}
          onCancel={() => setShowDeclineModal(false)}
        />
      )}
    </div>
  );
}
```

---

### Task 3.4: Add Umpire Tab to TopNav
**File:** `src/components/TopNav.jsx`
**Effort:** 30 minutes

```jsx
// Add umpire role check
const isUmpire = me && memberships?.some(m =>
  m.leagueId === leagueId && m.role === ROLE.UMPIRE
);

// Add tab
{isUmpire && (
  <button
    className={effectiveTab === 'umpire' ? 'nav-tab nav-tab--active' : 'nav-tab'}
    onClick={() => navigateTo('#umpire')}
    aria-current={effectiveTab === 'umpire' ? 'page' : undefined}
  >
    Umpire
    {pendingAssignmentsCount > 0 && (
      <span className="badge badge-notification">{pendingAssignmentsCount}</span>
    )}
  </button>
)}
```

**Dependencies:** Task 3.1 (API for pending count)

---

### Task 3.5: Wire Up Umpire Tab in App.jsx
**File:** `src/App.jsx`
**Effort:** 30 minutes

```jsx
import UmpireDashboard from './pages/UmpireDashboard';

// In lazy loading section:
const UmpirePage = lazy(() => import('./pages/UmpireDashboard'));

// In tab rendering:
{effectiveTab === 'umpire' && (
  <Suspense fallback={<StatusCard title="Loading..." />}>
    <UmpirePage leagueId={leagueId} me={me} />
  </Suspense>
)}
```

---

### Task 3.6: Create Basic Availability Manager (MVP: All-Day Rules Only)
**File:** `src/pages/UmpireAvailability.jsx` (NEW)
**Effort:** 3 hours

**MVP Scope:** Date range only, no time-specific or day-of-week filters

```jsx
export default function UmpireAvailability({ leagueId, me }) {
  const [rules, setRules] = useState([]);
  const [showAddModal, setShowAddModal] = useState(false);

  async function loadRules() {
    const data = await apiFetch('/api/umpires/availability');
    setRules(data);
  }

  async function addRule(ruleData) {
    await apiFetch('/api/umpires/availability', {
      method: 'POST',
      body: JSON.stringify(ruleData)
    });
    toast.success('Availability saved');
    loadRules();
  }

  async function deleteRule(ruleId) {
    if (!confirm('Remove this availability rule?')) return;
    await apiFetch(`/api/umpires/availability/${ruleId}`, { method: 'DELETE' });
    loadRules();
  }

  return (
    <div className="availability-manager">
      <h1>My Availability</h1>

      <button onClick={() => setShowAddModal(true)} className="btn btn-primary">
        + Add Availability Window
      </button>

      <div className="rules-list">
        {rules.map(rule => (
          <div key={rule.ruleId} className="rule-card">
            <div className="rule-header">
              <span className={`rule-type rule-type--${rule.ruleType.toLowerCase()}`}>
                {rule.ruleType}
              </span>
              <button onClick={() => deleteRule(rule.ruleId)} className="btn-icon">🗑️</button>
            </div>
            <div className="rule-details">
              {rule.dateFrom} to {rule.dateTo}
              {rule.reason && <div className="rule-reason">{rule.reason}</div>}
            </div>
          </div>
        ))}
      </div>

      {showAddModal && (
        <AddAvailabilityModal
          onSave={addRule}
          onClose={() => setShowAddModal(false)}
        />
      )}
    </div>
  );
}
```

---

### Phase 3 Deliverable:
✅ Umpire can log in and see assignments
✅ Umpire can accept/decline assignments
✅ Umpire can set basic availability (date ranges)
✅ Admin sees umpire responses

**Ready for:** Umpire beta testing

---

## PHASE 4: COACH INTEGRATION (Day 9, ~8 hours)

### Goal: Coaches see umpire info for their games

---

### Task 4.1: Enhance Game Detail Modal - Coach View
**File:** `src/pages/CalendarPage.jsx` (already modified in Task 2.9)
**Effort:** 2 hours

**Add coach-specific umpire section:**
```jsx
{isCoach && game.umpireAssignments?.length > 0 && (
  <div className="game-detail-section">
    <h3>Game Official</h3>
    {game.umpireAssignments.map(assignment => (
      <UmpireContactCard
        key={assignment.assignmentId}
        assignment={assignment}
        showContact={true}
        showActions={false}
      />
    ))}
  </div>
)}

{isCoach && (!game.umpireAssignments || game.umpireAssignments.length === 0) && (
  <div className="game-detail-section">
    <h3>Game Official</h3>
    <div className="alert alert-warning">
      ⚠️ No umpire assigned yet. Contact your league admin if needed.
    </div>
  </div>
)}
```

---

### Task 4.2: Create Umpire Contact Card Component
**File:** `src/components/UmpireContactCard.jsx` (NEW)
**Effort:** 2 hours

```jsx
export default function UmpireContactCard({ assignment, umpire, showContact, showActions }) {
  const statusBadge = {
    'Assigned': { icon: '⏳', text: 'Pending', class: 'warning' },
    'Accepted': { icon: '✓', text: 'Confirmed', class: 'success' },
    'Declined': { icon: '✗', text: 'Declined', class: 'error' },
    'Cancelled': { icon: '🚫', text: 'Cancelled', class: 'secondary' }
  }[assignment.status] || { icon: '', text: assignment.status, class: '' };

  return (
    <div className="umpire-contact-card">
      {umpire?.photoUrl && (
        <img src={umpire.photoUrl} alt={umpire.name} className="umpire-photo" />
      )}

      <div className="umpire-info">
        <div className="umpire-name">{umpire?.name || 'Loading...'}</div>

        <span className={`badge badge-${statusBadge.class}`}>
          {statusBadge.icon} {statusBadge.text}
        </span>

        {showContact && assignment.status === 'Accepted' && umpire && (
          <div className="umpire-contact-actions">
            {umpire.phone && (
              <a href={`tel:${umpire.phone}`} className="btn btn-sm btn-outline">
                📞 {formatPhone(umpire.phone)}
              </a>
            )}
            {umpire.email && (
              <a href={`mailto:${umpire.email}`} className="btn btn-sm btn-outline">
                ✉️ Email
              </a>
            )}
          </div>
        )}

        {assignment.status === 'Assigned' && (
          <div className="umpire-pending-note">
            Waiting for confirmation
          </div>
        )}
      </div>

      {showActions && (
        <div className="umpire-actions">
          <button onClick={onReassign} className="btn btn-sm">Reassign</button>
          <button onClick={onRemove} className="btn btn-sm btn-danger">Remove</button>
        </div>
      )}
    </div>
  );
}
```

**Used by:** Coaches (game detail), Admins (game detail), Umpires (dashboard)

---

### Task 4.3: Update CalendarPage to Load Umpire Data
**File:** `src/pages/CalendarPage.jsx`
**Effort:** 1 hour

**When loading game details:**
```jsx
async function loadGameDetails(slotId) {
  const game = await apiFetch(`/api/slots/${division}/${slotId}`);

  // Fetch umpire assignment (parallel with game fetch for performance)
  const [assignments, umpireDetails] = await Promise.all([
    apiFetch(`/api/games/${division}/${slotId}/umpire-assignments`),
    // For each assignment, fetch umpire profile (or do this server-side)
    fetchUmpireProfiles(assignments)
  ]);

  setSelectedGame({
    ...game,
    umpireAssignments: assignments.map((a, idx) => ({
      ...a,
      umpire: umpireDetails[idx]
    }))
  });
}
```

**Optimization:** Consider server-side join (return umpire details with assignment) to reduce API calls.

---

### Task 4.4: Add Umpire Assignment Indicator on Calendar Cards
**File:** `src/components/CalendarView.jsx`
**Effort:** 1 hour

**Add small badge to game cards:**
```jsx
<div className="game-card">
  {/* existing game info */}

  {game.hasUmpireAssignment && (
    <div className="game-badge">
      {game.umpireStatus === 'Accepted' && <span className="badge-umpire badge-success">✓ Umpire</span>}
      {game.umpireStatus === 'Assigned' && <span className="badge-umpire badge-warning">⏳ Umpire</span>}
      {!game.umpireStatus && <span className="badge-umpire badge-error">⚠️ No Umpire</span>}
    </div>
  )}
</div>
```

**Requires:** Update `/api/slots` endpoint to include `hasUmpireAssignment` and `umpireStatus` virtual fields

---

### Task 4.5: Create Helper API for Game List with Umpire Status
**File:** `api/Functions/GetSlots.cs` (MODIFY)
**Effort:** 2 hours

**Enhance existing GetSlots to optionally include umpire assignment status:**

```csharp
// Add query param: ?includeUmpireStatus=true
var includeUmpireStatus = ApiGuards.GetQueryParam(req, "includeUmpireStatus") == "true";

var slotsWithUmpires = new List<object>();
foreach (var slot in result.Items) {
    var slotDto = MapSlotToDto(slot);

    if (includeUmpireStatus) {
        var assignments = await _assignmentRepo.GetAssignmentsByGameAsync(
            leagueId, slot.GetString("Division"), slot.RowKey);

        slotDto["hasUmpireAssignment"] = assignments.Any();
        slotDto["umpireStatus"] = assignments.FirstOrDefault()?.GetString("Status");
        slotDto["umpireName"] = assignments.Any()
            ? await GetUmpireNameAsync(assignments[0].GetString("UmpireUserId"))
            : null;
    }

    slotsWithUmpires.Add(slotDto);
}
```

**Performance:** This adds N+1 queries. Phase 2 optimization: Denormalize umpire status into GameSwapSlots table OR use batch query.

---

### Phase 4 Deliverable:
✅ Coaches see umpire name + contact on game detail
✅ Coaches see assignment status (confirmed/pending/unfilled)
✅ Calendar shows umpire status badges
✅ One-tap call/email for coaches

**Ready for:** Coach user testing

---

## PHASE 5: NOTIFICATIONS & POLISH (Days 10-11, ~18 hours)

### Goal: Email notifications, edge case handling

---

### Task 5.1: Create Umpire Assignment Email Template
**File:** `api/Services/EmailTemplates/UmpireAssignmentEmail.cs` (NEW)
**Effort:** 2 hours

**Template:**
```html
<h2>Game Assignment</h2>

<p>Hi {UmpireName},</p>

<p>You've been assigned to officiate:</p>

<div style="background: #f5f5f5; padding: 20px; border-radius: 8px;">
  <h3>{HomeTeamId} vs {AwayTeamId}</h3>
  <p><strong>{DayOfWeek}, {GameDate}</strong></p>
  <p><strong>{StartTime} - {EndTime}</strong></p>
  <p>{FieldDisplayName}</p>
  <p>{FieldAddress}</p>
  <p><a href="{MapLink}">📍 Open in Maps</a></p>
</div>

<p>
  <a href="{PortalLink}" style="background: #007bff; color: white; padding: 12px 24px; text-decoration: none; border-radius: 4px;">
    View Assignment & Respond
  </a>
</p>

<p><a href="{AddToCalendarLink}">📅 Add to Calendar</a></p>

<p>To accept or decline this assignment, log in to your umpire portal.</p>
```

**Also create templates for:**
- Game changed
- Game cancelled
- Assignment removed

---

### Task 5.2: Implement Email Notification Methods
**File:** `api/Services/UmpireNotificationService.cs` (NEW)
**Effort:** 3 hours

```csharp
public class UmpireNotificationService
{
    private readonly IEmailService _emailService;
    private readonly INotificationService _inAppNotificationService;

    public async Task SendAssignmentNotificationAsync(string umpireUserId, GameUmpireAssignment assignment)
    {
        // In-app notification
        await _inAppNotificationService.CreateNotificationAsync(
            umpireUserId,
            assignment.LeagueId,
            "UmpireAssigned",
            $"You've been assigned to {assignment.HomeTeamId} vs {assignment.AwayTeamId} on {assignment.GameDate} at {assignment.StartTime}",
            "#umpire",
            assignment.AssignmentId,
            "UmpireAssignment");

        // Email notification
        var umpire = await GetUmpireAsync(assignment.LeagueId, umpireUserId);
        var emailBody = await RenderAssignmentEmailTemplate(assignment, umpire);

        await _emailService.SendEmailAsync(
            to: umpire.Email,
            subject: $"Game Assignment - {assignment.HomeTeamId} vs {assignment.AwayTeamId}",
            body: emailBody);
    }

    public async Task SendGameChangedNotificationAsync(string umpireUserId, GameUmpireAssignment assignment, GameChangeDetails changes)
    {
        // Similar pattern
    }

    public async Task SendGameCancelledNotificationAsync(string umpireUserId, GameUmpireAssignment assignment)
    {
        // Similar pattern
    }
}
```

**Dependencies:** Existing `IEmailService`, Task 5.1 (templates)

---

### Task 5.3: Wire Notifications into Assignment Service
**File:** `api/Services/UmpireAssignmentService.cs`
**Effort:** 1 hour

**After creating assignment:**
```csharp
await _assignmentRepo.CreateAssignmentAsync(assignment);

// Trigger notifications (fire-and-forget)
if (request.SendNotification) {
    _ = Task.Run(async () => {
        try {
            await _notificationService.SendAssignmentNotificationAsync(request.UmpireUserId, assignment);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to send umpire assignment notification for {AssignmentId}", assignmentId);
        }
    });
}
```

**Also trigger notifications on:**
- Umpire accepts → Notify admin
- Umpire declines → Notify admin (email + in-app)
- Assignment removed → Notify umpire

---

### Task 5.4: Handle Edge Case - Umpire Deactivated Mid-Season
**File:** `api/Services/UmpireService.cs`
**Effort:** 2 hours

**In DeactivateUmpireAsync:**
```csharp
public async Task DeactivateUmpireAsync(string umpireUserId, bool reassignFutureGames, CorrelationContext context)
{
    // 1. Set umpire inactive
    var umpire = await _umpireRepo.GetUmpireAsync(context.LeagueId, umpireUserId);
    umpire["IsActive"] = false;
    umpire["UpdatedUtc"] = DateTime.UtcNow;
    await _umpireRepo.UpdateUmpireAsync(umpire, umpire.ETag);

    // 2. If reassignFutureGames == true, cancel all future assignments
    if (reassignFutureGames) {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var futureAssignments = await _assignmentRepo.GetAssignmentsByUmpireAsync(
            context.LeagueId, umpireUserId, dateFrom: today, dateTo: null);

        foreach (var assignment in futureAssignments) {
            if (assignment.GetString("Status") == "Cancelled") continue;

            assignment["Status"] = "Cancelled";
            assignment["DeclineReason"] = "Umpire deactivated";
            assignment["UpdatedUtc"] = DateTime.UtcNow;

            await _assignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);

            // Notify umpire (optional - they're deactivated)
            // Notify admin that game is now unassigned
            await _inAppNotificationService.CreateNotificationAsync(
                context.UserId,  // Admin who deactivated
                context.LeagueId,
                "UmpireDeactivated",
                $"{umpire.GetString("Name")} deactivated. {futureAssignments.Count} future games returned to unassigned.",
                "#admin",
                umpireUserId,
                "UmpireManagement");
        }
    }
}
```

---

### Task 5.5: Handle Edge Case - Umpire Declines Assignment
**File:** `api/Services/UmpireAssignmentService.cs`
**Effort:** 1 hour

**In UpdateAssignmentStatusAsync:**
```csharp
if (newStatus == "Declined") {
    // Update assignment
    assignment["Status"] = "Declined";
    assignment["ResponseUtc"] = DateTimeOffset.UtcNow;
    assignment["DeclineReason"] = reason ?? "";
    assignment["UpdatedUtc"] = DateTime.UtcNow;

    await _assignmentRepo.UpdateAssignmentAsync(assignment, assignment.ETag);

    // Notify admin
    var game = await _slotRepo.GetSlotAsync(assignment.LeagueId, assignment.Division, assignment.SlotId);
    var gameDesc = $"{game.GetString("HomeTeamId")} vs {game.GetString("AwayTeamId")} on {assignment.GameDate}";

    // Notify all league admins
    var admins = await _membershipRepo.GetLeagueAdminsAsync(assignment.LeagueId);
    foreach (var admin in admins) {
        await _notificationService.CreateNotificationAsync(
            admin.PartitionKey,
            assignment.LeagueId,
            "UmpireDeclined",
            $"{umpire.Name} declined {gameDesc}. Reason: {reason ?? "No reason provided"}",
            "#admin",
            assignment.AssignmentId,
            "UmpireAssignment");
    }

    // Send email to admin with details
    await SendDeclineEmailToAdminsAsync(assignment, umpire, reason);
}
```

---

### Task 5.6: Create Decline Assignment Modal (Frontend)
**File:** `src/components/DeclineAssignmentModal.jsx` (NEW)
**Effort:** 1 hour

```jsx
export default function DeclineAssignmentModal({ assignment, onConfirm, onCancel }) {
  const [reason, setReason] = useState('');

  function handleConfirm() {
    onConfirm(reason.trim());
  }

  return (
    <Modal>
      <h2>Decline Assignment</h2>
      <p>Are you sure you want to decline this game?</p>

      <div className="game-summary">
        <strong>{assignment.homeTeamId} vs {assignment.awayTeamId}</strong>
        <div>{assignment.gameDate} at {assignment.startTime}</div>
      </div>

      <label>
        Reason (optional):
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Why are you declining? (Helps admin with reassignment)"
          rows={3}
        />
      </label>

      <div className="modal-actions">
        <button onClick={onCancel} className="btn">Cancel</button>
        <button onClick={handleConfirm} className="btn btn-danger">Confirm Decline</button>
      </div>
    </Modal>
  );
}
```

---

### Phase 5 Deliverable:
✅ Email notifications sent on assignment
✅ Admin notified when umpire accepts/declines
✅ Edge cases handled (deactivation, declination)
✅ Umpire deactivation reassigns future games

---

## PHASE 6: TESTING & DEPLOYMENT (Day 12, ~9 hours)

### Goal: Comprehensive testing, documentation, deployment

---

### Task 6.1: Write Unit Tests for Umpire Services
**Files:** `api/GameSwap.Tests/Services/Umpire*.cs` (NEW - 3 files)
**Effort:** 4 hours

**Test suites:**

**UmpireServiceTests.cs:**
- Create umpire (success, validation errors)
- Update umpire (authorized, unauthorized)
- Deactivate umpire with reassignment
- Query umpires (active only, all)

**UmpireAssignmentServiceTests.cs (CRITICAL):**
- Assign umpire (success)
- Assign umpire with conflict (blocked)
- Accept assignment (umpire auth, status transition)
- Decline assignment (umpire auth, admin notified)
- Remove assignment (admin auth)
- Check conflicts (overlapping times, same day, different days)

**UmpireAvailabilityServiceTests.cs:**
- Create availability rule
- Check if umpire available (rule matches, rule doesn't match)
- Blackout overrides availability

**Critical test scenarios:**
```csharp
[Fact]
public async Task AssignUmpire_HasConflict_ThrowsUmpireConflictError()
{
    // Arrange: Umpire already assigned 3pm-5pm
    var existing = CreateAssignment("3pm", "5pm");
    _mockRepo.Setup(x => x.GetAssignmentsByUmpireAsync(...)).ReturnsAsync([existing]);

    // Act: Try to assign same umpire 4pm-6pm (overlaps)
    var ex = await Assert.ThrowsAsync<ApiGuards.HttpError>(() =>
        _service.AssignUmpireToGameAsync(new AssignRequest { ... }));

    // Assert
    Assert.Equal(409, ex.Status);
    Assert.Equal(ErrorCodes.UMPIRE_CONFLICT, ex.Code);
}
```

---

### Task 6.2: Write Frontend Tests
**Files:** `src/__tests__/pages/UmpireDashboard.test.jsx`, etc. (NEW - 3 files)
**Effort:** 2 hours

**Test suites:**
- UmpireDashboard: Loads assignments, accept/decline workflows
- UmpireAssignModal: Conflict detection, assignment flow
- UmpireContactCard: Rendering variants (confirmed, pending, unfilled)

**Example:**
```jsx
describe('UmpireDashboard', () => {
  it('shows pending assignments with accept/decline buttons', async () => {
    const mockAssignments = [
      { assignmentId: '1', status: 'Assigned', homeTeamId: 'Tigers', awayTeamId: 'Lions' }
    ];

    global.fetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ data: { pendingCount: 1, thisWeek: 3 } })
    });
    global.fetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ data: mockAssignments })
    });

    render(<UmpireDashboard leagueId="league-1" me={{ userId: 'umpire-1' }} />);

    await waitFor(() => {
      expect(screen.getByText('Tigers vs Lions')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /accept/i })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /decline/i })).toBeInTheDocument();
    });
  });
});
```

---

### Task 6.3: Add OpenAPI Documentation
**Files:** All function files
**Effort:** 1 hour

**Add OpenAPI attributes to all umpire endpoints:**
```csharp
[OpenApiOperation(operationId: "AssignUmpire", tags: new[] { "UmpireAssignments" })]
[OpenApiRequestBody("application/json", typeof(AssignUmpireRequest))]
[OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(AssignmentDto))]
[OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Conflict, Description = "Umpire has conflicting assignment")]
```

**Regenerate API client:**
```bash
node scripts/generate-api-client.js
```

---

### Task 6.4: Update CLAUDE.md with Umpire Module
**File:** `CLAUDE.md`
**Effort:** 30 minutes

**Add section:**
```markdown
## Umpire Management Module

### Umpire Role
New user role: `ROLE.UMPIRE`
- Self-service portal: `#umpire` tab
- Can view own assignments, accept/decline, set availability
- Cannot manage games, teams, or other umpires

### Umpire Assignment Workflow
1. Admin assigns umpire via calendar or unassigned games list
2. Conflict detection prevents double-booking
3. Email notification sent to umpire
4. Umpire accepts/declines in portal
5. Admin sees status in calendar

### Key Services
- `IUmpireService` - Roster management
- `IUmpireAssignmentService` - Assignment logic, conflict detection
- `IUmpireAvailabilityService` - Availability rules

### Critical Patterns
- Conflict detection reuses `TimeUtil.Overlaps` (same as team double-booking)
- Assignment denormalizes game details for umpire-scoped queries
- Game reschedule propagates to assignments via `SlotService` hook
- Best-effort notification (failures logged, don't block assignment)

### Tables
- `GameSwapUmpireProfiles` - Umpire roster
- `GameSwapUmpireAvailability` - Availability rules
- `GameSwapGameUmpireAssignments` - Game assignments
```

---

### Task 6.5: Create Behavioral Contract
**File:** `docs/UMPIRE_ASSIGNMENT_BEHAVIORAL_CONTRACT.md` (NEW)
**Effort:** 1 hour

**Document:**
- Assignment lifecycle (Assigned → Accepted/Declined/Cancelled)
- Conflict detection rules
- Double-booking prevention logic
- Game change propagation rules
- Notification triggers
- Best-effort semantics
- MVP scope vs Phase 2

---

### Task 6.6: Manual Testing Checklist
**Effort:** 2 hours

**Test scenarios:**
- [ ] Create umpire profile
- [ ] Assign umpire to game (no conflict)
- [ ] Assign umpire to game (has conflict) - should block
- [ ] Umpire logs in, sees assignment
- [ ] Umpire accepts assignment
- [ ] Umpire declines assignment
- [ ] Admin sees declined game in unassigned list
- [ ] Assign different umpire to declined game
- [ ] Reschedule game - umpire receives notification
- [ ] Reschedule game to time umpire has conflict - unassigned automatically
- [ ] Cancel game - umpire receives cancellation email
- [ ] Coach views own game - sees umpire contact
- [ ] Coach views other team's game - doesn't see umpire contact (privacy)
- [ ] Deactivate umpire - future games reassigned
- [ ] Umpire sets availability window
- [ ] Assignment to umpire outside availability shows warning (but allows override)

---

### Phase 6 Deliverable:
✅ All unit tests passing
✅ Frontend tests passing
✅ Manual testing complete
✅ Documentation updated
✅ OpenAPI spec regenerated

**Ready for:** Staging deployment

---

## CRITICAL PATH & DEPENDENCIES

### Dependency Graph

```
Task 1.1-1.9 (Foundation)
    ↓
Task 2.1 (UmpireService) ←─┐
    ↓                       │
Task 2.2 (AssignmentService)│
    ↓                       │
Task 2.3 (SlotService hook) │
    ↓                       │
Task 2.4-2.5 (APIs) ────────┘
    ↓
Task 2.6-2.11 (Admin UI)
    ↓
Task 3.1 (Umpire APIs)
    ↓
Task 3.2-3.6 (Umpire Portal)
    ↓
Task 4.1-4.5 (Coach Integration)
    ↓
Task 5.1-5.6 (Notifications & Edge Cases)
    ↓
Task 6.1-6.6 (Testing & Deployment)
```

**Critical Path:** Tasks 1.x → 2.1-2.3 → 2.4-2.5 → 3.1-3.2 → 6.1-6.6

**Parallelization Opportunities:**
- Task 2.6-2.11 (Admin UI) can be built in parallel with Task 3.1-3.6 (Umpire Portal) after APIs done
- Task 6.1 (unit tests) can start after Task 2.2 (service logic) complete

---

## SPRINT BREAKDOWN (3-week delivery)

### Sprint 1 (Week 1): Foundation + Admin Assignment
**Focus:** Backend infrastructure + admin workflow

**Goals:**
- ✅ Data models and repositories complete
- ✅ Admin can assign umpires to games
- ✅ Conflict detection working
- ✅ Unassigned games list functional

**Tasks:** 1.1-1.9, 2.1-2.5, 2.8, 2.10-2.11

**Deliverable:** Admin can assign (no umpire portal yet)

---

### Sprint 2 (Week 2): Umpire Portal + Coach View
**Focus:** Self-service for umpires + read-only coach view

**Goals:**
- ✅ Umpire can log in and see assignments
- ✅ Umpire can accept/decline
- ✅ Coaches see umpire info on game detail
- ✅ Basic availability manager

**Tasks:** 3.1-3.6, 4.1-4.5

**Deliverable:** End-to-end workflow complete (admin assigns → umpire responds → coach sees)

---

### Sprint 3 (Week 3): Notifications + Testing + Polish
**Focus:** Notification system + comprehensive testing

**Goals:**
- ✅ Email notifications on all key events
- ✅ Edge cases handled (deactivation, game changes)
- ✅ Full test coverage
- ✅ Documentation complete

**Tasks:** 5.1-5.6, 6.1-6.6, polish UI, deploy to staging

**Deliverable:** Production-ready MVP

---

## RISK MITIGATION

| Risk | Mitigation Strategy |
|------|---------------------|
| **Conflict detection bugs** | Reuse existing TimeUtil.Overlaps; comprehensive unit tests; manual testing matrix |
| **N+1 query performance** | Accept in MVP; Phase 2 optimization with denormalization |
| **Email deliverability** | Use existing SendGrid; fallback: in-app only; delivery status tracking |
| **Umpire adoption** | Simple onboarding; email links work without login; admin can accept on behalf |
| **Scope creep** | Strict MVP definition in this plan; defer all Phase 2 items; timebox each task |

---

## TESTING STRATEGY

### Unit Tests (Backend)
- [ ] Conflict detection logic (all time overlap scenarios)
- [ ] Assignment state transitions (Assigned → Accepted/Declined)
- [ ] Authorization checks (umpire self-service scoped to own data)
- [ ] Game propagation logic (reschedule updates assignments)
- [ ] Deactivation reassignment logic

### Integration Tests
- [ ] End-to-end assignment flow (assign → notify → respond)
- [ ] Conflict detection with real data
- [ ] Game reschedule with umpire assignment

### Frontend Tests
- [ ] Umpire dashboard renders assignments
- [ ] Accept/decline workflows
- [ ] Admin assignment modal with conflict detection
- [ ] Coach game detail shows umpire info

### Manual Testing
- Use checklist from Task 6.6
- Test with 2-3 umpires, 5-10 games
- Simulate all edge cases

---

## DEPLOYMENT PLAN

### Phase 1: Internal Testing (Week 3, Day 1-2)
1. Deploy to staging environment
2. Create test umpire accounts (3)
3. Create test games (10)
4. Run through full workflow checklist
5. Fix any critical bugs

### Phase 2: Beta with Real League (Week 3, Day 3-5)
1. Deploy to production
2. Enable for one beta league
3. Onboard 3-5 umpires
4. Assign to real games (weekend games only)
5. Monitor Application Insights for errors
6. Gather feedback

### Phase 3: Full Rollout (Week 4)
1. Address beta feedback
2. Create user documentation
3. Enable for all leagues
4. Announce to admins via email
5. Monitor adoption metrics

---

## ACCEPTANCE CRITERIA

### MVP is complete when:
- [x] Admin can create umpire profile in under 2 minutes
- [x] Admin can assign umpire to game with inline conflict detection
- [x] Double-booking is prevented 100% (zero conflicts slip through)
- [x] Umpire receives email within 5 minutes of assignment
- [x] Umpire can accept/decline in 3 clicks from email link
- [x] Admin sees umpire response within 1 minute (in-app notification)
- [x] Coach sees umpire contact on game detail (own games only)
- [x] Game reschedule updates umpire assignment automatically
- [x] Unassigned games list shows games needing coverage
- [x] Zero critical bugs in manual testing
- [x] All unit tests passing
- [x] Documentation complete (API docs, behavioral contract, CLAUDE.md)

---

## EFFORT SUMMARY

| Phase | Focus | Tasks | Hours | Days |
|-------|-------|-------|-------|------|
| 1 | Foundation | 1.1-1.9 | 15 | 2 |
| 2 | Admin Assignment | 2.1-2.11 | 25 | 3 |
| 3 | Umpire Portal | 3.1-3.6 | 20 | 2.5 |
| 4 | Coach Integration | 4.1-4.5 | 8 | 1 |
| 5 | Notifications | 5.1-5.6 | 18 | 2 |
| 6 | Testing & Deploy | 6.1-6.6 | 9 | 1.5 |
| **Total** | **MVP Complete** | **56 tasks** | **95** | **12** |

**Timeline:** 3 weeks (with buffer) for single full-stack developer

---

## POST-MVP ROADMAP (Phase 2)

**When to build:** After 1 season, if:
- ✅ >80% of games have umpires assigned
- ✅ >60% umpire portal adoption
- ✅ User requests for advanced features

**Priority order:**
1. **SMS notifications** (high ROI - improves response rate)
2. **Time-specific availability** (requested by umpires)
3. **48hr/2hr reminders** (reduces no-shows)
4. **Multi-umpire games** (needed for higher divisions)
5. **Auto-suggest** (saves admin time)
6. **Bulk assignment** (for large schedules)

**Effort:** +60 hours for full Phase 2

---

This plan provides a clear, sequenced path from zero to deployed MVP in 3 weeks. Each task is buildable, testable, and integrated into your existing codebase patterns.

**Ready to start implementation?**
