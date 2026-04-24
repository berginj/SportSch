import { ParsedEvent } from "./types";
import { serializeCalendar } from "./ics";

const PUBLIC_STRIP_PROPERTY_NAMES = new Set([
  "ATTENDEE",
  "CONTACT",
  "ORGANIZER",
  "X-MS-OLK-CONFTYPE",
  "X-MS-OLK-SENDER",
  "X-MS-OLK-AUTOFILLLOCATION",
]);

const GAME_CATEGORY_KEYWORDS = [
  "game",
  "games",
  "athletics",
  "athletic",
  "sports",
  "sport",
  "match",
  "meet",
  "scrimmage",
  "tournament",
  "playoff",
  "championship",
  "doubleheader",
];

const GAME_TEXT_PATTERN =
  /\b(game|games|match|meet|scrimmage|tournament|playoff|championship|doubleheader|homecoming)\b|\bvs\.?\b|\bversus\b/i;

export interface ScheduleXEventDocument {
  serviceName: string;
  mode: "full" | "games";
  generatedAt: string;
  timezone: "UTC";
  events: ScheduleXEvent[];
}

interface ScheduleXEvent {
  id: string;
  title: string;
  start: string;
  end: string;
  location?: string;
  description?: string;
  calendarId: string;
}

export interface PublicCalendarArtifacts {
  fullCalendarText: string;
  gamesCalendarText: string;
  fullScheduleX: ScheduleXEventDocument;
  gamesScheduleX: ScheduleXEventDocument;
}

export function buildPublicCalendarArtifacts(
  events: ParsedEvent[],
  serviceName: string,
  generatedAt = new Date(),
): PublicCalendarArtifacts {
  const publicEvents = events.map(toPublicEvent);
  const gamesEvents = publicEvents.filter(isLikelyGameEvent);
  const generatedAtIso = generatedAt.toISOString();

  return {
    fullCalendarText: serializeCalendar(publicEvents, serviceName, generatedAt),
    gamesCalendarText: serializeCalendar(gamesEvents, serviceName, generatedAt),
    fullScheduleX: buildScheduleXDocument(publicEvents, serviceName, "full", generatedAtIso),
    gamesScheduleX: buildScheduleXDocument(gamesEvents, serviceName, "games", generatedAtIso),
  };
}

export function toPublicEvent(event: ParsedEvent): ParsedEvent {
  const properties = event.properties
    .filter((property) => !PUBLIC_STRIP_PROPERTY_NAMES.has(property.name))
    .filter((property) => property.name !== "DESCRIPTION");

  return {
    ...event,
    properties,
  };
}

export function isLikelyGameEvent(event: ParsedEvent): boolean {
  const categories = readMultiValueProperty(event, "CATEGORIES");
  if (categories.some((category) => GAME_CATEGORY_KEYWORDS.includes(category))) {
    return true;
  }

  const haystack = [event.summary, event.location, event.sourceName].filter(Boolean).join("\n");
  return GAME_TEXT_PATTERN.test(haystack);
}

function buildScheduleXDocument(
  events: ParsedEvent[],
  serviceName: string,
  mode: "full" | "games",
  generatedAt: string,
): ScheduleXEventDocument {
  return {
    serviceName,
    mode,
    generatedAt,
    timezone: "UTC",
    events: events.map(toScheduleXEvent),
  };
}

function toScheduleXEvent(event: ParsedEvent): ScheduleXEvent {
  return {
    id: event.mergedUid,
    title: event.summary || event.sourceName,
    start: toScheduleXTime(event.start.iso, event.start.kind),
    end: toScheduleXTime(event.end?.iso ?? event.start.iso, event.end?.kind ?? event.start.kind),
    location: event.location || undefined,
    description: buildScheduleXDescription(event),
    calendarId: event.sourceId,
  };
}

function buildScheduleXDescription(event: ParsedEvent): string | undefined {
  const lines = [event.location, event.cancelled ? "Cancelled" : undefined, `Source: ${event.sourceName}`].filter(Boolean);
  return lines.length > 0 ? lines.join("\n") : undefined;
}

function readMultiValueProperty(event: ParsedEvent, propertyName: string): string[] {
  return event.properties
    .filter((property) => property.name === propertyName)
    .flatMap((property) => property.value.split(","))
    .map((value) => value.trim().toLowerCase())
    .filter(Boolean);
}

function toScheduleXTime(iso: string, kind: "date" | "date-time"): string {
  if (kind === "date") {
    return iso.slice(0, 10);
  }

  return iso.slice(0, 16).replace("T", " ");
}
