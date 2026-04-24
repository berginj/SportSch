import { BlobStore } from "./blobStore";
import { getConfig } from "./config";
import { fetchFeed } from "./fetchFeeds";
import { serializeCalendar } from "./ics";
import { Logger } from "./log";
import { mergeFeedEvents } from "./merge";
import { buildPublicCalendarArtifacts } from "./publicCalendars";
import {
  buildStartingPublicStatus,
  buildStartingStatus,
  normalizePublicStatus,
  normalizeStatus,
  toPublicStatus,
} from "./status";
import { PublicServiceStatus, RefreshResult, ServiceStatus, SourceFeedConfig } from "./types";
import { errorMessage, buildOutputPaths } from "./util";

let activeRefresh: Promise<RefreshResult> | undefined;

interface UploadedSourcesLoadResult {
  sources: SourceFeedConfig[];
  error?: string;
}

export async function runRefresh(logger: Logger, reason: string): Promise<RefreshResult> {
  if (!activeRefresh) {
    activeRefresh = executeRefresh(logger, reason).finally(() => {
      activeRefresh = undefined;
    });
  } else {
    logger.info("refresh_reused_inflight_run", { reason });
  }

  return activeRefresh;
}

export async function loadCurrentStatus(logger: Logger): Promise<ServiceStatus> {
  const config = getConfig();
  const store = new BlobStore(config);
  const uploadedSources = await safeListUploadedSources(store, logger);
  const fallback = buildStartingStatus(config, uploadedSources.sources.length);

  try {
    const storedStatus = await store.readOperatorStatus();
    const status = storedStatus
      ? normalizeStatus(config, storedStatus, uploadedSources.sources.length)
      : fallback;

    if (!uploadedSources.error) {
      return status;
    }

    return {
      ...status,
      healthy: false,
      errorSummary: [...status.errorSummary, uploadedSources.error],
    };
  } catch (error) {
    logger.error("status_read_failed", { error: errorMessage(error) });

    return {
      ...fallback,
      errorSummary: [
        ...fallback.errorSummary,
        ...(uploadedSources.error ? [uploadedSources.error] : []),
        `Failed to read operator status: ${errorMessage(error)}`,
      ],
    };
  }
}

export async function loadCurrentPublicStatus(logger: Logger): Promise<PublicServiceStatus> {
  const config = getConfig();
  const store = new BlobStore(config);
  const uploadedSources = await safeListUploadedSources(store, logger);
  const fallback = buildStartingPublicStatus(config, uploadedSources.sources.length);

  try {
    const storedStatus = await store.readPublicStatus();
    const status = storedStatus
      ? normalizePublicStatus(config, storedStatus, uploadedSources.sources.length)
      : fallback;

    return uploadedSources.error
      ? {
          ...status,
          healthy: false,
        }
      : status;
  } catch (error) {
    logger.error("public_status_read_failed", { error: errorMessage(error) });

    return {
      ...fallback,
      healthy: false,
    };
  }
}

