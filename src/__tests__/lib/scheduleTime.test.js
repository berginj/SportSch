import { describe, expect, it } from "vitest";
import { bookingLeadTime, scheduleInstant } from "../../lib/scheduleTime";

describe("Eastern schedule time", () => {
  it("uses daylight saving offsets independent of the browser zone", () => {
    expect(scheduleInstant("2026-07-10", "18:00")).toBe(Date.parse("2026-07-10T22:00:00Z"));
    expect(scheduleInstant("2026-01-10", "18:00")).toBe(Date.parse("2026-01-10T23:00:00Z"));
  });
  it("rejects ambiguous, nonexistent, and invalid wall times", () => {
    for (const [date, time] of [["2026-03-08", "02:30"], ["2026-11-01", "01:30"], ["2026-02-30", "18:00"], ["", ""]]) {
      expect(scheduleInstant(date, time)).toBeNull();
      expect(bookingLeadTime(date, time).withinLeadTime).toBe(true);
    }
  });
  it("allows exactly 72 hours and blocks late or historical moves", () => {
    const start = scheduleInstant("2026-07-10", "18:00");
    expect(bookingLeadTime("2026-07-10", "18:00", start - 72 * 3600000).withinLeadTime).toBe(false);
    expect(bookingLeadTime("2026-07-10", "18:00", start - 72 * 3600000 + 1).withinLeadTime).toBe(true);
    expect(bookingLeadTime("2026-07-10", "18:00", start + 1).withinLeadTime).toBe(true);
  });
});
