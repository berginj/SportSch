import { AppConfig, PublicServiceStatus, ServiceStatus } from "./types";
import { buildOutputPaths } from "./util";

export function buildStartingStatus(config: AppConfig, uploadedSourceCount = 0): ServiceStatus {
  return {
    serviceName: config.serviceName,
    state: "starting",
    healthy: false,
    sourceFeedCount: config.sourceFeeds.length + uploadedSourceCount,
    configuredSourceFeedCount: config.sourceFeeds.length,
    uploadedSourceCount,
    mergedEventCount: 0,
    calendarPublished: false,
    servedLastKnownGood: false,
    sourceStatuses: [],
    output: buildOutputPaths(config),
    errorSummary: [],
  };
}

export function normalizeStatus(
  config: AppConfig,
  status: ServiceStatus,
  uploadedSourceCount = status.uploadedSourceCount ?? 0,
): ServiceStatus {
  const configuredSourceFeedCount = status.configuredSourceFeedCount ?? config.sourceFeeds.length;
  const totalSourceFeedCount = status.sourceFeedCount ?? configuredSourceFeedCount + uploadedSourceCount;

  return {
    ...buildStartingStatus(config, uploadedSourceCount),
    ...status,
    sourceFeedCount: totalSourceFeedCount,
    configuredSourceFeedCount,
    uploadedSourceCount,
    sourceStatuses: status.sourceStatuses ?? [],
    output: status.output ?? buildOutputPaths(config),
    errorSummary: status.errorSummary ?? [],
  };
}

export function buildStartingPublicStatus(config: AppConfig, uploadedSourceCount = 0): PublicServiceStatus {
  return toPublicStatus(buildStartingStatus(config, uploadedSourceCount));
}

export function normalizePublicStatus(
  config: AppConfig,
  status: PublicServiceStatus,
  uploadedSourceCount = status.uploadedSourceCount ?? 0,
): PublicServiceStatus {
  const fallback = buildStartingPublicStatus(config, uploadedSourceCount);
  const configuredSourceFeedCount = status.configuredSourceFeedCount ?? config.sourceFeeds.length;
  const totalSourceFeedCount = status.sourceFeedCount ?? configuredSourceFeedCount + uploadedSourceCount;

  return {
    ...fallback,
    ...status,
    sourceFeedCount: totalSourceFeedCount,
    configuredSourceFeedCount,
    uploadedSourceCount,
  };
}

export function toPublicStatus(status: ServiceStatus): PublicServiceStatus {
  return {
    serviceName: status.serviceName,
    state: status.state,
    healthy: status.healthy,
    lastAttemptedRefresh: status.lastAttemptedRefresh,
    lastSuccessfulRefresh: status.lastSuccessfulRefresh,
    sourceFeedCount: status.sourceFeedCount,
    configuredSourceFeedCount: status.configuredSourceFeedCount,
    uploadedSourceCount: status.uploadedSourceCount,
    mergedEventCount: status.mergedEventCount,
    candidateMergedEventCount: status.candidateMergedEventCount,
    calendarPublished: status.calendarPublished,
    servedLastKnownGood: status.servedLastKnownGood,
    output: status.output,
  };
}
