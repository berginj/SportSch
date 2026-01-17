using System.Net;
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

public class DeleteEvent
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    private const string EventsTableName = Constants.Tables.Events;

    public DeleteEvent(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<DeleteEvent>();
        _svc = tableServiceClient;
    }

    [Function("DeleteEvent")]
    [OpenApiOperation(operationId: "DeleteEvent", tags: new[] { "Events" }, Summary = "Delete event", Description = "Deletes a calendar event. Only league admins can delete events.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiParameter(name: "eventId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Event identifier")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Event deleted successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object), Description = "Invalid eventId")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Forbidden, contentType: "application/json", bodyType: typeof(object), Description = "Only league admins can delete events")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "Event not found")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "events/{eventId}")] HttpRequestData req,
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

            var table = await TableClients.GetTableAsync(_svc, EventsTableName);
            var pk = Constants.Pk.Events(leagueId);
            try { await table.DeleteEntityAsync(pk, eventId); }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return ApiResponses.Error(req, HttpStatusCode.NotFound, "NOT_FOUND", "Event not found");
            }

            return ApiResponses.Ok(req, new { deleted = true });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (Exception ex)
        {
            _log.LogError(ex, "DeleteEvent failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
