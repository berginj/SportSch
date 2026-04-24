import { DateTime } from "luxon";

import { IcsProperty, ParsedDateValue, ParsedEvent, SourceFeedConfig } from "./types";
import { sha256Hex } from "./util";

export function parseIcsCalendar(input: string, source: SourceFeedConfig): ParsedEvent[] {
  const lines = unfoldLines(input);
  const events: ParsedEvent[] = [];
  let currentEventProperties: IcsProperty[] | null = null;
  let nestedComponentDepth = 0;

  for (const line of lines) {
    if (!line) {
      continue;
    }

    if (line === "BEGIN:VEVENT") {
      if (currentEventProperties) {
        throw new Error("Encountered nested VEVENT.");
      }

      currentEventProperties = [];
      nestedComponentDepth = 0;
      continue;
    }

    if (line === "END:VEVENT") {
      if (!currentEventProperties) {
        throw new Error("Encountered END:VEVENT without a matching BEGIN:VEVENT.");
      }

      if (nestedComponentDepth !== 0) {
        throw new Error("Encountered END:VEVENT before nested components closed.");
      }

      events.push(buildParsedEvent(currentEventProperties, source));
      currentEventProperties = null;
      continue;
    }

    if (!currentEventProperties) {
      continue;
    }

    if (line.startsWith("BEGIN:")) {
      nestedComponentDepth += 1;
      continue;
    }

    if (line.startsWith("END:")) {
      if (nestedComponentDepth === 0) {
        throw new Error(`Unexpected component terminator inside VEVENT: ${line}`);
      }

      nestedComponentDepth -= 1;
      continue;
    }

    if (nestedComponentDepth > 0) {
      continue;
    }

    currentEventProperties.push(parsePropertyLine(line));
  }

  if (currentEventProperties) {
    throw new Error("Unterminated VEVENT block.");
  }

  return events;
}

export function serializeCalendar(events: ParsedEvent[], serviceName: string, generatedAt = new Date()): string {
  const dtstamp = toUtcTimestamp(generatedAt);
  const lines = [
    "BEGIN:VCALENDAR",
    "VERSION:2.0",
    `PRODID:-//${serviceName}//EN`,
    "CALSCALE:GREGORIAN",
    "METHOD:PUBLISH",
  ];

  for (const event of events) {
    lines.push(...serializeEvent(event, dtstamp));
  }

  lines.push("END:VCALENDAR");

  return `${lines.map(foldLine).join("\r\n")}\r\n`;
}

function serializeEvent(event: ParsedEvent, fallbackDtstamp: string): string[] {
  const lines = ["BEGIN:VEVENT"];
  let uidWritten = false;
  let dtstampWritten = false;

  for (const property of event.properties) {
    if (property.name === "UID") {
      if (!uidWritten) {
        lines.push(formatProperty({ ...property, value: event.mergedUid }));
        uidWritten = true;
      }

      continue;
    }

    if (property.name === "DTSTAMP") {
      if (!dtstampWritten) {
        lines.push(formatProperty(property));
        dtstampWritten = true;
      }

      continue;
    }

    lines.push(formatProperty(property));
  }

  if (!uidWritten) {
    lines.splice(1, 0, `UID:${event.mergedUid}`);
  }

  if (!dtstampWritten) {
    lines.push(`DTSTAMP:${fallbackDtstamp}`);
  }

  lines.push("END:VEVENT");

  return lines;
}

function buildParsedEvent(properties: IcsProperty[], source: SourceFeedConfig): ParsedEvent {
  const startProperty = getFirstProperty(properties, "DTSTART");
  if (!startProperty) {
    throw new Error(`VEVENT from ${source.id} is missing DTSTART.`);
  }

  const endProperty = getFirstProperty(properties, "DTEND");
  const uidProperty = getFirstProperty(properties, "UID");
  const statusProperty = getFirstProperty(properties, "STATUS");
  const summaryProperty = getFirstProperty(properties, "SUMMARY");
  const locationProperty = getFirstProperty(properties, "LOCATION");
  const sequenceProperty = getFirstProperty(properties, "SEQUENCE");
  const lastModifiedProperty = getFirstProperty(properties, "LAST-MODIFIED");
  const dtstampProperty = getFirstProperty(properties, "DTSTAMP");

  const rawUid = uidProperty?.value.trim() || undefined;
  const summary = summaryProperty?.value ?? "";
  const location = locationProperty?.value ?? "";
  const start = parseDateValue(startProperty, "DTSTART");
  const end = endProperty ? parseDateValue(endProperty, "DTEND") : undefined;
  const sequence = parseSequence(sequenceProperty?.value);
  const status = statusProperty?.value?.trim().toUpperCase();
  const identitySeed = rawUid
    ? `${source.id}\nuid\n${rawUid}`
    : `${source.id}\nfallback\n${summary}\n${start.raw}\n${end?.raw ?? ""}\n${location}`;

  return {
    sourceId: source.id,
    sourceName: source.name,
    identityKey: sha256Hex(identitySeed),
    mergedUid: buildMergedUid(source.id, identitySeed),
    rawUid,
    summary,
    location,
    status,
    cancelled: status === "CANCELLED",
    sequence,
    updatedSortValue: parseComparableTimestamp(lastModifiedProperty) ?? parseComparableTimestamp(dtstampProperty),
    start,
    end,
    properties,
  };
}

