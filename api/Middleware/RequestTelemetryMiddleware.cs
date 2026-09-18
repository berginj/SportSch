using System.Diagnostics;
using System.Net;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace GameSwap.Functions.Middleware;

public sealed class RequestTelemetryMiddleware(ILogger<RequestTelemetryMiddleware> logger) : IFunctionsWorkerMiddleware
{
    private static int _hasHandledRequest;

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var request = await context.GetHttpRequestDataAsync();
        if (request is null) { await next(context); return; }
        var correlationId = request.Headers.TryGetValues("x-correlation-id", out var values)
            && Guid.TryParse(values.FirstOrDefault(), out var supplied) ? supplied.ToString("D") : Guid.NewGuid().ToString("D");
        var firstRequest = Interlocked.Exchange(ref _hasHandledRequest, 1) == 0;
        var timer = Stopwatch.StartNew();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["FunctionName"] = context.FunctionDefinition.Name,
            ["InvocationId"] = context.InvocationId
        });
        try
        {
            await next(context);
        }
        catch (ApiGuards.HttpError error)
        {
            context.GetInvocationResult().Value = ApiResponses.FromHttpError(request, error);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Unhandled API failure");
            context.GetInvocationResult().Value = ApiResponses.Error(request, HttpStatusCode.InternalServerError,
                ErrorCodes.INTERNAL_ERROR, "An unexpected error occurred. Provide the request ID when contacting support.");
        }
        finally
        {
            var response = context.GetInvocationResult().Value as HttpResponseData;
            response?.Headers.Add("x-correlation-id", correlationId);
            logger.LogInformation("api_request {Endpoint} {Method} {StatusCode} {DurationMs} {FirstRequestInProcess}",
                context.FunctionDefinition.Name, request.Method, (int?)response?.StatusCode ?? 500,
                timer.Elapsed.TotalMilliseconds, firstRequest);
        }
    }
}
