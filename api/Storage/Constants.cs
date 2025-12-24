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

        public const string AccessRequests = "GameSwapAccessRequests";
        public const string Fields = "GameSwapFields";
        public const string Divisions = "GameSwapDivisions";
        public const string Events = "GameSwapEvents";
        public const string Slots = "GameSwapSlots";
        public const string SlotRequests = "GameSwapSlotRequests";

        // Org / Team management
        public const string Teams = "GameSwapTeams";
        public const string TeamContacts = "GameSwapTeamContacts";
        public const string Seasons = "GameSwapSeasons";
        public const string SeasonDivisions = "GameSwapSeasonDivisions";
        public const string LeagueInvites = "GameSwapLeagueInvites";
    }

    public static class Pk
    {
        public const string Leagues = "LEAGUE"; // RK = leagueId
        public const string GlobalAdmins = "GLOBAL"; // RK = userId

        public static string AccessRequests(string leagueId) => $"ACCESSREQ#{leagueId}"; // RK = userId
        public static string Divisions(string leagueId) => $"DIV#{leagueId}"; // RK = divisionCode

        public static string Fields(string leagueId, string parkCode) => $"FIELD#{leagueId}#{parkCode}"; // RK = fieldCode
        public static string Slots(string leagueId, string division) => $"SLOT#{leagueId}#{division}"; // RK = slotId
        public static string SlotRequests(string leagueId, string division, string slotId) => $"SLOTREQ#{leagueId}#{division}#{slotId}"; // RK = requestId

        // Calendar events (non-slot): PK = EVENT#{leagueId}, RK = eventId
        public static string Events(string leagueId) => $"EVENT#{leagueId}";
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
