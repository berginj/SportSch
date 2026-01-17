using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;
using GameSwap.Functions.Services;
using GameSwap.Functions.Telemetry;

namespace GameSwap.Functions.Functions;

/// <summary>
/// Azure Function for creating slots.
/// Refactored to use service layer for business logic.
/// </summary>
public class CreateSlot
{
    private readonly ISlotService _slotService;
    private readonly ILogger _log;

    public CreateSlot(ISlotService slotService, ILoggerFactory lf)
    {
        _slotService = slotService;
        _log = lf.CreateLogger<CreateSlot>();
    }

    public record CreateSlotReq(
        string? division,
        string? offeringTeamId,
        string? gameDate,
        string? startTime,
        string? endTime,
        string? fieldKey,
        string? parkName,
        string? fieldName,
        string? offeringEmail,
        string? gameType,
        string? notes
    );

    [Function("CreateSlot")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "slots")] HttpRequestData req)
    {
        try
        {
            // Extract request context
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            // Note: Authorization is now handled by the service layer

            var body = await HttpUtil.ReadJsonAsync<CreateSlotReq>(req);
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body");
            }

            // Build correlation context for distributed tracing
            var context = CorrelationContext.FromRequest(req, leagueId);

            // Build service request from HTTP request
            var serviceRequest = new Services.CreateSlotRequest
            {
                Division = (body.division ?? "").Trim(),
                OfferingTeamId = (body.offeringTeamId ?? "").Trim(),
                OfferingEmail = body.offeringEmail ?? me.Email,
                GameDate = (body.gameDate ?? "").Trim(),
                StartTime = (body.startTime ?? "").Trim(),
                EndTime = (body.endTime ?? "").Trim(),
                FieldKey = (body.fieldKey ?? "").Trim(),
                ParkName = body.parkName,
                FieldName = body.fieldName,
                GameType = string.IsNullOrWhiteSpace(body.gameType) ? "Swap" : body.gameType!.Trim(),
                Notes = body.notes
            };

            // Delegate to service layer (all business logic is in the service)
            var result = await _slotService.CreateSlotAsync(serviceRequest, context);

            // Track telemetry
            UsageTelemetry.Track(_log, "api_slot_create", leagueId, me.UserId, new
            {
                division = serviceRequest.Division,
                fieldKey = serviceRequest.FieldKey,
                gameDate = serviceRequest.GameDate
            });

            return ApiResponses.Ok(req, result, HttpStatusCode.Created);
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateSlot failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred");
        }
    }
}
