using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Timer-triggered function to send game reminders 24 hours before scheduled games.
/// Runs hourly to check for upcoming games and notify teams.
/// </summary>
public class GameReminderFunction
{
    private readonly TableServiceClient _tableService;
    private readonly ISlotRepository _slotRepo;
    private readonly IEmailService _emailService;
    private readonly ILogger _log;

    public GameReminderFunction(
        TableServiceClient tableService,
        ISlotRepository slotRepo,
        IEmailService emailService,
        ILoggerFactory lf)
    {
        _tableService = tableService;
        _slotRepo = slotRepo;
        _emailService = emailService;
        _log = lf.CreateLogger<GameReminderFunction>();
    }

    /// <summary>
    /// Runs hourly to send game reminders for games happening in ~24 hours.
    /// NCRONTAB format: {second} {minute} {hour} {day} {month} {day-of-week}
    /// "0 0 * * * *" = Run at the top of every hour
    /// </summary>
    [Function("SendGameReminders")]
    public async Task SendGameReminders(
        [TimerTrigger("0 0 * * * *")] TimerInfo timerInfo)
    {
        _log.LogInformation("Game reminder function started at {Time}", DateTime.UtcNow);

        try
        {
            // Calculate the 24-hour window for reminders
            var now = DateTime.UtcNow;
            var reminderWindowStart = now.AddHours(23); // 23-25 hours from now
            var reminderWindowEnd = now.AddHours(25);

            var reminderStartDate = DateOnly.FromDateTime(reminderWindowStart);
            var reminderEndDate = DateOnly.FromDateTime(reminderWindowEnd);

            _log.LogInformation("Checking for games between {Start} and {End}",
                reminderWindowStart, reminderWindowEnd);

            // Get all leagues
            var leaguesTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.Leagues);
            var leagues = leaguesTable.QueryAsync<TableEntity>(filter: "");

            var totalRemindersSent = 0;

            await foreach (var league in leagues)
            {
                var leagueId = league.RowKey;
                _log.LogInformation("Processing reminders for league {LeagueId}", leagueId);

                try
                {
                    var remindersSent = await ProcessLeagueReminders(
                        leagueId,
                        reminderStartDate,
                        reminderEndDate,
                        reminderWindowStart,
                        reminderWindowEnd);

                    totalRemindersSent += remindersSent;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to process reminders for league {LeagueId}", leagueId);
                    // Continue with next league
                }
            }

            _log.LogInformation("Game reminder function completed. Sent {Count} reminders", totalRemindersSent);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Game reminder function failed");
            throw;
        }
    }

    private async Task<int> ProcessLeagueReminders(
        string leagueId,
        DateOnly reminderStartDate,
        DateOnly reminderEndDate,
        DateTime reminderWindowStart,
        DateTime reminderWindowEnd)
    {
        var remindersSent = 0;

        // Get all slots for the league within the date range (across all divisions)
        var slotsTable = await TableClients.GetTableAsync(_tableService, Constants.Tables.Slots);
        var pkPrefix = Constants.Pk.Slots(leagueId, "");

        // Query with partition key prefix to get slots from all divisions
        var slots = slotsTable.QueryAsync<TableEntity>(
            filter: $"PartitionKey ge '{pkPrefix}' and PartitionKey lt '{pkPrefix}~' and GameDate ge '{reminderStartDate:yyyy-MM-dd}' and GameDate le '{reminderEndDate:yyyy-MM-dd}'");

        await foreach (var slot in slots)
        {
            try
            {
                // Only send reminders for confirmed games (not Open/Available slots)
                var status = (slot.GetString("Status") ?? "").Trim();
                if (!string.Equals(status, Constants.Status.SlotConfirmed, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Parse game date and time
                var gameDateStr = (slot.GetString("GameDate") ?? "").Trim();
                var startTimeStr = (slot.GetString("StartTime") ?? "").Trim();

                if (string.IsNullOrWhiteSpace(gameDateStr) || string.IsNullOrWhiteSpace(startTimeStr))
                    continue;

                if (!DateOnly.TryParse(gameDateStr, out var gameDate))
                    continue;

                // Parse start time (format: "HH:mm" or "h:mm tt")
                if (!TryParseTime(startTimeStr, out var gameTime))
                    continue;

                var gameDateTime = gameDate.ToDateTime(gameTime);

                // Check if game is within the reminder window
                if (gameDateTime < reminderWindowStart || gameDateTime > reminderWindowEnd)
                    continue;

                // Check if reminder already sent (to avoid duplicates)
                var reminderSentKey = $"ReminderSent_{slot.RowKey}";
                if (slot.ContainsKey(reminderSentKey) && slot.GetBoolean(reminderSentKey) == true)
                    continue;

                // Send reminders to both teams
                var homeTeamId = (slot.GetString("HomeTeamId") ?? "").Trim();
                var awayTeamId = (slot.GetString("AwayTeamId") ?? "").Trim();
                var division = (slot.GetString("Division") ?? "").Trim();
                var fieldDisplay = (slot.GetString("DisplayName") ?? slot.GetString("FieldKey") ?? "TBD").Trim();

                var sentCount = 0;

                if (!string.IsNullOrWhiteSpace(homeTeamId))
                {
                    if (await SendTeamReminder(leagueId, division, homeTeamId, gameDateStr, startTimeStr, fieldDisplay))
                        sentCount++;
                }

                if (!string.IsNullOrWhiteSpace(awayTeamId))
                {
                    if (await SendTeamReminder(leagueId, division, awayTeamId, gameDateStr, startTimeStr, fieldDisplay))
                        sentCount++;
                }

                // Mark reminder as sent to avoid duplicates
                if (sentCount > 0)
                {
                    slot[reminderSentKey] = true;
                    await slotsTable.UpdateEntityAsync(slot, slot.ETag);
                    remindersSent += sentCount;

                    _log.LogInformation("Sent {Count} reminders for slot {SlotId} (game on {GameDate} at {StartTime})",
                        sentCount, slot.RowKey, gameDateStr, startTimeStr);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to process reminder for slot {SlotId}", slot.RowKey);
                // Continue with next slot
            }
        }

        return remindersSent;
    }

    private async Task<bool> SendTeamReminder(
        string leagueId,
        string division,
        string teamId,
        string gameDate,
        string startTime,
        string field)
    {
        try
        {
            // Fetch team for contact email
            var teamClient = await TableClients.GetTableAsync(_tableService, Constants.Tables.Teams);
            var teamEntity = await teamClient.GetEntityAsync<TableEntity>(Constants.Pk.Teams(leagueId, division), teamId);

            var teamEmail = (teamEntity.Value.GetString("PrimaryContactEmail") ?? "").Trim();

            if (string.IsNullOrWhiteSpace(teamEmail))
            {
                _log.LogWarning("No email address for team {TeamId} in division {Division}", teamId, division);
                return false;
            }

            await _emailService.SendGameReminderEmailAsync(
                to: teamEmail,
                leagueId: leagueId,
                gameDate: gameDate,
                startTime: startTime,
                field: field
            );

            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send reminder to team {TeamId} in division {Division}", teamId, division);
            return false;
        }
    }

    private static bool TryParseTime(string timeStr, out TimeOnly time)
    {
        time = default;

        // Try common time formats
        if (TimeOnly.TryParse(timeStr, out time))
            return true;

        // Try with AM/PM
        if (DateTime.TryParse(timeStr, out var dateTime))
        {
            time = TimeOnly.FromDateTime(dateTime);
            return true;
        }

        return false;
    }
}
