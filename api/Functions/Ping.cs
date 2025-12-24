using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Functions;

public class Ping
{
    [Function("Ping")]
    public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")] HttpRequestData req)
        => HttpUtil.Text(req, HttpStatusCode.OK, "pong");
}
