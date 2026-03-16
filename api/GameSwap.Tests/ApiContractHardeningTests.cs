#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using GameSwap.Functions.Functions;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameSwap.Tests;

public class ApiContractHardeningTests
{
    [Fact]
    public async Task ApiResponses_Error_HidesInternalDetailsForServerErrors()
    {
        var req = CreateRequest("http://localhost:7071/api/test");

        var response = ApiResponses.Error(
            req,
            HttpStatusCode.InternalServerError,
            ErrorCodes.INTERNAL_ERROR,
            "Internal Server Error",
            new { exception = "Exploded", detail = "secret", message = "should not leak" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var error = json.RootElement.GetProperty("error");
        var details = error.GetProperty("details");

        Assert.True(details.TryGetProperty("requestId", out _));
        Assert.False(details.TryGetProperty("exception", out _));
        Assert.False(details.TryGetProperty("detail", out _));
        Assert.False(details.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task ListLeagues_ReturnsOnlyPublicFields()
    {
        var leagueRepo = new Mock<ILeagueRepository>();
        leagueRepo.Setup(x => x.QueryLeaguesAsync(false)).ReturnsAsync(new System.Collections.Generic.List<TableEntity>
        {
            new(Constants.Pk.Leagues, "LEAGUE-1")
            {
                ["Name"] = "Alpha League",
                ["Timezone"] = "America/New_York",
                ["Status"] = "Active",
                ["ContactEmail"] = "admin@example.com",
                ["ContactPhone"] = "555-1234",
                ["SpringStart"] = "2026-03-01",
            },
        });

        var functions = new LeaguesFunctions(
            leagueRepo.Object,
            Mock.Of<IMembershipRepository>(),
            Mock.Of<TableServiceClient>(),
            CreateLoggerFactory());

        var response = await functions.List(CreateRequest("http://localhost:7071/api/leagues"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var item = json.RootElement.GetProperty("data")[0];

        Assert.Equal("LEAGUE-1", item.GetProperty("leagueId").GetString());
        Assert.Equal("Alpha League", item.GetProperty("name").GetString());
        Assert.False(item.TryGetProperty("timezone", out _));
        Assert.False(item.TryGetProperty("status", out _));
        Assert.False(item.TryGetProperty("contact", out _));
        Assert.False(item.TryGetProperty("season", out _));
    }

    [Fact]
    public async Task ListMemberships_AllRequiresUserId()
    {
        var memberships = new Mock<IMembershipRepository>();
        memberships
            .Setup(x => x.IsGlobalAdminAsync("global-admin"))
            .ReturnsAsync(true);

        var functions = new MembershipsFunctions(
            memberships.Object,
            Mock.Of<TableServiceClient>(),
            CreateLoggerFactory());

        var response = await functions.List(
            CreateAuthenticatedRequest("http://localhost:7071/api/memberships?all=true", "global-admin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("BAD_REQUEST", error.GetProperty("code").GetString());
        Assert.Contains("userId", error.GetProperty("message").GetString() ?? "");

        memberships.Verify(x => x.GetUserMembershipsAsync(It.IsAny<string>()), Times.Never);
        memberships.Verify(x => x.QueryAllMembershipsAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ListMemberships_AllWithUserId_UsesExactUserPartition()
    {
        var memberships = new Mock<IMembershipRepository>();
        memberships
            .Setup(x => x.IsGlobalAdminAsync("global-admin"))
            .ReturnsAsync(true);
        memberships
            .Setup(x => x.GetUserMembershipsAsync("user-1"))
            .ReturnsAsync(new System.Collections.Generic.List<TableEntity>
            {
                new("user-1", "LEAGUE-1")
                {
                    ["Email"] = "viewer@example.com",
                    ["Role"] = Constants.Roles.Viewer,
                },
                new("user-1", "LEAGUE-2")
                {
                    ["Email"] = "coach@example.com",
                    ["Role"] = Constants.Roles.Coach,
                    ["Division"] = "AAA",
                    ["TeamId"] = "TEAM-9",
                },
            });

        var functions = new MembershipsFunctions(
            memberships.Object,
            Mock.Of<TableServiceClient>(),
            CreateLoggerFactory());

        var response = await functions.List(
            CreateAuthenticatedRequest(
                "http://localhost:7071/api/memberships?all=true&userId=user-1&leagueId=LEAGUE-2&role=Coach&search=coach",
                "global-admin"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var rows = json.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Single(rows);

        var item = rows[0];
        Assert.Equal("user-1", item.GetProperty("userId").GetString());
        Assert.Equal("LEAGUE-2", item.GetProperty("leagueId").GetString());
        Assert.Equal(Constants.Roles.Coach, item.GetProperty("role").GetString());
        Assert.Equal("coach@example.com", item.GetProperty("email").GetString());

        var team = item.GetProperty("team");
        Assert.Equal("AAA", team.GetProperty("division").GetString());
        Assert.Equal("TEAM-9", team.GetProperty("teamId").GetString());

        memberships.Verify(x => x.GetUserMembershipsAsync("user-1"), Times.Once);
        memberships.Verify(x => x.QueryAllMembershipsAsync(It.IsAny<string?>()), Times.Never);
        memberships.Verify(
            x => x.QueryLeagueMembershipsAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAdminDashboard_AggregatesAcrossPagedSlots()
    {
        var openDate = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd");
        var confirmedDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");

        var accessRequests = new Mock<IAccessRequestRepository>();
        accessRequests
            .Setup(x => x.QueryAccessRequestsByLeagueAsync("LEAGUE-1", Constants.Status.AccessRequestPending))
            .ReturnsAsync(new System.Collections.Generic.List<TableEntity>
            {
                new("ACCESSREQ|LEAGUE-1", "user-1"),
                new("ACCESSREQ|LEAGUE-1", "user-2"),
            });

        var divisions = new Mock<IDivisionRepository>();
        divisions
            .Setup(x => x.QueryDivisionsAsync("LEAGUE-1"))
            .ReturnsAsync(new System.Collections.Generic.List<TableEntity>
            {
                new(Constants.Pk.Divisions("LEAGUE-1"), "AAA"),
                new(Constants.Pk.Divisions("LEAGUE-1"), "AA"),
            });

        var memberships = new Mock<IMembershipRepository>();
        memberships
            .Setup(x => x.GetLeagueMembershipsAsync("LEAGUE-1"))
            .ReturnsAsync(new System.Collections.Generic.List<TableEntity>
            {
                new("coach-1", "LEAGUE-1")
                {
                    ["Role"] = Constants.Roles.Coach,
                    ["TeamId"] = "TEAM-1",
                },
                new("coach-2", "LEAGUE-1")
                {
                    ["Role"] = Constants.Roles.Coach,
                    ["TeamId"] = "",
                },
                new("viewer-1", "LEAGUE-1")
                {
                    ["Role"] = Constants.Roles.Viewer,
                },
            });

        var slots = new Mock<ISlotRepository>();
        slots
            .SetupSequence(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), It.IsAny<string?>()))
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items =
                {
                    CreateSlot("slot-open", Constants.Status.SlotOpen, "game", openDate),
                },
                ContinuationToken = "next",
            })
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items =
                {
                    CreateSlot("slot-confirmed", Constants.Status.SlotConfirmed, "game", confirmedDate),
                    CreateSlot("slot-practice", Constants.Status.SlotConfirmed, "practice", confirmedDate),
                    CreateSlot("slot-availability", Constants.Status.SlotOpen, "game", confirmedDate, isAvailability: true),
                },
                ContinuationToken = null,
            });

        var functions = new DashboardFunctions(
            accessRequests.Object,
            divisions.Object,
            memberships.Object,
            slots.Object,
            Mock.Of<ITeamRepository>(),
            CreateGlobalAdminTableService("global-admin"),
            CreateLoggerFactory());

        var response = await functions.GetAdminDashboard(CreateLeagueScopedRequest("http://localhost:7071/api/admin/dashboard", "global-admin", "LEAGUE-1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("pendingRequests").GetInt32());
        Assert.Equal(1, data.GetProperty("unassignedCoaches").GetInt32());
        Assert.Equal(2, data.GetProperty("totalCoaches").GetInt32());
        Assert.Equal(50, data.GetProperty("scheduleCoverage").GetInt32());
        Assert.Equal(1, data.GetProperty("upcomingGames").GetInt32());
        Assert.Equal(2, data.GetProperty("totalSlots").GetInt32());
        Assert.Equal(1, data.GetProperty("confirmedSlots").GetInt32());
        Assert.Equal(1, data.GetProperty("openSlots").GetInt32());
        Assert.Equal(2, data.GetProperty("divisions").GetInt32());

        slots.Verify(x => x.QuerySlotsAsync(It.Is<SlotQueryFilter>(filter =>
                filter.LeagueId == "LEAGUE-1" &&
                filter.PageSize == 250 &&
                filter.ExcludeAvailability),
            null), Times.Once);
        slots.Verify(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), "next"), Times.Once);
    }

    [Fact]
    public async Task GetCoachDashboard_AggregatesOpenOffersAndUpcomingGames()
    {
        var memberships = new Mock<IMembershipRepository>();
        memberships
            .Setup(x => x.GetMembershipAsync("global-admin", "LEAGUE-1"))
            .ReturnsAsync(new TableEntity("global-admin", "LEAGUE-1")
            {
                ["Role"] = Constants.Roles.Coach,
                ["Division"] = "AAA",
                ["TeamId"] = "TEAM-1",
            });

        var teams = new Mock<ITeamRepository>();
        teams
            .Setup(x => x.GetTeamAsync("LEAGUE-1", "AAA", "TEAM-1"))
            .ReturnsAsync(new TableEntity(Constants.Pk.Teams("LEAGUE-1", "AAA"), "TEAM-1")
            {
                ["Name"] = "Blue Waves",
                ["PrimaryContactName"] = "Coach Blue",
                ["PrimaryContactEmail"] = "coach@example.com",
                ["PrimaryContactPhone"] = "555-0001",
            });

        var slots = new Mock<ISlotRepository>();
        slots
            .SetupSequence(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), It.IsAny<string?>()))
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items =
                {
                    CreateCoachSlot("slot-other-open", Constants.Status.SlotOpen, "game", offeringTeamId: "TEAM-2"),
                    CreateCoachSlot("slot-my-open", Constants.Status.SlotOpen, "game", offeringTeamId: "TEAM-1"),
                },
                ContinuationToken = "next",
            })
            .ReturnsAsync(new PaginationResult<TableEntity>
            {
                Items =
                {
                    CreateCoachSlot("slot-request", Constants.Status.SlotOpen, "request", offeringTeamId: "TEAM-3"),
                    CreateCoachSlot("slot-practice", Constants.Status.SlotConfirmed, "practice", offeringTeamId: "TEAM-1"),
                    CreateCoachSlot("slot-confirmed", Constants.Status.SlotConfirmed, "game", offeringTeamId: "TEAM-2", homeTeamId: "TEAM-1", awayTeamId: "TEAM-2"),
                },
                ContinuationToken = null,
            });

        var functions = new DashboardFunctions(
            Mock.Of<IAccessRequestRepository>(),
            Mock.Of<IDivisionRepository>(),
            memberships.Object,
            slots.Object,
            teams.Object,
            CreateGlobalAdminTableService("global-admin"),
            CreateLoggerFactory());

        var response = await functions.GetCoachDashboard(CreateLeagueScopedRequest("http://localhost:7071/api/coach/dashboard", "global-admin", "LEAGUE-1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        var team = data.GetProperty("team");

        Assert.Equal("TEAM-1", team.GetProperty("teamId").GetString());
        Assert.Equal("Blue Waves", team.GetProperty("name").GetString());
        Assert.Equal("AAA", team.GetProperty("division").GetString());
        Assert.Equal(1, data.GetProperty("openOffersInDivision").GetInt32());
        Assert.Equal(1, data.GetProperty("myOpenOffers").GetInt32());
        Assert.Single(data.GetProperty("upcomingGames").EnumerateArray());

        slots.Verify(x => x.QuerySlotsAsync(It.Is<SlotQueryFilter>(filter =>
                filter.LeagueId == "LEAGUE-1" &&
                filter.Division == "AAA" &&
                filter.PageSize == 250 &&
                filter.ExcludeAvailability),
            null), Times.Once);
        slots.Verify(x => x.QuerySlotsAsync(It.IsAny<SlotQueryFilter>(), "next"), Times.Once);
    }

    private static HttpRequestData CreateRequest(string url)
    {
        var context = new Mock<FunctionContext>();
        context.SetupGet(x => x.InvocationId).Returns(Guid.NewGuid().ToString());
        return new TestHttpRequestData(context.Object, new Uri(url), new HttpHeadersCollection());
    }

    private static HttpRequestData CreateAuthenticatedRequest(string url, string userId)
    {
        var context = new Mock<FunctionContext>();
        context.SetupGet(x => x.InvocationId).Returns(Guid.NewGuid().ToString());

        return new TestHttpRequestData(
            context.Object,
            new Uri(url),
            new HttpHeadersCollection
            {
                { "x-user-id", userId },
            });
    }

    private static HttpRequestData CreateLeagueScopedRequest(string url, string userId, string leagueId)
    {
        var context = new Mock<FunctionContext>();
        context.SetupGet(x => x.InvocationId).Returns(Guid.NewGuid().ToString());

        return new TestHttpRequestData(
            context.Object,
            new Uri(url),
            new HttpHeadersCollection
            {
                { "x-user-id", userId },
                { Constants.LEAGUE_HEADER_NAME, leagueId },
            });
    }

    private sealed class TestHttpRequestData : HttpRequestData
    {
        public TestHttpRequestData(FunctionContext functionContext, Uri url, HttpHeadersCollection headers)
            : base(functionContext)
        {
            Url = url;
            Headers = headers;
            Body = new MemoryStream();
            Cookies = Array.Empty<IHttpCookie>();
            Identities = Array.Empty<ClaimsIdentity>();
            Method = "GET";
        }

        public override Stream Body { get; }
        public override HttpHeadersCollection Headers { get; }
        public override IReadOnlyCollection<IHttpCookie> Cookies { get; }
        public override Uri Url { get; }
        public override IEnumerable<ClaimsIdentity> Identities { get; }
        public override string Method { get; }

        public override HttpResponseData CreateResponse()
            => new TestHttpResponseData(FunctionContext, HttpStatusCode.OK);
    }

    private static async System.Threading.Tasks.Task<JsonDocument> ReadJsonAsync(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonDocument.Parse(body);
    }

    private static ILoggerFactory CreateLoggerFactory()
        => LoggerFactory.Create(_ => { });

    private static TableServiceClient CreateGlobalAdminTableService(string userId)
    {
        var tableService = new Mock<TableServiceClient>();
        var globalAdmins = new Mock<TableClient>();
        var entityResponse = new Mock<Response<TableEntity>>();
        entityResponse.SetupGet(x => x.Value).Returns(new TableEntity(Constants.Pk.GlobalAdmins, userId));

        globalAdmins
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Response<TableItem>>()));
        globalAdmins
            .Setup(x => x.GetEntityAsync<TableEntity>(
                Constants.Pk.GlobalAdmins,
                userId,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entityResponse.Object);

        tableService
            .Setup(x => x.GetTableClient(Constants.Tables.GlobalAdmins))
            .Returns(globalAdmins.Object);

        return tableService.Object;
    }

    private static TableEntity CreateSlot(string slotId, string status, string gameType, string gameDate, bool isAvailability = false)
    {
        return new TableEntity(Constants.Pk.Slots("LEAGUE-1", "AAA"), slotId)
        {
            ["Status"] = status,
            ["GameType"] = gameType,
            ["GameDate"] = gameDate,
            ["IsAvailability"] = isAvailability,
        };
    }

    private static TableEntity CreateCoachSlot(
        string slotId,
        string status,
        string gameType,
        string offeringTeamId,
        string? homeTeamId = null,
        string? awayTeamId = null)
    {
        return new TableEntity(Constants.Pk.Slots("LEAGUE-1", "AAA"), slotId)
        {
            ["Status"] = status,
            ["GameType"] = gameType,
            ["GameDate"] = "2099-01-15",
            ["StartTime"] = "18:00",
            ["EndTime"] = "20:00",
            ["DisplayName"] = "Main Field",
            ["FieldKey"] = "FIELD-1",
            ["OfferingTeamId"] = offeringTeamId,
            ["HomeTeamId"] = homeTeamId ?? "",
            ["AwayTeamId"] = awayTeamId ?? "",
        };
    }

    private sealed class TestHttpResponseData : HttpResponseData
    {
        public TestHttpResponseData(FunctionContext functionContext, HttpStatusCode statusCode)
            : base(functionContext)
        {
            StatusCode = statusCode;
            Headers = new HttpHeadersCollection();
            Body = new MemoryStream();
            Cookies = new Mock<HttpCookies>().Object;
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; }
        public override Stream Body { get; set; }
        public override HttpCookies Cookies { get; }
    }
}
