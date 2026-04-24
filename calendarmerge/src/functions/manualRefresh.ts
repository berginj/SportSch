import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";

import { createLogger } from "../lib/log";
import { runRefresh } from "../lib/refresh";

app.http("manualRefresh", {
  methods: ["POST"],
  authLevel: "function",
  route: "refresh",
  handler: manualRefreshHandler,
});

async function manualRefreshHandler(
  _request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  const logger = createLogger(context);
  const result = await runRefresh(logger, "manual");

  return {
    status: result.inFlight ? 202 : result.status.state === "failed" ? 502 : 200,
    jsonBody: {
      success: result.status.state !== "failed",
      inFlight: result.inFlight,
      partialFailure: result.status.state === "partial",
      sourceFeedCount: result.status.sourceFeedCount,
      configuredSourceFeedCount: result.status.configuredSourceFeedCount,
      uploadedSourceCount: result.status.uploadedSourceCount,
      eventCount: result.status.mergedEventCount,
      candidateEventCount: result.candidateEventCount,
      sourceStatuses: result.status.sourceStatuses,
      output: result.status.output,
      servedLastKnownGood: result.usedLastKnownGood,
      calendarPublished: result.calendarPublished,
      lastAttemptedRefresh: result.status.lastAttemptedRefresh,
      lastSuccessfulRefresh: result.status.lastSuccessfulRefresh,
      errorSummary: result.status.errorSummary,
      state: result.status.state,
    },
  };
}
