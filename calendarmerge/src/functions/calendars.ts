import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";

import { BlobStore } from "../lib/blobStore";
import { getConfig } from "../lib/config";
import {
  BadRequestError,
  ConflictError,
  NotFoundError,
  PayloadTooLargeError,
  UnsupportedMediaTypeError,
} from "../lib/errors";
import { parseIcsCalendar } from "../lib/ics";
import { createLogger } from "../lib/log";
import { loadCurrentStatus, runRefresh } from "../lib/refresh";
import { SourceFeedConfig, UploadAction, UploadCalendarInput } from "../lib/types";
import {
  normalizeCalendarId,
  normalizeUploadCalendarInput,
  parseBooleanInput,
  parseUploadAction,
} from "../lib/uploads";
import { assertContentLengthWithinLimit, errorMessage, readTextStreamWithLimit } from "../lib/util";

type UploadCalendarRequestBody = Partial<UploadCalendarInput> & {
  action?: UploadAction | string;
  refresh?: boolean | string;
};

app.http("calendarSources", {
  methods: ["GET", "POST"],
  authLevel: "function",
  route: "calendars",
  handler: calendarsHandler,
});

app.http("calendarSourceById", {
  methods: ["DELETE"],
  authLevel: "function",
  route: "calendars/{id}",
  handler: deleteCalendarHandler,
});

