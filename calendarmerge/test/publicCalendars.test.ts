import { describe, expect, it } from "vitest";

import { parseIcsCalendar } from "../src/lib/ics";
import { buildPublicCalendarArtifacts, isLikelyGameEvent, toPublicEvent } from "../src/lib/publicCalendars";
import { SourceFeedConfig } from "../src/lib/types";

function source(id: string, name = id): SourceFeedConfig {
  return {
    id,
    name,
    kind: "remote",
    url: `https://example.com/${id}.ics`,
  };
}

describe("public calendar artifacts", () => {
  it("removes attendee and organizer details from public calendar output", () => {
    const event = parseIcsCalendar(
      `BEGIN:VCALENDAR
BEGIN:VEVENT
UID:game-1
DTSTART:20260512T170000Z
DTEND:20260512T180000Z
SUMMARY:Game vs Tigers
LOCATION:Field 4, 123 Main St, Springfield
ATTENDEE;CN=Pat Coach:mailto:pat@example.com
ORGANIZER;CN=Athletics Office:mailto:office@example.com
CONTACT:555-111-2222
DESCRIPTION:Call Pat for gate access
END:VEVENT
END:VCALENDAR`,
      source("athletics", "Athletics"),
    )[0];

    const publicEvent = toPublicEvent(event!);
    const serialized = buildPublicCalendarArtifacts([event!], "calendarmerge").fullCalendarText;

    expect(publicEvent.properties.some((property) => property.name === "ATTENDEE")).toBe(false);
    expect(publicEvent.properties.some((property) => property.name === "ORGANIZER")).toBe(false);
    expect(publicEvent.properties.some((property) => property.name === "CONTACT")).toBe(false);
    expect(publicEvent.properties.some((property) => property.name === "DESCRIPTION")).toBe(false);
    expect(serialized).toContain("LOCATION:Field 4, 123 Main St, Springfield");
    expect(serialized).not.toContain("ATTENDEE");
    expect(serialized).not.toContain("ORGANIZER");
    expect(serialized).not.toContain("CONTACT");
    expect(serialized).not.toContain("Call Pat");
  });

  it("keeps only likely game events in the games view document", () => {
    const athletics = source("athletics", "Athletics");
    const events = parseIcsCalendar(
      `BEGIN:VCALENDAR
BEGIN:VEVENT
UID:game-1
DTSTART:20260512T170000Z
DTEND:20260512T180000Z
SUMMARY:Game vs Tigers
LOCATION:Field 4
END:VEVENT
BEGIN:VEVENT
UID:meeting-1
DTSTART:20260512T190000Z
DTEND:20260512T193000Z
SUMMARY:Booster Club Meeting
LOCATION:Library
END:VEVENT
END:VCALENDAR`,
      athletics,
    );

    const artifacts = buildPublicCalendarArtifacts(events, "calendarmerge");

    expect(events.map((event) => isLikelyGameEvent(event))).toEqual([true, false]);
    expect(artifacts.gamesScheduleX.events).toHaveLength(1);
    expect(artifacts.gamesScheduleX.events[0]?.title).toBe("Game vs Tigers");
    expect(artifacts.fullScheduleX.events).toHaveLength(2);
  });
});
