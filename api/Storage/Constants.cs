namespace GameSwap.Functions.Storage;

/// <summary>
/// Canonical constants for storage + API behavior.
/// Keep these aligned with the UI constants and /docs/contract.md.
/// </summary>
public static class Constants
{
    public const string LEAGUE_HEADER_NAME = "x-league-id";

    public static class Roles
    {
        public const string LeagueAdmin = "LeagueAdmin";
        public const string Coach = "Coach";
        public const string Viewer = "Viewer";
    }

    public static class Tables
    {
        public const string Leagues = "GameSwapLeagues";
        public const string Memberships = "GameSwapMemberships";
        public const string GlobalAdmins = "GameSwapGlobalAdmins";
        public const string Users = "GameSwapUsers";

        public const string AccessRequests = "GameSwapAccessRequests";
        public const string Fields = "GameSwapFields";
        public const string Divisions = "GameSwapDivisions";
        public const string Events = "GameSwapEvents";
        public const string Slots = "GameSwapSlots";
        public const string SlotRequests = "GameSwapSlotRequests";
        public const string ScheduleRuns = "GameSwapScheduleRuns";
        public const string FieldAvailabilityRules = "GameSwapFieldAvailabilityRules";
        public const string FieldAvailabilityExceptions = "GameSwapFieldAvailabilityExceptions";
        public const string FieldAvailabilityAllocations = "GameSwapFieldAvailabilityAllocations";

        // Org / Team management
        public const string Teams = "GameSwapTeams";
        public const string TeamContacts = "GameSwapTeamContacts";
        public const string Seasons = "GameSwapSeasons";
        public const string SeasonDivisions = "GameSwapSeasonDivisions";
        public const string LeagueInvites = "GameSwapLeagueInvites";
        public const string LeagueBackups = "GameSwapLeagueBackups";

        // Notifications
        public const string Notifications = "GameSwapNotifications";
        public const string NotificationPreferences = "GameSwapNotificationPreferences";
        public const string EmailQueue = "GameSwapEmailQueue";
    }

    public static class Pk
    {
        public const string Leagues = "LEAGUE"; // RK = leagueId
        public const string GlobalAdmins = "GLOBAL"; // RK = userId
        public const string Users = "USER"; // RK = userId

        public static string AccessRequests(string leagueId) => $"ACCESSREQ|{leagueId}"; // RK = userId
        public static string Divisions(string leagueId) => $"DIV|{leagueId}"; // RK = divisionCode

        public static string Fields(string leagueId, string parkCode) => $"FIELD|{leagueId}|{parkCode}"; // RK = fieldCode
        public static string Slots(string leagueId, string division) => $"SLOT|{leagueId}|{division}"; // RK = slotId
        public static string SlotRequests(string leagueId, string division, string slotId) => $"SLOTREQ|{leagueId}|{division}|{slotId}"; // RK = requestId
        public static string Teams(string leagueId, string division) => $"TEAM|{leagueId}|{division}"; // RK = teamId
        public static string ScheduleRuns(string leagueId, string division) => $"SCHED|{leagueId}|{division}"; // RK = runId
        public const string LeagueBackups = "LEAGUEBACKUP"; // RK = leagueId

        // Calendar events (non-slot): PK = EVENT|{leagueId}, RK = eventId
        public static string Events(string leagueId) => $"EVENT|{leagueId}";

        // Field availability rules: PK = AVAILRULE|{leagueId}|{fieldKey}, RK = ruleId
        public static string FieldAvailabilityRules(string leagueId, string fieldKey) => $"AVAILRULE|{leagueId}|{fieldKey}";

        // Field availability rule exceptions: PK = AVAILRULEEX|{ruleId}, RK = exceptionId
        public static string FieldAvailabilityRuleExceptions(string ruleId) => $"AVAILRULEEX|{ruleId}";

        // Secondary index rows for fast division/date range queries: PK = AVAILRULEIDX|{leagueId}|{divisionId}
        // Suggested RK: {startDate}|{endDate}|{ruleId}
        public static string FieldAvailabilityRuleIndex(string leagueId, string divisionId) => $"AVAILRULEIDX|{leagueId}|{divisionId}";

        // Field allocations: PK = ALLOC|{leagueId}|{scope}|{fieldKey}, RK = allocationId
        public static string FieldAvailabilityAllocations(string leagueId, string scope, string fieldKey)
            => $"ALLOC|{leagueId}|{scope}|{NormalizeAllocFieldKey(fieldKey)}";

        private static string NormalizeAllocFieldKey(string fieldKey)
            => (fieldKey ?? "").Trim().Replace("/", "|");

        // Notifications: PK = userId, RK = notificationId
        public static string Notifications(string userId) => userId;

        // Notification preferences: PK = userId, RK = leagueId (or "global")
        public static string NotificationPreferences(string userId) => userId;

        // Email queue: PK = status (Pending, Sent, Failed), RK = emailId
        public static string EmailQueue(string status) => $"EMAIL|{status}";
    }

    public static class FieldAvailabilityColumns
    {
        public const string FieldKey = "FieldKey";
        public const string Division = "Division";
        public const string DivisionIds = "DivisionIds";
        public const string StartsOn = "StartsOn";
        public const string EndsOn = "EndsOn";
        public const string DaysOfWeek = "DaysOfWeek";
        public const string StartTimeLocal = "StartTimeLocal";
        public const string EndTimeLocal = "EndTimeLocal";
        public const string RecurrencePattern = "RecurrencePattern";
        public const string Timezone = "Timezone";
        public const string IsActive = "IsActive";
    }

    public static class FieldAvailabilityExceptionColumns
    {
        public const string DateFrom = "DateFrom";
        public const string DateTo = "DateTo";
        public const string StartTimeLocal = "StartTimeLocal";
        public const string EndTimeLocal = "EndTimeLocal";
        public const string Reason = "Reason";
    }

    public static class Status
    {
        // Fields
        public const string FieldActive = "Active";
        public const string FieldInactive = "Inactive";

        // Access requests
        public const string AccessRequestPending = "Pending";
        public const string AccessRequestApproved = "Approved";
        public const string AccessRequestDenied = "Denied";

        // Slots
        public const string SlotOpen = "Open";
        public const string SlotCancelled = "Cancelled";
        public const string SlotConfirmed = "Confirmed";

        // Slot requests
        public const string SlotRequestPending = "Pending";
        public const string SlotRequestApproved = "Approved";
        public const string SlotRequestDenied = "Denied";

        // Events
        public const string EventScheduled = "Scheduled";
        public const string EventCancelled = "Cancelled";
    }

    public static class EventTypes
    {
        public const string Practice = "Practice";
        public const string Meeting = "Meeting";
        public const string Clinic = "Clinic";
        public const string Other = "Other";
    }
}
