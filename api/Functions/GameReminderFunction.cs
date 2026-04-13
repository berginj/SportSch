using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Timer-triggered function that sends upcoming game reminders.
/// Uses durable reminder dispatch records and honors user email preferences.
/// </summary>
public class GameReminderFunction
{
    private static readonly ReminderWindow[] ReminderWindows =
    {
        new("24h", TimeSpan.FromHours(23), TimeSpan.FromHours(25)),
        new("2h", TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(150)),
    };

    private readonly TableServiceClient _tableService;
    private readonly IMembershipRepository _membershipRepo;
    private readonly INotificationPreferencesService _preferencesService;
    private readonly IEmailService _emailService;
    private readonly ILogger _log;

    private readonly record struct ReminderWindow(string Type, TimeSpan WindowStart, TimeSpan WindowEnd);
    private readonly record struct CoachRecipient(string UserId, string Email);

    public GameReminderFunction(
        TableServiceClient tableService,
        IMembershipRepository membershipRepo,
        INotificationPreferencesService preferencesService,
        IEmailService emailService,
        ILoggerFactory lf)
    {
        _tableService = tableService;
        _membershipRepo = membershipRepo;
        _preferencesService = preferencesService;
        _emailService = emailService;
        _log = lf.CreateLogger<GameReminderFunction>();
    }

    /// <summary>
    /// Runs hourly and checks for upcoming game windows.
    /// NCRONTAB: second minute hour day month day-of-week
    /// </summary>
    [Function("SendGameReminders")]
    public async Task SendGameReminders([TimerTrigger("0 0 * * * *")] TimerInfo timerInfo)
    {
        var nowUtc = DateTime.UtcNow;
        _log.LogInformation("Game reminder function started at {TimeUtc}", nowUtc);

        try
        {
            var leaguesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
            var totalRemindersSent = 0;

            await foreach (var league in leaguesTable.QueryAsync<TableEntity>(filter: ""))
            {
                var leagueId = (league.RowKey ?? "").Trim();
                if (string.IsNullOrWhiteSpace(leagueId)) continue;

                try
                {
                    var sent = await ProcessLeagueReminders(leagueId, nowUtc);
                    totalRemindersSent += sent;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to process reminders for league {LeagueId}", leagueId);
                }
            }

            _log.LogInformation("Game reminder function completed. Total reminders sent: {Count}", totalRemindersSent);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Game reminder function failed");
            throw;
        }
    }

