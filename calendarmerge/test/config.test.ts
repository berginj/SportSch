import { describe, expect, it } from "vitest";

import { loadConfig } from "../src/lib/config";

describe("config", () => {
  it("supports upload-only mode when SOURCE_FEEDS_JSON is omitted", () => {
    const config = loadConfig({
      OUTPUT_STORAGE_ACCOUNT: "calendarmergeprod01",
    } as NodeJS.ProcessEnv);

    expect(config.sourceFeeds).toEqual([]);
    expect(config.operatorStatusContainer).toBe("sources");
    expect(config.operatorStatusBlobPath).toBe("_system/status-detail.json");
    expect(config.publicCalendarBlobPath).toBe("calendar-public.ics");
    expect(config.publicGamesCalendarBlobPath).toBe("calendar-games.ics");
    expect(config.scheduleXFullBlobPath).toBe("schedule-x-full.json");
    expect(config.scheduleXGamesBlobPath).toBe("schedule-x-games.json");
    expect(config.uploadedSourcesContainer).toBe("sources");
    expect(config.uploadedSourcesPrefix).toBe("uploads");
    expect(config.refreshLockContainer).toBe("sources");
    expect(config.refreshLockBlobPath).toBe("_system/refresh.lock");
    expect(config.maxUploadBytes).toBe(5 * 1024 * 1024);
    expect(config.maxFetchBytes).toBe(5 * 1024 * 1024);
  });
});