function buildMergedUid(sourceId: string, seed: string): string {
  const hash = sha256Hex(seed).slice(0, 24);
  const normalizedSourceId = sourceId.toLowerCase().replace(/[^a-z0-9-]/g, "-");

  return `${normalizedSourceId}-${hash}@calendarmerge`;
}

function parseComparableTimestamp(property: IcsProperty | undefined): number | undefined {
  if (!property) {
    return undefined;
  }

  return parseDateValue(property, property.name).sortValue;
}

function parseDateValue(property: IcsProperty, propertyName: string): ParsedDateValue {
  const value = property.value.trim();
  const valueType = property.params.VALUE?.toUpperCase();

  if (valueType === "DATE" || /^\d{8}$/.test(value)) {
    const parsed = DateTime.fromFormat(value, "yyyyLLdd", { zone: "utc" });
    if (!parsed.isValid) {
      throw new Error(`Invalid ${propertyName} value: ${value}`);
    }

    return {
      kind: "date",
      raw: value,
      params: property.params,
      sortValue: parsed.toMillis(),
      iso: parsed.toISODate() ?? value,
    };
  }

  const parsed = parseDateTime(value, property.params.TZID);
  if (!parsed.isValid) {
    throw new Error(`Invalid ${propertyName} value: ${value}`);
  }

  return {
    kind: "date-time",
    raw: value,
    params: property.params,
    sortValue: parsed.toUTC().toMillis(),
    iso: parsed.toUTC().toISO() ?? value,
  };
}

function parseDateTime(value: string, tzid: string | undefined): DateTime {
  const isUtc = value.endsWith("Z");
  const rawValue = isUtc ? value.slice(0, -1) : value;
  const match = rawValue.match(/^(\d{8}T\d{4})(\d{2})?$/);

  if (!match) {
    return DateTime.invalid("Unsupported datetime format");
  }

  const normalized = match[2] ? rawValue : `${rawValue}00`;
  const zone = isUtc ? "utc" : tzid ?? "utc";

  return DateTime.fromFormat(normalized, "yyyyLLdd'T'HHmmss", {
    zone,
    setZone: Boolean(isUtc || tzid),
  });
}

function parseSequence(rawValue: string | undefined): number {
  if (!rawValue) {
    return 0;
  }

  const value = Number.parseInt(rawValue, 10);
  return Number.isInteger(value) ? value : 0;
}

function getFirstProperty(properties: IcsProperty[], name: string): IcsProperty | undefined {
  return properties.find((property) => property.name === name);
}

function parsePropertyLine(line: string): IcsProperty {
  const separatorIndex = line.indexOf(":");
  if (separatorIndex <= 0) {
    throw new Error(`Invalid ICS property line: ${line}`);
  }

  const descriptor = line.slice(0, separatorIndex);
  const value = line.slice(separatorIndex + 1);
  const parts = descriptor.split(";");
  const name = parts[0]?.trim().toUpperCase();

  if (!name) {
    throw new Error(`Invalid ICS property name: ${line}`);
  }

  const params: Record<string, string> = {};
  for (const part of parts.slice(1)) {
    const equalsIndex = part.indexOf("=");
    if (equalsIndex <= 0) {
      throw new Error(`Invalid ICS property parameter: ${line}`);
    }

    const paramName = part.slice(0, equalsIndex).trim().toUpperCase();
    const paramValue = stripQuotes(part.slice(equalsIndex + 1).trim());
    params[paramName] = paramValue;
  }

  return {
    name,
    params,
    value,
  };
}

function unfoldLines(input: string): string[] {
  const normalized = input.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  const rawLines = normalized.split("\n");
  const lines: string[] = [];

  for (const rawLine of rawLines) {
    if ((rawLine.startsWith(" ") || rawLine.startsWith("\t")) && lines.length > 0) {
      lines[lines.length - 1] += rawLine.slice(1);
      continue;
    }

    lines.push(rawLine.trimEnd());
  }

  return lines;
}

function formatProperty(property: IcsProperty): string {
  const parameterText = Object.entries(property.params)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([name, value]) => `;${name}=${formatParameterValue(value)}`)
    .join("");

  return `${property.name}${parameterText}:${property.value}`;
}

function formatParameterValue(value: string): string {
  if (/[,:;]/.test(value)) {
    return `"${value.replace(/"/g, '\\"')}"`;
  }

  return value;
}

function stripQuotes(value: string): string {
  return value.startsWith("\"") && value.endsWith("\"") ? value.slice(1, -1) : value;
}

function toUtcTimestamp(value: Date): string {
  return DateTime.fromJSDate(value, { zone: "utc" }).toFormat("yyyyLLdd'T'HHmmss'Z'");
}

function foldLine(line: string): string {
  let current = "";
  let result = "";

  for (const character of line) {
    const candidate = `${current}${character}`;
    if (Buffer.byteLength(candidate, "utf8") > 75) {
      result += `${current}\r\n `;
      current = character;
    } else {
      current = candidate;
    }
  }

  return `${result}${current}`;
}