async function calendarsHandler(request: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> {
  if (request.method === "GET") {
    return listCalendarsHandler(context);
  }

  return uploadCalendarHandler(request, context);
}

async function listCalendarsHandler(context: InvocationContext): Promise<HttpResponseInit> {
  const logger = createLogger(context);
  const config = getConfig();
  const store = new BlobStore(config);

  try {
    const [uploadedSources, status] = await Promise.all([store.listUploadedSources(), loadCurrentStatus(logger)]);
    const sources = [...config.sourceFeeds, ...uploadedSources].sort(compareSourceFeeds);

    return {
      status: 200,
      jsonBody: {
        serviceName: config.serviceName,
        sourceFeedCount: sources.length,
        configuredSourceFeedCount: config.sourceFeeds.length,
        uploadedSourceCount: uploadedSources.length,
        sources,
        lastAttemptedRefresh: status.lastAttemptedRefresh,
        lastSuccessfulRefresh: status.lastSuccessfulRefresh,
        output: status.output,
        errorSummary: status.errorSummary,
      },
    };
  } catch (error) {
    logger.error("calendar_sources_list_failed", { error: errorMessage(error) });

    return {
      status: 500,
      jsonBody: {
        success: false,
        error: `Failed to list calendars: ${errorMessage(error)}`,
      },
    };
  }
}

async function uploadCalendarHandler(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  const logger = createLogger(context);
  const config = getConfig();
  const store = new BlobStore(config);

  try {
    const { action, input, refreshRequested } = await parseUploadRequest(request, config.maxUploadBytes);
    const parsedEventCount = validateCalendarUpload(input);

    const uploadResult = await store.writeUploadedCalendar(input, action);
    let refreshResult;
    let refreshError: string | undefined;

    if (refreshRequested) {
      try {
        refreshResult = await runRefresh(logger, `upload:${uploadResult.source.id}`);
      } catch (error) {
        refreshError = errorMessage(error);
        logger.error("upload_refresh_failed", {
          sourceId: uploadResult.source.id,
          error: refreshError,
        });
      }
    }

    const refreshInFlight = Boolean(refreshRequested && refreshResult?.inFlight);
    const refreshSucceeded =
      !refreshRequested ||
      Boolean(!refreshError && refreshResult && !refreshResult.inFlight && refreshResult.status.state !== "failed");
    const statusCode = refreshInFlight ? 202 : uploadResult.created ? 201 : 200;

    return {
      status: statusCode,
      jsonBody: {
        success: true,
        requestedAction: action,
        appliedAction: uploadResult.action,
        created: uploadResult.created,
        parsedEventCount,
        source: uploadResult.record,
        refreshTriggered: refreshRequested,
        refreshInFlight,
        refreshSucceeded,
        refreshError,
        refresh: refreshResult
          ? {
              inFlight: refreshResult.inFlight,
              success: refreshResult.status.state !== "failed",
              state: refreshResult.status.state,
              sourceFeedCount: refreshResult.status.sourceFeedCount,
              configuredSourceFeedCount: refreshResult.status.configuredSourceFeedCount,
              uploadedSourceCount: refreshResult.status.uploadedSourceCount,
              eventCount: refreshResult.status.mergedEventCount,
              candidateEventCount: refreshResult.candidateEventCount,
              output: refreshResult.status.output,
              servedLastKnownGood: refreshResult.usedLastKnownGood,
              calendarPublished: refreshResult.calendarPublished,
              lastAttemptedRefresh: refreshResult.status.lastAttemptedRefresh,
              lastSuccessfulRefresh: refreshResult.status.lastSuccessfulRefresh,
              errorSummary: refreshResult.status.errorSummary,
            }
          : undefined,
      },
    };
  } catch (error) {
    if (error instanceof UnsupportedMediaTypeError) {
      return {
        status: 415,
        jsonBody: {
          success: false,
          error: error.message,
        },
      };
    }

    if (error instanceof PayloadTooLargeError) {
      return {
        status: 413,
        jsonBody: {
          success: false,
          error: error.message,
        },
      };
    }

    if (error instanceof ConflictError) {
      return {
        status: 409,
        jsonBody: {
          success: false,
          error: error.message,
        },
      };
    }

    if (error instanceof NotFoundError) {
      return {
        status: 404,
        jsonBody: {
          success: false,
          error: error.message,
        },
      };
    }

    if (error instanceof BadRequestError) {
      return {
        status: 400,
        jsonBody: {
          success: false,
          error: error.message,
        },
      };
    }

    logger.error("calendar_upload_failed", { error: errorMessage(error) });
    return {
      status: 500,
      jsonBody: {
        success: false,
        error: `Failed to upload calendar: ${errorMessage(error)}`,
      },
    };
  }
}

async function deleteCalendarHandler(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  const logger = createLogger(context);
  const config = getConfig();
  const store = new BlobStore(config);

  try {
    const { id, refreshRequested } = parseDeleteRequest(request);
    const deleteResult = await store.deleteUploadedCalendar(id);
    let refreshResult;
    let refreshError: string | undefined;

    if (refreshRequested) {
      try {
        refreshResult = await runRefresh(logger, `delete:${deleteResult.source.id}`);
      } catch (error) {
        refreshError = errorMessage(error);
        logger.error("delete_refresh_failed", {
          sourceId: deleteResult.source.id,
          error: refreshError,
        });
      }
    }

    const refreshInFlight = Boolean(refreshRequested && refreshResult?.inFlight);
    const refreshSucceeded =
      !refreshRequested ||
      Boolean(!refreshError && refreshResult && !refreshResult.inFlight && refreshResult.status.state !== "failed");

    return {
      status: refreshInFlight ? 202 : 200,
      jsonBody: {
        success: true,
        deleted: true,
        source: deleteResult.record,
        refreshTriggered: refreshRequested,
        refreshInFlight,
        refreshSucceeded,
        refreshError,
        refresh: refreshResult
          ? {
              inFlight: refreshResult.inFlight,
              success: refreshResult.status.state !== "failed",
              state: refreshResult.status.state,
              sourceFeedCount: refreshResult.status.sourceFeedCount,
              configuredSourceFeedCount: refreshResult.status.configuredSourceFeedCount,
              uploadedSourceCount: refreshResult.status.uploadedSourceCount,
              eventCount: refreshResult.status.mergedEventCount,
              candidateEventCount: refreshResult.candidateEventCount,
              output: refreshResult.status.output,
              servedLastKnownGood: refreshResult.usedLastKnownGood,
              calendarPublished: refreshResult.calendarPublished,
              lastAttemptedRefresh: refreshResult.status.lastAttemptedRefresh,
              lastSuccessfulRefresh: refreshResult.status.lastSuccessfulRefresh,
              errorSummary: refreshResult.status.errorSummary,
            }
          : undefined,
      },
    };
  } catch (error) {
    if (error instanceof ConflictError) {
      return {
        status: 409,
        jsonBody: {
          success: false,
          error: error.message,
        },
      };
    }

    if (error instanceof NotFoundError) {
      return {
        status: 404,
        jsonBody: {
          success: false,
          error: error.message,
        },
      };
    }

    if (error instanceof BadRequestError) {
      return {
        status: 400,
        jsonBody: {
          success: false,
          error: error.message,
        },
      };
    }

    logger.error("calendar_delete_failed", { error: errorMessage(error) });
    return {
      status: 500,
      jsonBody: {
        success: false,
        error: `Failed to delete calendar: ${errorMessage(error)}`,
      },
    };
  }
}

async function parseUploadRequest(
  request: HttpRequest,
  maxUploadBytes: number,
): Promise<{ action: UploadAction; input: ReturnType<typeof normalizeUploadCalendarInput>; refreshRequested: boolean }> {
  const contentType = request.headers.get("content-type")?.split(";")[0]?.trim().toLowerCase() ?? "";
  const queryRefresh = request.query.get("refresh") ?? request.headers.get("x-refresh-now") ?? undefined;
  const queryAction = request.query.get("action") ?? request.headers.get("x-calendar-action") ?? undefined;

  if (!contentType || contentType === "application/json") {
    let rawBody = "";

    try {
      rawBody = await readRequestText(request, maxUploadBytes);
    } catch (error) {
      if (error instanceof PayloadTooLargeError) {
        throw error;
      }

      throw new BadRequestError(`Request body could not be read: ${errorMessage(error)}`);
    }

    let body: UploadCalendarRequestBody;
    try {
      body = JSON.parse(rawBody) as UploadCalendarRequestBody;
    } catch (error) {
      throw new BadRequestError(`Request body must be valid JSON: ${errorMessage(error)}`);
    }

    try {
      return {
        action: parseUploadAction(queryAction ?? body.action, "upsert"),
        input: normalizeUploadCalendarInput(body),
        refreshRequested: parseBooleanInput(queryRefresh ?? body.refresh, true),
      };
    } catch (error) {
      throw new BadRequestError(errorMessage(error));
    }
  }

  if (["text/calendar", "text/plain", "application/octet-stream"].includes(contentType)) {
    try {
      return {
        action: parseUploadAction(queryAction, "upsert"),
        input: normalizeUploadCalendarInput({
          id: request.query.get("id") ?? request.headers.get("x-calendar-id") ?? undefined,
          name: request.query.get("name") ?? request.headers.get("x-calendar-name") ?? undefined,
          calendarText: await readRequestText(request, maxUploadBytes),
        }),
        refreshRequested: parseBooleanInput(queryRefresh, true),
      };
    } catch (error) {
      if (error instanceof PayloadTooLargeError) {
        throw error;
      }

      throw new BadRequestError(errorMessage(error));
    }
  }

  throw new UnsupportedMediaTypeError(
    "Use application/json with calendarText, or send raw ICS with content-type text/calendar.",
  );
}

async function readRequestText(request: HttpRequest, maxUploadBytes: number): Promise<string> {
  try {
    assertContentLengthWithinLimit(request.headers.get("content-length"), maxUploadBytes, "Request body");
    return await readTextStreamWithLimit(request.body, maxUploadBytes, "Request body");
  } catch (error) {
    const message = errorMessage(error);
    if (message.includes("configured limit")) {
      throw new PayloadTooLargeError(message);
    }

    throw error;
  }
}

function parseDeleteRequest(request: HttpRequest): { id: string; refreshRequested: boolean } {
  try {
    return {
      id: normalizeCalendarId(
        request.params.id ?? request.query.get("id") ?? request.headers.get("x-calendar-id") ?? undefined,
      ),
      refreshRequested: parseBooleanInput(
        request.query.get("refresh") ?? request.headers.get("x-refresh-now") ?? undefined,
        true,
      ),
    };
  } catch (error) {
    throw new BadRequestError(errorMessage(error));
  }
}

function validateCalendarUpload(input: ReturnType<typeof normalizeUploadCalendarInput>): number {
  try {
    return parseIcsCalendar(input.calendarText, buildValidationSource(input.id, input.name)).length;
  } catch (error) {
    throw new BadRequestError(`Uploaded ICS is invalid: ${errorMessage(error)}`);
  }
}

function buildValidationSource(id: string, name: string): SourceFeedConfig {
  return {
    id,
    name,
    kind: "uploaded",
    url: `upload://${id}`,
    blobPath: `${id}.ics`,
  };
}

function compareSourceFeeds(left: SourceFeedConfig, right: SourceFeedConfig): number {
  const nameCompare = left.name.localeCompare(right.name);
  if (nameCompare !== 0) {
    return nameCompare;
  }

  return left.id.localeCompare(right.id);
}
