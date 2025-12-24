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
        var code = status switch
        {
            HttpStatusCode.BadRequest => "BAD_REQUEST",
            HttpStatusCode.Unauthorized => "UNAUTHENTICATED",
            HttpStatusCode.Forbidden => "FORBIDDEN",
            HttpStatusCode.NotFound => "NOT_FOUND",
            HttpStatusCode.Conflict => "CONFLICT",
            _ => "INTERNAL"
        };

        return Error(req, status, code, ex.Message);
    }
}