async function executeRefresh(logger: Logger, reason: string): Promise<RefreshResult> {
  const config = getConfig();
  const store = new BlobStore(config);
  const attemptTimestamp = new Date().toISOString();
  const uploadedSources = await safeListUploadedSources(store, logger);
  const allSources = [...config.sourceFeeds, ...uploadedSources.sources];
  const previousStatus = await safeReadStatus(store, logger, config.serviceName, config, uploadedSources.sources.length);
  const previousCalendarExists = await safeCalendarExists(store, logger);
  const refreshLease = await store.tryAcquireRefreshLock();

  if (!refreshLease) {
    logger.info("refresh_deferred_to_existing_lock", { reason });
    return buildInFlightRefreshResult(store, logger, config, uploadedSources.sources.length);
  }

  const startingStatus = buildRefreshStartingStatus(
    config,
    uploadedSources.sources.length,
    attemptTimestamp,
    previousStatus,
    previousCalendarExists,
  );

  await writeStatusSnapshot(store, logger, startingStatus, "refresh_start");

  try {
    const sourceResults = await Promise.all(allSources.map((source) => fetchFeed(source, config, logger, store)));
    const successfulResults = sourceResults.filter((result) => result.status.ok);
    const failedStatuses = sourceResults.filter((result) => !result.status.ok).map((result) => result.status);
    const candidateEvents = successfulResults.length > 0 ? mergeFeedEvents(successfulResults) : [];
    const candidateEventCount = candidateEvents.length;
    const noSourcesConfigured = allSources.length === 0;
    const hasRefreshErrors = failedStatuses.length > 0 || Boolean(uploadedSources.error);
    const canPublishPartial = hasRefreshErrors && previousCalendarExists === false;
    const shouldPublishCalendar =
      successfulResults.length > 0 && (!hasRefreshErrors || canPublishPartial);
    let calendarPublished = false;
    let usedLastKnownGood = false;
    let mergedEventCount = previousStatus?.mergedEventCount ?? 0;
    let lastSuccessfulRefresh = previousStatus?.lastSuccessfulRefresh;
    const errorSummary = [
      ...(uploadedSources.error ? [uploadedSources.error] : []),
      ...failedStatuses.map((status) => `${status.id}: ${status.error ?? "Unknown feed failure."}`),
    ];
    let fatalPublishError: string | undefined;

    if (noSourcesConfigured) {
      errorSummary.unshift(
        "No source feeds configured. Set SOURCE_FEEDS_JSON or upload calendars through the calendars API.",
      );
    }

    logger.info("refresh_started", {
      reason,
      feedCount: allSources.length,
      configuredSourceFeedCount: config.sourceFeeds.length,
      uploadedSourceCount: uploadedSources.sources.length,
    });

    if (shouldPublishCalendar) {
      try {
        const generatedAt = new Date();
        const calendarText = serializeCalendar(candidateEvents, config.serviceName, generatedAt);
        const publicArtifacts = buildPublicCalendarArtifacts(candidateEvents, config.serviceName, generatedAt);
        await store.writeCalendar(calendarText);
        await store.writePublicCalendar(config.publicCalendarBlobPath, publicArtifacts.fullCalendarText);
        await store.writePublicCalendar(config.publicGamesCalendarBlobPath, publicArtifacts.gamesCalendarText);
        await store.writePublicJsonBlob(config.scheduleXFullBlobPath, publicArtifacts.fullScheduleX);
        await store.writePublicJsonBlob(config.scheduleXGamesBlobPath, publicArtifacts.gamesScheduleX);
        calendarPublished = true;
        mergedEventCount = candidateEventCount;
        lastSuccessfulRefresh = attemptTimestamp;
      } catch (error) {
        fatalPublishError = `Failed to write public calendar artifacts: ${errorMessage(error)}`;
        logger.error("calendar_write_failed", { error: fatalPublishError });
        errorSummary.push(fatalPublishError);
      }
    }

    if (!calendarPublished && previousCalendarExists !== false && (hasRefreshErrors || noSourcesConfigured || fatalPublishError)) {
      usedLastKnownGood = true;
    }

    const state =
      noSourcesConfigured || successfulResults.length === 0 || fatalPublishError
        ? "failed"
        : hasRefreshErrors
          ? "partial"
          : "success";
    const status: ServiceStatus = {
      serviceName: config.serviceName,
      state,
      healthy: state !== "failed",
      lastAttemptedRefresh: attemptTimestamp,
      lastSuccessfulRefresh,
      sourceFeedCount: allSources.length,
      configuredSourceFeedCount: config.sourceFeeds.length,
      uploadedSourceCount: uploadedSources.sources.length,
      mergedEventCount,
      candidateMergedEventCount:
        state === "partial" || (state === "failed" && candidateEventCount > 0) ? candidateEventCount : undefined,
      calendarPublished,
      servedLastKnownGood: usedLastKnownGood,
      sourceStatuses: sourceResults.map((result) => result.status),
      output: buildOutputPaths(config),
      errorSummary,
    };

    await writeStatusSnapshot(store, logger, status, "refresh_finish", true);

    logger.info("refresh_finished", {
      reason,
      state,
      mergedEventCount: status.mergedEventCount,
      candidateEventCount,
      calendarPublished,
      usedLastKnownGood,
      failures: failedStatuses.length,
    });

    return {
      status,
      candidateEventCount,
      calendarPublished,
      usedLastKnownGood,
      inFlight: false,
    };
  } finally {
    await refreshLease.release();
  }
}

