using System.Net;
using System.Globalization;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class GetEvents
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string EventsTableName = Constants.Tables.Events;

    public GetEvents(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<GetEvents>();
        _svc = tableServiceClient;
    }

    public record EventDto(
        string eventId,
        string type,
        string status,
        string division,
        string teamId,
        string opponentTeamId,
        string title,
        string eventDate,
        string startTime,
        string endTime,
        string location,
        string notes,
        string createdByUserId,
        string acceptedByUserId,
        string createdUtc,
        string updatedUtc
    );

    [Function("GetEvents")]
    [OpenApiOperation(operationId: "GetEvents", tags: new[] { "Events" }, Summary = "Get events", Description = "Retrieves calendar events (practices, meetings, etc.) for a league. Supports filtering by division and date range. League members only.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "division", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter by division code (includes league-wide events)")]
    [OpenApiParameter(name: "dateFrom", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter events on or after this date (YYYY-MM-DD)")]
    [OpenApiParameter(name: "dateTo", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Filter events on or before this date (YYYY-MM-DD)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Events retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Not a league member")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "events")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireMemberAsync(_svc, me.UserId, leagueId);

            var division = (ApiGuards.GetQueryParam(req, "division") ?? "").Trim();
            var dateFrom = (ApiGuards.GetQueryParam(req, "dateFrom") ?? "").Trim();
            var dateTo = (ApiGuards.GetQueryParam(req, "dateTo") ?? "").Trim();

            var table = await TableClients.GetTableAsync(_svc, EventsTableName);
            var pk = Constants.Pk.Events(leagueId);
            var filters = new List<string> { ODataFilterBuilder.PartitionKeyExact(pk) };
            if (!string.IsNullOrWhiteSpace(dateFrom) || !string.IsNullOrWhiteSpace(dateTo))
            {
                filters.Add(ODataFilterBuilder.DateRange("EventDate", dateFrom, dateTo));
            }
            if (!string.IsNullOrWhiteSpace(division))
            {
                filters.Add(ODataFilterBuilder.Or(
                    ODataFilterBuilder.PropertyEquals("Division", division),
                    ODataFilterBuilder.PropertyEquals("Division", "")));
            }

            var filter = ODataFilterBuilder.And(filters.ToArray());

            var list = new List<EventDto>();
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter))
            {
                var eventDivision = ReadString(e, "Division");
                var eventDate = ReadString(e, "EventDate");
                var createdUtc = e.TryGetValue("CreatedUtc", out var cu) ? (cu?.ToString() ?? "") : "";
                var updatedUtc = e.TryGetValue("UpdatedUtc", out var uu) ? (uu?.ToString() ?? "") : "";
                list.Add(new EventDto(
                    eventId: e.RowKey,
                    type: ReadString(e, "Type"),
                    status: ReadString(e, "Status"),
                    division: eventDivision,
                    teamId: ReadString(e, "TeamId"),
                    opponentTeamId: ReadString(e, "OpponentTeamId"),
                    title: ReadString(e, "Title"),
                    eventDate: eventDate,
                    startTime: ReadString(e, "StartTime"),
                    endTime: ReadString(e, "EndTime"),
                    location: ReadString(e, "Location"),
                    notes: ReadString(e, "Notes"),
                    createdByUserId: ReadString(e, "CreatedByUserId"),
                    acceptedByUserId: ReadString(e, "AcceptedByUserId"),
                    createdUtc: createdUtc,
                    updatedUtc: updatedUtc
                ));
            }

            return ApiResponses.Ok(req, list
                .OrderBy(x => x.eventDate)
                .ThenBy(x => x.startTime)
                .ThenBy(x => x.title)
                .ToList());
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetEvents failed");
            var requestId = req.FunctionContext.InvocationId.ToString();
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR,
                "An unexpected error occurred",
                new { requestId, exception = ex.GetType().Name, detail = ex.Message });
        }
    }

    private static string ReadString(TableEntity e, string propertyName)
    {
        if (!e.TryGetValue(propertyName, out var raw) || raw is null) return "";
        return (raw?.ToString() ?? "").Trim();
    }

}
