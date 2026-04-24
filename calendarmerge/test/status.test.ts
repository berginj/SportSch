import { describe, expect, it } from "vitest";

import { toPublicStatus } from "../src/lib/status";
import { ServiceStatus } from "../src/lib/types";

describe("public status", () => {
  it("strips operator-only diagnostics from the public status model", () => {
    const detailedStatus: ServiceStatus = {
      serviceName: "calendarmerge",
      state: "partial",
      healthy: true,
      lastAttemptedRefresh: "2026-04-12T00:00:00.000Z",
      lastSuccessfulRefresh: "2026-04-11T23:00:00.000Z",
      sourceFeedCount: 3,
      configuredSourceFeedCount: 2,
      uploadedSourceCount: 1,
      mergedEventCount: 42,
      candidateMergedEventCount: 44,
      calendarPublished: false,
      servedLastKnownGood: true,
      sourceStatuses: [
        {
          id: "district",
          name: "District",
          kind: "remote",
          url: "https://example.com/calendar.ics",
          ok: false,
          attemptedAt: "2026-04-12T00:00:00.000Z",
          durationMs: 250,
          eventCount: 0,
          error: "HTTP 500",
        },
      ],
      output: {
        storageAccount: "calendarmergeprod01",
        container: "$web",
        calendarBlobPath: "calendar.ics",
        publicCalendarBlobPath: "calendar-public.ics",
        publicGamesCalendarBlobPath: "calendar-games.ics",
        scheduleXFullBlobPath: "schedule-x-full.json",
        scheduleXGamesBlobPath: "schedule-x-games.json",
        statusBlobPath: "status.json",
        blobBaseUrl: "https://calendarmergeprod01.blob.core.windows.net",
        blobCalendarUrl: "https://calendarmergeprod01.blob.core.windows.net/$web/calendar.ics",
        blobPublicCalendarUrl: "https://calendarmergeprod01.blob.core.windows.net/$web/calendar-public.ics",
        blobPublicGamesCalendarUrl: "https://calendarmergeprod01.blob.core.windows.net/$web/calendar-games.ics",
        blobScheduleXFullUrl: "https://calendarmergeprod01.blob.core.windows.net/$web/schedule-x-full.json",
        blobScheduleXGamesUrl: "https://calendarmergeprod01.blob.core.windows.net/$web/schedule-x-games.json",
        blobStatusUrl: "https://calendarmergeprod01.blob.core.windows.net/$web/status.json",
      },
      errorSummary: ["district: HTTP 500"],
    };

    expect(toPublicStatus(detailedStatus)).toEqual({
      serviceName: "calendarmerge",
      state: "partial",
      healthy: true,
      lastAttemptedRefresh: "2026-04-12T00:00:00.000Z",
      lastSuccessfulRefresh: "2026-04-11T23:00:00.000Z",
      sourceFeedCount: 3,
      configuredSourceFeedCount: 2,
      uploadedSourceCount: 1,
      mergedEventCount: 42,
      candidateMergedEventCount: 44,
      calendarPublished: false,
      servedLastKnownGood: true,
      output: detailedStatus.output,
    });
  });
});
