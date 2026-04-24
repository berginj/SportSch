import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";

import { createLogger } from "../lib/log";
import { loadCurrentPublicStatus, loadCurrentStatus } from "../lib/refresh";

app.http("healthStatus", {
  methods: ["GET"],
  authLevel: "anonymous",
  route: "status",
  handler: healthHandler,
});

app.http("operatorHealthStatus", {
  methods: ["GET"],
  authLevel: "function",
  route: "status/detail",
  handler: operatorHealthHandler,
});

async function healthHandler(_request: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> {
  const logger = createLogger(context);
  const status = await loadCurrentPublicStatus(logger);

  return {
    status: status.healthy ? 200 : 503,
    jsonBody: {
      serviceName: status.serviceName,
      state: status.state,
      lastSuccessfulRefresh: status.lastSuccessfulRefresh,
      lastAttemptedRefresh: status.lastAttemptedRefresh,
      sourceFeedCount: status.sourceFeedCount,
      configuredSourceFeedCount: status.configuredSourceFeedCount,
      uploadedSourceCount: status.uploadedSourceCount,
      mergedEventCount: status.mergedEventCount,
      candidateMergedEventCount: status.candidateMergedEventCount,
      calendarPublished: status.calendarPublished,
      servedLastKnownGood: status.servedLastKnownGood,
    },
  };
}

async function operatorHealthHandler(_request: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> {
  const logger = createLogger(context);
  const status = await loadCurrentStatus(logger);

  return {
    status: status.healthy ? 200 : 503,
    jsonBody: {
      serviceName: status.serviceName,
      state: status.state,
      lastSuccessfulRefresh: status.lastSuccessfulRefresh,
      lastAttemptedRefresh: status.lastAttemptedRefresh,
      sourceFeedCount: status.sourceFeedCount,
      configuredSourceFeedCount: status.configuredSourceFeedCount,
      uploadedSourceCount: status.uploadedSourceCount,
      mergedEventCount: status.mergedEventCount,
      candidateMergedEventCount: status.candidateMergedEventCount,
      output: status.output,
      errorSummary: status.errorSummary,
      sourceStatuses: status.sourceStatuses,
      calendarPublished: status.calendarPublished,
      servedLastKnownGood: status.servedLastKnownGood,
    },
  };
}