    private async Task<int> ProcessLeagueReminders(string leagueId, DateTime nowUtc)
    {
        var earliestWindow = nowUtc.Add(ReminderWindows.Min(w => w.WindowStart));
        var latestWindow = nowUtc.Add(ReminderWindows.Max(w => w.WindowEnd));
        var fromDate = DateOnly.FromDateTime(earliestWindow);
        var toDate = DateOnly.FromDateTime(latestWindow);

        var recipientsByTeam = await LoadCoachRecipientsByTeamAsync(leagueId);
        var slotsTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.Slots);
        var reminderDispatchTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.ReminderDispatch);

        var pkPrefix = Constants.Pk.Slots(leagueId, "");
        var filter = $"PartitionKey ge '{pkPrefix}' and PartitionKey lt '{pkPrefix}~' and GameDate ge '{fromDate:yyyy-MM-dd}' and GameDate le '{toDate:yyyy-MM-dd}'";
        var remindersSent = 0;

        await foreach (var slot in slotsTable.QueryAsync<TableEntity>(filter: filter))
        {
            var status = (slot.GetString("Status") ?? "").Trim();
            if (!string.Equals(status, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
                continue;

            var gameDate = (slot.GetString("GameDate") ?? "").Trim();
            var startTime = (slot.GetString("StartTime") ?? "").Trim();
            if (!TryParseGameDateTime(gameDate, startTime, out var gameDateTime))
                continue;

            foreach (var window in ReminderWindows)
            {
                if (!IsWithinReminderWindow(nowUtc, gameDateTime, window))
                    continue;

                var slotId = (slot.GetString("SlotId") ?? slot.RowKey ?? "").Trim();
                if (string.IsNullOrWhiteSpace(slotId))
                    continue;

                if (await ReminderAlreadySentAsync(reminderDispatchTable, leagueId, slotId, window.Type))
                    continue;

                var sentForSlot = await SendSlotRemindersAsync(
                    leagueId,
                    slot,
                    recipientsByTeam);

                if (sentForSlot <= 0)
                    continue;

                await MarkReminderSentAsync(
                    reminderDispatchTable,
                    leagueId,
                    slotId,
                    window.Type,
                    gameDate,
                    startTime,
                    sentForSlot);

                remindersSent += sentForSlot;
                _log.LogInformation(
                    "Sent {Count} reminder emails for slot {SlotId} ({ReminderType})",
                    sentForSlot,
                    slotId,
                    window.Type);
            }
        }

        return remindersSent;
    }

    private async Task<Dictionary<string, List<CoachRecipient>>> LoadCoachRecipientsByTeamAsync(string leagueId)
    {
        var memberships = await _membershipRepo.GetLeagueMembershipsAsync(leagueId);
        var map = new Dictionary<string, List<CoachRecipient>>(StringComparer.OrdinalIgnoreCase);
        var uniqueByTeam = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in memberships)
        {
            var role = (member.GetString("Role") ?? "").Trim();
            if (!string.Equals(role, Constants.Roles.Coach, StringComparison.OrdinalIgnoreCase))
                continue;

            var userId = (member.PartitionKey ?? "").Trim();
            var email = (member.GetString("Email") ?? "").Trim();
            var division = ReadMembershipDivision(member);
            var teamId = ReadMembershipTeamId(member);
            if (string.IsNullOrWhiteSpace(userId) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(division) ||
                string.IsNullOrWhiteSpace(teamId))
            {
                continue;
            }

            var key = TeamKey(division, teamId);
            if (!map.TryGetValue(key, out var recipients))
            {
                recipients = new List<CoachRecipient>();
                map[key] = recipients;
            }
            if (!uniqueByTeam.TryGetValue(key, out var seenUsers))
            {
                seenUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                uniqueByTeam[key] = seenUsers;
            }
            if (!seenUsers.Add(userId))
                continue;

            recipients.Add(new CoachRecipient(userId, email));
        }

        return map;
    }

    private async Task<int> SendSlotRemindersAsync(
        string leagueId,
        TableEntity slot,
        IReadOnlyDictionary<string, List<CoachRecipient>> recipientsByTeam)
    {
        var division = (slot.GetString("Division") ?? "").Trim();
        var homeTeamId = (slot.GetString("HomeTeamId") ?? "").Trim();
        var awayTeamId = ResolveOpponentTeamId(slot);
        var gameDate = (slot.GetString("GameDate") ?? "").Trim();
        var startTime = (slot.GetString("StartTime") ?? "").Trim();
        var fieldDisplay = (slot.GetString("DisplayName") ?? slot.GetString("FieldKey") ?? "TBD").Trim();

        if (string.IsNullOrWhiteSpace(division))
            return 0;

        var totalSent = 0;
        totalSent += await SendTeamCoachRemindersAsync(leagueId, division, homeTeamId, gameDate, startTime, fieldDisplay, recipientsByTeam);
        totalSent += await SendTeamCoachRemindersAsync(leagueId, division, awayTeamId, gameDate, startTime, fieldDisplay, recipientsByTeam);
        return totalSent;
    }

    private async Task<int> SendTeamCoachRemindersAsync(
        string leagueId,
        string division,
        string teamId,
        string gameDate,
        string startTime,
        string field,
        IReadOnlyDictionary<string, List<CoachRecipient>> recipientsByTeam)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return 0;

        if (!recipientsByTeam.TryGetValue(TeamKey(division, teamId), out var recipients) || recipients.Count == 0)
            return 0;

        var sent = 0;
        foreach (var recipient in recipients)
        {
            try
            {
                var canSend = await _preferencesService.ShouldSendEmailAsync(recipient.UserId, leagueId, "GameReminder");
                if (!canSend) continue;

                await _emailService.SendGameReminderEmailAsync(
                    to: recipient.Email,
                    leagueId: leagueId,
                    gameDate: gameDate,
                    startTime: startTime,
                    field: field);
                sent += 1;
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex,
                    "Failed sending game reminder to user {UserId} for league {LeagueId}",
                    recipient.UserId,
                    leagueId);
            }
        }

        return sent;
    }

    private static string ResolveOpponentTeamId(TableEntity slot)
    {
        var awayTeamId = (slot.GetString("AwayTeamId") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(awayTeamId))
            return awayTeamId;

        return (slot.GetString("ConfirmedTeamId") ?? "").Trim();
    }

    private static string TeamKey(string division, string teamId)
        => $"{division}|{teamId}";

    private static string ReadMembershipDivision(TableEntity membership)
    {
        return (membership.GetString("Division") ?? "").Trim();
    }

    private static string ReadMembershipTeamId(TableEntity membership)
    {
        return (membership.GetString("TeamId") ?? "").Trim();
    }

    private static bool IsWithinReminderWindow(DateTime nowUtc, DateTime gameDateTime, ReminderWindow window)
    {
        var untilGame = gameDateTime - nowUtc;
        return untilGame >= window.WindowStart && untilGame <= window.WindowEnd;
    }

    private static bool TryParseGameDateTime(string gameDate, string startTime, out DateTime gameDateTime)
    {
        gameDateTime = default;
        if (!DateOnly.TryParse(gameDate, out var parsedDate))
            return false;
        if (!TryParseTime(startTime, out var parsedTime))
            return false;

        gameDateTime = parsedDate.ToDateTime(parsedTime);
        return true;
    }

    private static bool TryParseTime(string timeStr, out TimeOnly time)
    {
        time = default;
        if (TimeOnly.TryParse(timeStr, out time))
            return true;

        if (DateTime.TryParse(timeStr, out var dateTime))
        {
            time = TimeOnly.FromDateTime(dateTime);
            return true;
        }

        return false;
    }

    private static async Task<bool> ReminderAlreadySentAsync(
        TableClient reminderDispatchTable,
        string leagueId,
        string slotId,
        string reminderType)
    {
        var pk = Constants.Pk.ReminderDispatch(leagueId, slotId);
        try
        {
            _ = await reminderDispatchTable.GetEntityAsync<TableEntity>(pk, reminderType);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    private static async Task MarkReminderSentAsync(
        TableClient reminderDispatchTable,
        string leagueId,
        string slotId,
        string reminderType,
        string gameDate,
        string startTime,
        int recipientCount)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new TableEntity(Constants.Pk.ReminderDispatch(leagueId, slotId), reminderType)
        {
            ["LeagueId"] = leagueId,
            ["SlotId"] = slotId,
            ["ReminderType"] = reminderType,
            ["GameDate"] = gameDate,
            ["StartTime"] = startTime,
            ["RecipientCount"] = recipientCount,
            ["SentUtc"] = now,
            ["UpdatedUtc"] = now,
        };
        await reminderDispatchTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }
}
