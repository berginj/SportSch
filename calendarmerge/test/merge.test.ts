import { describe, expect, it } from "vitest";

import { parseIcsCalendar, serializeCalendar } from "../src/lib/ics";
import { mergeFeedEvents } from "../src/lib/merge";
import { FeedRunResult, SourceFeedConfig } from "../src/lib/types";

function source(id: string, name = id): SourceFeedConfig {
  return {
    id,
    name,
    kind: "remote",
    url: `https://example.com/${id}.ics`,
  };
}

function successfulResult(sourceFeed: SourceFeedConfig, ics: string): FeedRunResult {
  const events = parseIcsCalendar(ics, sourceFeed);

  return {
    source: sourceFeed,
    status: {
      id: sourceFeed.id,
      name: sourceFeed.name,
      kind: sourceFeed.kind,
      url: sourceFeed.url,
      ok: true,
      attemptedAt: "2026-03-16T00:00:00.000Z",
      durationMs: 1,
      eventCount: events.length,
    },
    events,
  };
}

describe("calendar merge", () => {
  it("keeps duplicate raw UIDs from different feeds by namespacing the merged UID", () => {
    const left = source("district-a");
    const right = source("district-b");

    const leftIcs = `BEGIN:VCALENDAR
BEGIN:VEVENT
UID:shared-uid
DTSTART:20260501T130000Z
DTEND:20260501T140000Z
SUMMARY:Varsity Practice
END:VEVENT
END:VCALENDAR`;

    const rightIcs = `BEGIN:VCALENDAR
BEGIN:VEVENT
UID:shared-uid
DTSTART:20260501T150000Z
DTEND:20260501T160000Z
SUMMARY:Board Meeting
END:VEVENT
END:VCALENDAR`;

    const merged = mergeFeedEvents([successfulResult(left, leftIcs), successfulResult(right, rightIcs)]);
    const serialized = serializeCalendar(merged, "calendarmerge", new Date("2026-03-16T00:00:00.000Z"));

    expect(merged).toHaveLength(2);
    expect(new Set(merged.map((event) => event.mergedUid)).size).toBe(2);
    expect(serialized).toContain("SUMMARY:Varsity Practice");
    expect(serialized).toContain("SUMMARY:Board Meeting");
  });

  it("deduplicates missing-UID events deterministically within the same feed", () => {
    const teamFeed = source("team");
    const ics = `BEGIN:VCALENDAR
BEGIN:VEVENT
DTSTART:20260502T090000Z
DTEND:20260502T100000Z
SUMMARY:Field Setup
LOCATION:Diamond 1
END:VEVENT
BEGIN:VEVENT
DTSTART:20260502T090000Z
DTEND:20260502T100000Z
SUMMARY:Field Setup
LOCATION:Diamond 1
END:VEVENT
END:VCALENDAR`;

    const merged = mergeFeedEvents([successfulResult(teamFeed, ics)]);

    expect(merged).toHaveLength(1);
    expect(merged[0]?.rawUid).toBeUndefined();
  });

  it("preserves all-day events as VALUE=DATE entries", () => {
    const schoolFeed = source("school");
    const ics = `BEGIN:VCALENDAR
BEGIN:VEVENT
UID:day-off
DTSTART;VALUE=DATE:20260510
DTEND;VALUE=DATE:20260511
SUMMARY:No School
END:VEVENT
END:VCALENDAR`;

    const merged = mergeFeedEvents([successfulResult(schoolFeed, ics)]);
    const serialized = serializeCalendar(merged, "calendarmerge", new Date("2026-03-16T00:00:00.000Z"));

    expect(merged[0]?.start.kind).toBe("date");
    expect(serialized).toContain("DTSTART;VALUE=DATE:20260510");
    expect(serialized).toContain("DTEND;VALUE=DATE:20260511");
  });

  it("keeps cancelled updates when a later sequence marks the event cancelled", () => {
    const schoolFeed = source("school");
    const ics = `BEGIN:VCALENDAR
BEGIN:VEVENT
UID:game-123
SEQUENCE:1
DTSTART:20260512T170000Z
DTEND:20260512T180000Z
SUMMARY:Game vs Tigers
END:VEVENT
BEGIN:VEVENT
UID:game-123
SEQUENCE:2
STATUS:CANCELLED
DTSTART:20260512T170000Z
DTEND:20260512T180000Z
SUMMARY:Game vs Tigers
END:VEVENT
END:VCALENDAR`;

    const merged = mergeFeedEvents([successfulResult(schoolFeed, ics)]);

    expect(merged).toHaveLength(1);
    expect(merged[0]?.cancelled).toBe(true);
    expect(serializeCalendar(merged, "calendarmerge")).toContain("STATUS:CANCELLED");
  });

  it("throws on malformed ICS input", () => {
    const brokenFeed = source("broken");
    const brokenIcs = `BEGIN:VCALENDAR
BEGIN:VEVENT
UID:oops
DTSTART:NOT-A-DATE
SUMMARY:Bad Event
END:VEVENT
END:VCALENDAR`;

    expect(() => parseIcsCalendar(brokenIcs, brokenFeed)).toThrow(/Invalid DTSTART value/);
  });
});
