using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using GameSwap.Functions.Functions;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameSwap.Tests.Functions;

public class GameReminderFunctionTests
{
    [Fact]
    public async Task SendGameReminders_UsesConfirmedTeamWhenAwayTeamIsBlank()
    {
        var now = DateTime.UtcNow;
        var gameTime = now.AddHours(24);
        var leagueId = "LEAGUE-1";
        var division = "AAA";
        var sentEmails = new List<string>();

        var league = new TableEntity(Constants.Pk.Leagues, leagueId);
        var slot = new TableEntity(Constants.Pk.Slots(leagueId, division), "slot-1")
        {
            ["LeagueId"] = leagueId,
            ["Division"] = division,
            ["Status"] = Constants.Status.SlotConfirmed,
            ["GameDate"] = gameTime.ToString("yyyy-MM-dd"),
            ["StartTime"] = gameTime.ToString("HH:mm"),
            ["DisplayName"] = "Diamond 1",
            ["HomeTeamId"] = "TEAM-A",
            ["AwayTeamId"] = "",
            ["ConfirmedTeamId"] = "TEAM-B",
        };

        var tableService = new Mock<TableServiceClient>();
        var leaguesTable = CreateTableClient();
        var slotsTable = CreateTableClient();
        var reminderDispatchTable = CreateTableClient();
        var membershipRepo = new Mock<IMembershipRepository>();
        var preferencesService = new Mock<INotificationPreferencesService>();
        var emailService = new Mock<IEmailService>();

        tableService.Setup(x => x.GetTableClient(Constants.Tables.Leagues)).Returns(leaguesTable.Object);
        tableService.Setup(x => x.GetTableClient(Constants.Tables.Slots)).Returns(slotsTable.Object);
        tableService.Setup(x => x.GetTableClient(Constants.Tables.ReminderDispatch)).Returns(reminderDispatchTable.Object);

        leaguesTable
            .Setup(x => x.QueryAsync<TableEntity>(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncPageable(league));
        slotsTable
            .Setup(x => x.QueryAsync<TableEntity>(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncPageable(slot));
        reminderDispatchTable
            .Setup(x => x.GetEntityAsync<TableEntity>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));
        reminderDispatchTable
            .Setup(x => x.UpsertEntityAsync(It.IsAny<TableEntity>(), TableUpdateMode.Replace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        membershipRepo
            .Setup(x => x.GetLeagueMembershipsAsync(leagueId))
            .ReturnsAsync(new List<TableEntity>
            {
                new("coach-home", leagueId)
                {
                    ["Role"] = Constants.Roles.Coach,
                    ["Division"] = division,
                    ["TeamId"] = "TEAM-A",
                    ["Email"] = "home@example.com",
                },
                new("coach-away", leagueId)
                {
                    ["Role"] = Constants.Roles.Coach,
                    ["Division"] = division,
                    ["TeamId"] = "TEAM-B",
                    ["Email"] = "away@example.com",
                }
            });

        preferencesService
            .Setup(x => x.ShouldSendEmailAsync(It.IsAny<string>(), leagueId, "GameReminder"))
            .ReturnsAsync(true);
        emailService
            .Setup(x => x.SendGameReminderEmailAsync(It.IsAny<string>(), leagueId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string, string>((to, _, _, _, _) => sentEmails.Add(to))
            .Returns(Task.CompletedTask);

        var function = new GameReminderFunction(
            tableService.Object,
            membershipRepo.Object,
            preferencesService.Object,
            emailService.Object,
            CreateLoggerFactory());

        await function.SendGameReminders(null!);

        Assert.Contains("home@example.com", sentEmails);
        Assert.Contains("away@example.com", sentEmails);
        Assert.Equal(2, sentEmails.Count);
        reminderDispatchTable.Verify(
            x => x.UpsertEntityAsync(It.IsAny<TableEntity>(), TableUpdateMode.Replace, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Mock<TableClient> CreateTableClient()
    {
        var tableClient = new Mock<TableClient>();
        tableClient
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<TableItem>>());
        return tableClient;
    }

    private static AsyncPageable<TableEntity> ToAsyncPageable(params TableEntity[] entities)
    {
        var page = Page<TableEntity>.FromValues(entities, null, Mock.Of<Response>());
        return AsyncPageable<TableEntity>.FromPages(new[] { page });
    }

    private static ILoggerFactory CreateLoggerFactory()
        => LoggerFactory.Create(_ => { });
}
