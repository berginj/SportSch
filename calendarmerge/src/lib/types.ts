export type RefreshState = "starting" | "success" | "partial" | "failed";
export type SourceFeedKind = "remote" | "uploaded";
export type UploadAction = "create" | "replace" | "upsert";

export interface SourceFeedConfig {
  id: string;
  name: string;
  kind: SourceFeedKind;
  url: string;
  blobPath?: string;
  uploadedAt?: string;
}

export interface AppConfig {
  serviceName: string;
  sourceFeeds: SourceFeedConfig[];
  outputStorageAccount: string;
  outputContainer: string;
  outputBlobPath: string;
  publicCalendarBlobPath: string;
  publicGamesCalendarBlobPath: string;
  scheduleXFullBlobPath: string;
  scheduleXGamesBlobPath: string;
  statusBlobPath: string;
  operatorStatusContainer: string;
  operatorStatusBlobPath: string;
  uploadedSourcesContainer: string;
  uploadedSourcesPrefix: string;
  refreshLockContainer: string;
  refreshLockBlobPath: string;
  refreshSchedule: string;
  fetchTimeoutMs: number;
  fetchRetryCount: number;
  fetchRetryDelayMs: number;
  maxUploadBytes: number;
  maxFetchBytes: number;
}

export interface IcsProperty {
  name: string;
  params: Record<string, string>;
  value: string;
}

export interface ParsedDateValue {
  kind: "date" | "date-time";
  raw: string;
  params: Record<string, string>;
  sortValue: number;
  iso: string;
}

export interface ParsedEvent {
  sourceId: string;
  sourceName: string;
  identityKey: string;
  mergedUid: string;
  rawUid?: string;
  summary: string;
  location: string;
  status?: string;
  cancelled: boolean;
  sequence: number;
  updatedSortValue?: number;
  start: ParsedDateValue;
  end?: ParsedDateValue;
  properties: IcsProperty[];
}

export interface FeedStatus {
  id: string;
  name: string;
  kind: SourceFeedKind;
  url: string;
  ok: boolean;
  attemptedAt: string;
  durationMs: number;
  eventCount: number;
  httpStatus?: number;
  error?: string;
}

export interface FeedRunResult {
  source: SourceFeedConfig;
  status: FeedStatus;
  events: ParsedEvent[];
}

export interface OutputPaths {
  storageAccount: string;
  container: string;
  calendarBlobPath: string;
  publicCalendarBlobPath: string;
  publicGamesCalendarBlobPath: string;
  scheduleXFullBlobPath: string;
  scheduleXGamesBlobPath: string;
  statusBlobPath: string;
  blobBaseUrl: string;
  blobCalendarUrl: string;
  blobPublicCalendarUrl: string;
  blobPublicGamesCalendarUrl: string;
  blobScheduleXFullUrl: string;
  blobScheduleXGamesUrl: string;
  blobStatusUrl: string;
}

export interface ServiceStatus {
  serviceName: string;
  state: RefreshState;
  healthy: boolean;
  lastAttemptedRefresh?: string;
  lastSuccessfulRefresh?: string;
  sourceFeedCount: number;
  configuredSourceFeedCount: number;
  uploadedSourceCount: number;
  mergedEventCount: number;
  candidateMergedEventCount?: number;
  calendarPublished: boolean;
  servedLastKnownGood: boolean;
  sourceStatuses: FeedStatus[];
  output: OutputPaths;
  errorSummary: string[];
}

export interface PublicServiceStatus {
  serviceName: string;
  state: RefreshState;
  healthy: boolean;
  lastAttemptedRefresh?: string;
  lastSuccessfulRefresh?: string;
  sourceFeedCount: number;
  configuredSourceFeedCount: number;
  uploadedSourceCount: number;
  mergedEventCount: number;
  candidateMergedEventCount?: number;
  calendarPublished: boolean;
  servedLastKnownGood: boolean;
  output: OutputPaths;
}

export interface RefreshResult {
  status: ServiceStatus;
  candidateEventCount: number;
  calendarPublished: boolean;
  usedLastKnownGood: boolean;
  inFlight: boolean;
}

export interface UploadedCalendarRecord {
  id: string;
  name: string;
  kind: "uploaded";
  blobPath: string;
  url: string;
  uploadedAt?: string;
}

export interface UploadCalendarInput {
  id: string;
  name?: string;
  calendarText: string;
}
