using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace GameSwap.Functions.Storage;

public static class HttpUtil
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public static HttpResponseData Text(HttpRequestData req, HttpStatusCode status, string message)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        resp.WriteString(message ?? "");
        return resp;
    }

    public static HttpResponseData Json(HttpRequestData req, HttpStatusCode status, object body)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
        resp.WriteString(JsonSerializer.Serialize(body, _jsonOpts));
        return resp;
    }

    public static async Task<string> ReadBodyAsStringAsync(HttpRequestData req)
    {
        using var reader = new StreamReader(req.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    public static async Task<T?> ReadJsonAsync<T>(HttpRequestData req)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(req.Body, _jsonOpts);
        }
        catch
        {
            return default;
        }
    }
}
