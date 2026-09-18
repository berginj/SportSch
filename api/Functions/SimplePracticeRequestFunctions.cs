using System.Net;
using GameSwap.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace GameSwap.Functions.Functions;

// Deliberately fail closed for stale clients. No incompatible practice writes.
public class SimplePracticeRequestFunctions
{
    [Function("CheckPracticeConflicts")]
    public Task<HttpResponseData> CheckConflicts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "practice/check-conflicts")] HttpRequestData req)
        => Retired(req);

    [Function("CreateSimplePracticeRequest")]
    public Task<HttpResponseData> CreateSimpleRequest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "practice/requests")] HttpRequestData req)
        => Retired(req);

    private static Task<HttpResponseData> Retired(HttpRequestData req) => Task.FromResult(ApiResponses.Error(req,
        HttpStatusCode.Gone, "PRACTICE_WORKFLOW_RETIRED",
        "Reload the calendar and use the field-inventory practice workflow. This endpoint no longer creates reservations."));
}
