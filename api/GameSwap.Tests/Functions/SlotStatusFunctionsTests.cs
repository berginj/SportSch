using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using GameSwap.Functions.Functions;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GameSwap.Tests.Functions;

public class SlotStatusFunctionsTests
{
    [Fact]
    public async Task UpdateSlotStatus_CancelledSlot_NotifiesConfirmedTeamWhenAwayTeamIsBlank()
    {
        var leagueId = "LEAGUE-1";
        var division = "AAA";
        var slotId = "slot-1";
        var sentEmails = new List<string>();

        var slot = new TableEntity(Constants.Pk.Slots(leagueId, division), slotId)
        {
            ["Status"] = Constants.Status.SlotConfirmed,
            ["HomeTeamId"] = "TEAM-A",
            ["AwayTeamId"] = "",
            ["ConfirmedTeamId"] = "TEAM-B",
            ["GameDate"] = "2026-06-15",
            ["StartTime"] = "18:00",
            ["DisplayName"] = "Diamond 1",
        };
        slot.ETag = new ETag("etag-1");

        var tableService = new Mock<TableServiceClient>();
        var membershipRepo = new Mock<IMembershipRepository>();
        var slotRepo = new Mock<ISlotRepository>();
        var emailService = new Mock<IEmailService>();
        var slotsTable = CreateTableClient();
        var teamsTable = CreateTableClient();

        tableService.Setup(x => x.GetTableClient(Constants.Tables.Slots)).Returns(slotsTable.Object);
        tableService.Setup(x => x.GetTableClient(Constants.Tables.Teams)).Returns(teamsTable.Object);

        membershipRepo.Setup(x => x.IsGlobalAdminAsync("global-admin")).ReturnsAsync(true);
        slotRepo.Setup(x => x.GetSlotAsync(leagueId, division, slotId)).ReturnsAsync(slot);

        slotsTable
            .Setup(x => x.UpdateEntityAsync(It.IsAny<TableEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        teamsTable
            .Setup(x => x.GetEntityAsync<TableEntity>(Constants.Pk.Teams(leagueId, division), "TEAM-A", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntityResponse(new TableEntity(Constants.Pk.Teams(leagueId, division), "TEAM-A")
            {
                ["PrimaryContactEmail"] = "home@example.com",
            }));
        teamsTable
            .Setup(x => x.GetEntityAsync<TableEntity>(Constants.Pk.Teams(leagueId, division), "TEAM-B", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntityResponse(new TableEntity(Constants.Pk.Teams(leagueId, division), "TEAM-B")
            {
                ["PrimaryContactEmail"] = "away@example.com",
            }));

        emailService
            .Setup(x => x.SendGameCancelledEmailAsync(It.IsAny<string>(), leagueId, "2026-06-15", "18:00", "Diamond 1", "Weather"))
            .Callback<string, string, string, string, string, string>((to, _, _, _, _, _) => sentEmails.Add(to))
            .Returns(Task.CompletedTask);

        var function = new SlotStatusFunctions(
            tableService.Object,
            membershipRepo.Object,
            slotRepo.Object,
            emailService.Object,
            CreateLoggerFactory());

        var response = await function.UpdateSlotStatus(
            CreatePatchRequest(
                "http://localhost:7071/api/slots/AAA/slot-1/status",
                "global-admin",
                leagueId,
                new { status = Constants.Status.SlotCancelled, reason = "Weather" }),
            division,
            slotId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("home@example.com", sentEmails);
        Assert.Contains("away@example.com", sentEmails);
        Assert.Equal(2, sentEmails.Count);

        using var json = await ReadJsonAsync(response);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(Constants.Status.SlotCancelled, data.GetProperty("newStatus").GetString());
    }

    private static Mock<TableClient> CreateTableClient()
    {
        var tableClient = new Mock<TableClient>();
        tableClient
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<TableItem>>());
        return tableClient;
    }

    private static Response<TableEntity> CreateEntityResponse(TableEntity entity)
    {
        var response = new Mock<Response<TableEntity>>();
        response.SetupGet(x => x.Value).Returns(entity);
        return response.Object;
    }

    private static HttpRequestData CreatePatchRequest(string url, string userId, string leagueId, object body)
    {
        var context = new Mock<FunctionContext>();
        context.SetupGet(x => x.InvocationId).Returns(Guid.NewGuid().ToString());

        var request = new TestHttpRequestData(
            context.Object,
            new Uri(url),
            new HttpHeadersCollection
            {
                { "x-user-id", userId },
                { Constants.LEAGUE_HEADER_NAME, leagueId },
            },
            "PATCH");

        var json = JsonSerializer.Serialize(body);
        var bytes = Encoding.UTF8.GetBytes(json);
        request.Body.Write(bytes, 0, bytes.Length);
        request.Body.Position = 0;
        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonDocument.Parse(body);
    }

    private static ILoggerFactory CreateLoggerFactory()
        => LoggerFactory.Create(_ => { });

    private sealed class TestHttpRequestData : HttpRequestData
    {
        public TestHttpRequestData(FunctionContext functionContext, Uri url, HttpHeadersCollection headers, string method)
            : base(functionContext)
        {
            Url = url;
            Headers = headers;
            Body = new MemoryStream();
            Cookies = Array.Empty<IHttpCookie>();
            Identities = Array.Empty<ClaimsIdentity>();
            Method = method;
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
