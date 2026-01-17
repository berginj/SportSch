using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace GameSwap.Functions.Storage;

public static class ApiResponses
{
    public static HttpResponseData Ok(HttpRequestData req, object data, HttpStatusCode status = HttpStatusCode.OK)
        => HttpUtil.Json(req, status, new { data });

    public static HttpResponseData Error(HttpRequestData req, HttpStatusCode status, string code, string message, object? details = null)
    {
        if (details is null)
            return HttpUtil.Json(req, status, new { error = new { code, message } });

        return HttpUtil.Json(req, status, new { error = new { code, message, details } });
    }

    public static HttpResponseData FromHttpError(HttpRequestData req, ApiGuards.HttpError ex)
    {
        var status = (HttpStatusCode)ex.Status;

        // Use the explicit error code if provided, otherwise fall back to generic codes
        var code = ex.Code ?? (status switch
        {
            HttpStatusCode.BadRequest => ErrorCodes.BAD_REQUEST,
            HttpStatusCode.Unauthorized => ErrorCodes.UNAUTHENTICATED,
            HttpStatusCode.Forbidden => ErrorCodes.FORBIDDEN,
            HttpStatusCode.NotFound => ErrorCodes.NOT_FOUND,
            HttpStatusCode.Conflict => ErrorCodes.CONFLICT,
            _ => ErrorCodes.INTERNAL_ERROR
        });

        return Error(req, status, code, ex.Message);
    }
}