function buildRefreshStartingStatus(
  config: ReturnType<typeof getConfig>,
  uploadedSourceCount: number,
  attemptTimestamp: string,
  previousStatus: ServiceStatus | null,
  previousCalendarExists: boolean | undefined,
): ServiceStatus {
  return {
    ...buildStartingStatus(config, uploadedSourceCount),
    lastAttemptedRefresh: attemptTimestamp,
    lastSuccessfulRefresh: previousStatus?.lastSuccessfulRefresh,
    mergedEventCount: previousStatus?.mergedEventCount ?? 0,
    calendarPublished: previousCalendarExists !== false,
    sourceFeedCount: config.sourceFeeds.length + uploadedSourceCount,
  };
}

async function buildInFlightRefreshResult(
  store: BlobStore,
  logger: Logger,
  config: ReturnType<typeof getConfig>,
  uploadedSourceCount: number,
): Promise<RefreshResult> {
  const status =
    (await safeReadStatus(store, logger, config.serviceName, config, uploadedSourceCount)) ??
    buildStartingStatus(config, uploadedSourceCount);

  return {
    status,
    candidateEventCount: status.candidateMergedEventCount ?? status.mergedEventCount,
    calendarPublished: status.calendarPublished,
    usedLastKnownGood: status.servedLastKnownGood,
    inFlight: true,
  };
}

async function writeStatusSnapshot(
  store: BlobStore,
  logger: Logger,
  status: ServiceStatus,
  phase: string,
  mutateStatusOnFailure = false,
): Promise<void> {
  const writeErrors: string[] = [];

  try {
    await store.writeOperatorStatus(status);
  } catch (error) {
    const message = `Failed to write operator status: ${errorMessage(error)}`;
    logger.error(`${phase}_operator_status_write_failed`, { error: message });
    writeErrors.push(message);
  }

  try {
    await store.writePublicStatus(toPublicStatus(status));
  } catch (error) {
    const message = `Failed to write public status: ${errorMessage(error)}`;
    logger.error(`${phase}_public_status_write_failed`, { error: message });
    writeErrors.push(message);
  }

  if (mutateStatusOnFailure && writeErrors.length > 0) {
    status.healthy = false;
    status.errorSummary = [...status.errorSummary, ...writeErrors];
  }
}

async function safeListUploadedSources(store: BlobStore, logger: Logger): Promise<UploadedSourcesLoadResult> {
  try {
    return {
      sources: await store.listUploadedSources(),
    };
  } catch (error) {
    const message = `Failed to load uploaded calendars: ${errorMessage(error)}`;
    logger.error("uploaded_sources_load_failed", { error: message });

    return {
      sources: [],
      error: message,
    };
  }
}

async function safeReadStatus(
  store: BlobStore,
  logger: Logger,
  serviceName: string,
  config: ReturnType<typeof getConfig>,
  uploadedSourceCount: number,
): Promise<ServiceStatus | null> {
  try {
    const status = await store.readOperatorStatus();
    return status ? normalizeStatus(config, status, uploadedSourceCount) : null;
  } catch (error) {
    logger.warn("status_read_ignored", {
      serviceName,
      error: errorMessage(error),
    });

    return null;
  }
}

async function safeCalendarExists(store: BlobStore, logger: Logger): Promise<boolean | undefined> {
  try {
    return await store.calendarExists();
  } catch (error) {
    logger.warn("calendar_exists_check_failed", {
      error: errorMessage(error),
    });

    return undefined;
  }
}
