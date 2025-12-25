using System.Net;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Functions;

public class StorageHealth
{
    private readonly TableServiceClient _svc;
    private readonly ILogger _log;

    public StorageHealth(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<StorageHealth>();
        _svc = tableServiceClient;
    }

    [Function("StorageHealthAdmin")]
    public Task<HttpResponseData> RunAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/storage/health")] HttpRequestData req)
        => RunInternalAsync(req);

    [Function("StorageHealth")]
    public Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "storage/health")] HttpRequestData req)
        => RunInternalAsync(req);

    private async Task<HttpResponseData> RunInternalAsync(HttpRequestData req)
    {
        try
        {
            var me = IdentityUtil.GetMe(req);
            await ApiGuards.RequireGlobalAdminAsync(_svc, me);

            var tables = new List<string>();
            await foreach (var table in _svc.QueryAsync(maxPerPage: 5))
            {
                tables.Add(table.Name);
            }

            var props = await _svc.GetPropertiesAsync();

            return ApiResponses.Ok(req, new
            {
                ok = true,
                tables,
                corsRules = props.Value.Cors?.Count ?? 0
            });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            _log.LogError(ex, "StorageHealth failed");
            var status = ex.Status > 0 ? (HttpStatusCode)ex.Status : HttpStatusCode.BadGateway;
            return ApiResponses.Error(req, status, "STORAGE_ERROR", ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "StorageHealth failed");
            return ApiResponses.Error(req, HttpStatusCode.InternalServerError, "INTERNAL", "Internal Server Error");
        }
    }
}
