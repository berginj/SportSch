import { describe, expect, it } from "vitest";

import {
  normalizeCalendarId,
  normalizeUploadCalendarInput,
  parseBooleanInput,
  parseUploadAction,
} from "../src/lib/uploads";

describe("calendar uploads", () => {
  it("derives a stable id from the provided name when id is omitted", () => {
    const normalized = normalizeUploadCalendarInput({
      name: "School Board Events",
      calendarText: "BEGIN:VCALENDAR\nEND:VCALENDAR",
    });

    expect(normalized.id).toBe("school-board-events");
    expect(normalized.name).toBe("School Board Events");
  });

  it("rejects empty ICS content", () => {
    expect(() =>
      normalizeUploadCalendarInput({
        id: "district",
        calendarText: "   ",
      }),
    ).toThrow(/ICS content/);
  });

  it("parses supported boolean inputs for refresh behavior", () => {
    expect(parseBooleanInput("true", false)).toBe(true);
    expect(parseBooleanInput("0", true)).toBe(false);
    expect(parseBooleanInput(undefined, true)).toBe(true);
  });

  it("parses upload actions and defaults to upsert", () => {
    expect(parseUploadAction("create")).toBe("create");
    expect(parseUploadAction("REPLACE")).toBe("replace");
    expect(parseUploadAction(undefined)).toBe("upsert");
  });

  it("normalizes calendar ids for lifecycle operations", () => {
    expect(normalizeCalendarId(" School Board Events ")).toBe("school-board-events");
  });
});
