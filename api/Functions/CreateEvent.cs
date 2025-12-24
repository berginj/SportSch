using System.Net;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class CreateEvent
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string EventsTableName = Constants.Tables.Events;

    public CreateEvent(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<CreateEvent>();
        _svc = tableServiceClient;
    }

    public record CreateEventReq(
        string? type,
        string? division,
        string? teamId,
        string? title,
        string? eventDate,
        string? startTime,
        string? endTime,
        string? location,
        string? notes
    );

    [Function("CreateEvent")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "events")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            // Events are league calendar items (practices/meetings/etc.).
            // Game requests are represented as Slots, not Events.
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var isGlobalAdmin = await ApiGuards.IsGlobalAdminAsync(_svc, me.UserId);
            var mem = isGlobalAdmin ? null : await ApiGuards.GetMembershipAsync(_svc, me.UserId, leagueId);
            var role = isGlobalAdmin ? Constants.Roles.LeagueAdmin : ApiGuards.GetRole(mem);

            var body = await HttpUtil.ReadJsonAsync<CreateEventReq>(req);
            if (body is null) return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            var requestedType = (body.type ?? "").Trim();
            var division = (body.division ?? "").Trim();
            var teamId = (body.teamId ?? "").Trim();
            var title = (body.title ?? "").Trim();
            var eventDate = (body.eventDate ?? "").Trim();
            var startTime = (body.startTime ?? "").Trim();
            var endTime = (body.endTime ?? "").Trim();
            var location = (body.location ?? "").Trim();
            var notes = (body.notes ?? "").Trim();

            // Admin defaults
            var type = requestedType;
            if (string.IsNullOrWhiteSpace(type)) type = Constants.EventTypes.Other;
            if (string.Equals(type, "GameRequest", StringComparison.OrdinalIgnoreCase))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "GameRequest events are not supported. Create a Slot to offer an open game.");

            if (string.IsNullOrWhiteSpace(eventDate) || string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "eventDate, startTime, and endTime are required");
            if (string.IsNullOrWhiteSpace(title))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "title is required");

            var table = await TableClients.GetTableAsync(_svc, EventsTableName);
            var eventId = "evt_" + Guid.NewGuid().ToString("N");
            var pk = Constants.Pk.Events(leagueId);
            var now = DateTimeOffset.UtcNow;

            var entity = new TableEntity(pk, eventId)
            {
                ["LeagueId"] = leagueId,
                ["EventId"] = eventId,
                ["Type"] = type,
                ["Status"] = Constants.Status.EventScheduled,
                ["Division"] = division,
                ["TeamId"] = teamId,
                ["Title"] = title,
                ["EventDate"] = eventDate,
                ["StartTime"] = startTime,
                ["EndTime"] = endTime,
                ["Location"] = location,
                ["Notes"] = notes,
                // Keep both keys for compatibility.
                ["CreatedBy"] = me.UserId,
                ["CreatedByUserId"] = me.UserId,
                ["CreatedByRole"] = role,
                ["CreatedUtc"] = now,
                ["UpdatedUtc"] = now
            };

            await table.AddEntityAsync(entity);

            return ApiResponses.Ok(req, new
            {
                eventId,
                type,
                status = Constants.Status.EventScheduled,
                division,
                teamId,
                title,
                eventDate,
                startTime,
                endTime,
                location,
                notes,
                createdByUserId = me.UserId,
                createdUtc = now,
                updatedUtc = now
            }, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateEvent failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
