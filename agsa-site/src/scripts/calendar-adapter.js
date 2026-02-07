import calendarConfig from "../config/calendar.json";

const CACHE_KEY = "agsa_calendar_cache_v1";
const CACHE_TTL_MS = 1000 * 60 * 20;

function parseIcsDate(value) {
  if (!value) return null;
  const clean = value.trim();
  if (/^\d{8}$/.test(clean)) {
    return new Date(`${clean.slice(0, 4)}-${clean.slice(4, 6)}-${clean.slice(6, 8)}T00:00:00`);
  }
  if (/^\d{8}T\d{6}Z$/.test(clean)) {
    return new Date(
      `${clean.slice(0, 4)}-${clean.slice(4, 6)}-${clean.slice(6, 8)}T${clean.slice(9, 11)}:${clean.slice(11, 13)}:${clean.slice(13, 15)}Z`
    );
  }
  return new Date(clean);
}

function parseIcs(content) {
  const lines = content.replace(/\r\n/g, "\n").split("\n");
  const events = [];
  let current = null;

  for (const line of lines) {
    if (line.startsWith("BEGIN:VEVENT")) current = {};
    else if (line.startsWith("END:VEVENT") && current) {
      if (current.summary && current.start) events.push(current);
      current = null;
    } else if (current) {
      const idx = line.indexOf(":");
      if (idx === -1) continue;
      const rawKey = line.slice(0, idx);
      const value = line.slice(idx + 1).trim();
      const key = rawKey.split(";")[0];
      if (key === "SUMMARY") current.summary = value;
      if (key === "DESCRIPTION") current.description = value;
      if (key === "LOCATION") current.location = value;
      if (key === "DTSTART") current.start = parseIcsDate(value);
      if (key === "DTEND") current.end = parseIcsDate(value);
    }
  }

  return events
    .filter((event) => event.start instanceof Date && !Number.isNaN(event.start.valueOf()))
    .sort((a, b) => a.start.valueOf() - b.start.valueOf());
}

function readCache() {
  try {
    const raw = localStorage.getItem(CACHE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed.timestamp || Date.now() - parsed.timestamp > CACHE_TTL_MS) return null;
    return parsed.events || null;
  } catch {
    return null;
  }
}

function writeCache(events) {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify({ timestamp: Date.now(), events }));
  } catch {
    // ignore cache errors
  }
}

async function fetchIcsEvents() {
  const cached = readCache();
  if (cached) return cached;

  const response = await fetch(calendarConfig.icsUrl);
  if (!response.ok) {
    throw new Error("Unable to load calendar ICS feed. Check feed URL and CORS policy.");
  }
  const content = await response.text();
  const parsed = parseIcs(content).slice(0, calendarConfig.maxEvents || 100);
  const serialized = parsed.map((event) => ({
    ...event,
    start: event.start.toISOString(),
    end: event.end ? event.end.toISOString() : null,
  }));
  writeCache(serialized);
  return serialized;
}

async function fetchGoogleEvents() {
  const keyName = calendarConfig.apiKeyEnvVarName || "PUBLIC_GCAL_API_KEY";
  const apiKey = import.meta.env[keyName];
  if (!apiKey) throw new Error(`Missing ${keyName}. Add it to your environment.`);
  if (!calendarConfig.calendarId) throw new Error("Missing calendarId in calendar.json.");

  const timeMin = new Date().toISOString();
  const endpoint =
    `https://www.googleapis.com/calendar/v3/calendars/${encodeURIComponent(calendarConfig.calendarId)}` +
    `/events?key=${encodeURIComponent(apiKey)}&timeMin=${encodeURIComponent(timeMin)}&singleEvents=true&orderBy=startTime&maxResults=${calendarConfig.maxEvents || 100}`;

  const response = await fetch(endpoint);
  if (!response.ok) throw new Error("Unable to load Google Calendar events.");
  const json = await response.json();
  return (json.items || []).map((item) => ({
    summary: item.summary || "Untitled event",
    description: item.description || "",
    location: item.location || "",
    start: item.start?.dateTime || item.start?.date,
    end: item.end?.dateTime || item.end?.date || null,
  }));
}

export async function getCalendarEvents() {
  if (calendarConfig.mode === "gcal_api") return fetchGoogleEvents();
  return fetchIcsEvents();
}
