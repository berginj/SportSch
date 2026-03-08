using System.Net;
using GameSwap.Functions.Models;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Functions;

public class FieldInventoryImportFunctions
{
    private readonly IFieldInventoryImportService _service;
    private readonly TableServiceClient _tableService;
    private readonly ILogger _log;

    public FieldInventoryImportFunctions(
        IFieldInventoryImportService service,
        TableServiceClient tableService,
        ILoggerFactory loggerFactory)
    {
        _service = service;
        _tableService = tableService;
        _log = loggerFactory.CreateLogger<FieldInventoryImportFunctions>();
    }

    [Function("InspectFieldInventoryWorkbook")]
    [OpenApiOperation(operationId: "InspectFieldInventoryWorkbook", tags: new[] { "Field Inventory Import" }, Summary = "Inspect a Google Sheets workbook", Description = "Loads workbook metadata from a public Google Sheets URL and infers parser/action for each tab.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryWorkbookInspectRequest), Required = true)]
    public async Task<HttpResponseData> InspectWorkbook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/workbook/inspect")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryWorkbookInspectRequest>();
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            }

            var context = CorrelationContext.FromRequest(req, leagueId);
            var response = await _service.InspectWorkbookAsync(body.SourceWorkbookUrl ?? "", context);
            return ApiResponses.Ok(req, response);
        });

    [Function("PreviewFieldInventoryImport")]
    [OpenApiOperation(operationId: "PreviewFieldInventoryImport", tags: new[] { "Field Inventory Import" }, Summary = "Parse and preview staged field inventory", Description = "Parses selected workbook tabs, creates staged records, warnings, and review queue items, and stores them separately from live records.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryPreviewRequest), Required = true)]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/preview")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryPreviewRequest>();
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            }

            var context = CorrelationContext.FromRequest(req, leagueId);
            var response = await _service.CreatePreviewAsync(body, context);
            return ApiResponses.Ok(req, response);
        });

    [Function("GetFieldInventoryImportRun")]
    [OpenApiOperation(operationId: "GetFieldInventoryImportRun", tags: new[] { "Field Inventory Import" }, Summary = "Get staged import run", Description = "Returns the latest staged records, warnings, review items, and canonical field choices for an import run.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> GetRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "field-inventory/runs/{runId}")] HttpRequestData req,
        string runId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var context = CorrelationContext.FromRequest(req, leagueId);
            var response = await _service.GetRunAsync(runId, context);
            return response is null
                ? ApiResponses.Error(req, HttpStatusCode.NotFound, ErrorCodes.NOT_FOUND, "Import run not found.")
                : ApiResponses.Ok(req, response);
        });

    [Function("StageFieldInventoryImportRun")]
    [OpenApiOperation(operationId: "StageFieldInventoryImportRun", tags: new[] { "Field Inventory Import" }, Summary = "Mark parsed results as staged", Description = "Stages a parsed import run without modifying live inventory records.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    public async Task<HttpResponseData> StageRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "field-inventory/runs/{runId}/stage")] HttpRequestData req,
        string runId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var context = CorrelationContext.FromRequest(req, leagueId);
            var response = await _service.StageRunAsync(runId, context);
            return ApiResponses.Ok(req, response);
        });

    [Function("SaveFieldInventoryFieldAlias")]
    [OpenApiOperation(operationId: "SaveFieldInventoryFieldAlias", tags: new[] { "Field Inventory Import" }, Summary = "Save field alias mapping", Description = "Persists a mapping between a raw sheet field name and a canonical SportsCH field, then rebuilds the preview.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryAliasSaveRequest), Required = true)]
    public async Task<HttpResponseData> SaveFieldAlias(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/field-aliases")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryAliasSaveRequest>();
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            }

            var context = CorrelationContext.FromRequest(req, leagueId);
            var response = await _service.SaveFieldAliasAsync(body, context);
            return ApiResponses.Ok(req, response);
        });

    [Function("SaveFieldInventoryTabClassification")]
    [OpenApiOperation(operationId: "SaveFieldInventoryTabClassification", tags: new[] { "Field Inventory Import" }, Summary = "Save tab classification", Description = "Persists a reusable parser/action decision for a workbook tab and rebuilds the preview.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryTabClassificationSaveRequest), Required = true)]
    public async Task<HttpResponseData> SaveTabClassification(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/tab-classifications")] HttpRequestData req)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryTabClassificationSaveRequest>();
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            }

            var context = CorrelationContext.FromRequest(req, leagueId);
            var response = await _service.SaveTabClassificationAsync(body, context);
            return ApiResponses.Ok(req, response);
        });

    [Function("UpdateFieldInventoryReviewItem")]
    [OpenApiOperation(operationId: "UpdateFieldInventoryReviewItem", tags: new[] { "Field Inventory Import" }, Summary = "Resolve or ignore a review queue item", Description = "Updates a review queue item, optionally saves the decision for future runs, and rebuilds the preview.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryReviewDecisionRequest), Required = true)]
    public async Task<HttpResponseData> UpdateReviewItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "field-inventory/runs/{runId}/review-items/{reviewItemId}")] HttpRequestData req,
        string runId,
        string reviewItemId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryReviewDecisionRequest>();
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            }

            var context = CorrelationContext.FromRequest(req, leagueId);
            var response = await _service.UpdateReviewItemAsync(runId, reviewItemId, body, context);
            return ApiResponses.Ok(req, response);
        });

    [Function("CommitFieldInventoryImportRun")]
    [OpenApiOperation(operationId: "CommitFieldInventoryImportRun", tags: new[] { "Field Inventory Import" }, Summary = "Run dry-run/import/upsert against live inventory storage", Description = "Runs the explicit import/upsert boundary. Dry runs only return a diff preview; live writes only happen when DryRun is false.")]
    [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
    [OpenApiRequestBody("application/json", typeof(FieldInventoryCommitRequest), Required = true)]
    public async Task<HttpResponseData> CommitRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "field-inventory/runs/{runId}/commit")] HttpRequestData req,
        string runId)
        => await ExecuteAsync(req, async () =>
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireLeagueAdminAsync(_tableService, me.UserId, leagueId);
            var body = await req.ReadFromJsonAsync<FieldInventoryCommitRequest>();
            if (body is null)
            {
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, ErrorCodes.BAD_REQUEST, "Invalid JSON body.");
            }

            var context = CorrelationContext.FromRequest(req, leagueId);
            var response = await _service.CommitRunAsync(runId, body, context);
            return ApiResponses.Ok(req, response);
        });

    private async Task<HttpResponseData> ExecuteAsync(HttpRequestData req, Func<Task<HttpResponseData>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiGuards.HttpError ex)
        {
            return ApiResponses.FromHttpError(req, ex);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Field inventory import request failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, ErrorCodes.INTERNAL_ERROR,
                "Field inventory import failed.",
                new { exception = ex.Message });
        }
    }
}
