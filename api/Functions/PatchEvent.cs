using System.Net;
using Azure;
using Azure.Data.Tables;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class PatchEvent
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string EventsTableName = Constants.Tables.Events;

    public PatchEvent(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<PatchEvent>();
        _svc = tableServiceClient;
    }

    public record PatchEventReq(
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

    [Function("PatchEvent")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "events/{eventId}")] HttpRequestData req,
        string eventId)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            // Events are managed by LeagueAdmin/global admin.
            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            eventId = (eventId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(eventId))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "eventId is required");

            var body = await HttpUtil.ReadJsonAsync<PatchEventReq>(req);
            if (body is null) return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Invalid JSON body");

            // Prevent clearing required fields via PATCH.
            if (body.eventDate != null && string.IsNullOrWhiteSpace(body.eventDate))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "eventDate is required");
            if (body.startTime != null && string.IsNullOrWhiteSpace(body.startTime))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "startTime is required");
            if (body.endTime != null && string.IsNullOrWhiteSpace(body.endTime))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "endTime is required");

            var table = await TableClients.GetTableAsync(_svc, EventsTableName);
            var pk = Constants.Pk.Events(leagueId);
            TableEntity entity;
            try { entity = (await table.GetEntityAsync<TableEntity>(pk, eventId)).Value; }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Event not found");
            }

            // No per-creator edit permissions; LeagueAdmin/global admin governs.

            void SetIfNotNull(string key, string? value)
            {
                if (value is null) return;
                entity[key] = value.Trim();
            }

            SetIfNotNull("Type", body.type);
            SetIfNotNull("Division", body.division);
            SetIfNotNull("TeamId", body.teamId);
            SetIfNotNull("Title", body.title);
            SetIfNotNull("EventDate", body.eventDate);
            SetIfNotNull("StartTime", body.startTime);
            SetIfNotNull("EndTime", body.endTime);
            SetIfNotNull("Location", body.location);
            SetIfNotNull("Notes", body.notes);

            var finalEventDate = (entity.GetString("EventDate") ?? "").Trim();
            var finalStartTime = (entity.GetString("StartTime") ?? "").Trim();
            var finalEndTime = (entity.GetString("EndTime") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(finalEventDate) || string.IsNullOrWhiteSpace(finalStartTime) || string.IsNullOrWhiteSpace(finalEndTime))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "eventDate, startTime, and endTime are required");

            entity["UpdatedUtc"] = DateTimeOffset.UtcNow;

            await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge);

            return ApiResponses.Ok(req, new
            {
                eventId,
                type = (entity.GetString("Type") ?? "").Trim(),
                status = (entity.GetString("Status") ?? "").Trim(),
                opponentTeamId = (entity.GetString("OpponentTeamId") ?? "").Trim(),
                acceptedByUserId = (entity.GetString("AcceptedByUserId") ?? "").Trim(),
                division = (entity.GetString("Division") ?? "").Trim(),
                teamId = (entity.GetString("TeamId") ?? "").Trim(),
                title = (entity.GetString("Title") ?? "").Trim(),
                eventDate = (entity.GetString("EventDate") ?? "").Trim(),
                startTime = (entity.GetString("StartTime") ?? "").Trim(),
                endTime = (entity.GetString("EndTime") ?? "").Trim(),
                location = (entity.GetString("Location") ?? "").Trim(),
                notes = (entity.GetString("Notes") ?? "").Trim(),
                createdByUserId = (entity.GetString("CreatedByUserId") ?? entity.GetString("CreatedBy") ?? "").Trim(),
                createdUtc = entity.TryGetValue("CreatedUtc", out var cu2) ? (cu2?.ToString() ?? "") : "",
                updatedUtc = entity.TryGetValue("UpdatedUtc", out var uu2) ? (uu2?.ToString() ?? "") : ""
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "PatchEvent failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
