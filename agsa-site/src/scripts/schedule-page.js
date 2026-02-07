import { getCalendarEvents } from "./calendar-adapter";

const listEl = document.querySelector("[data-schedule-list]");
const divisionEl = document.querySelector("[data-schedule-filter-division]");
const teamEl = document.querySelector("[data-schedule-filter-team]");
const locationEl = document.querySelector("[data-schedule-filter-location]");
const searchEl = document.querySelector("[data-schedule-search]");
const statusEl = document.querySelector("[data-schedule-status]");
const monthGridEl = document.querySelector("[data-calendar-grid]");
const copyLinkBtn = document.querySelector("[data-copy-link]");
const downloadIcsBtn = document.querySelector("[data-download-ics]");
const toggleButtons = Array.from(document.querySelectorAll("[data-view-toggle]"));
const panels = Array.from(document.querySelectorAll("[data-view-panel]"));

function parseMeta(event) {
  const summary = event.summary || "";
  const divisionMatch = summary.match(/\b(6U|8U|10U|12U|14U|16U|18U)\b/i);
  const teamMatch = summary.match(/([A-Za-z0-9 .'-]+)\s+(?:vs\.?|@)\s+([A-Za-z0-9 .'-]+)/i);
  return {
    division: divisionMatch ? divisionMatch[1].toUpperCase() : "General",
    team: teamMatch ? `${teamMatch[1].trim()} vs ${teamMatch[2].trim()}` : "Unassigned",
    location: event.location || "TBD",
  };
}

function formatDate(value) {
  const d = new Date(value);
  return d.toLocaleString("en-US", {
    weekday: "short",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function renderMonthGrid(events) {
  const byDay = new Map();
  events.forEach((event) => {
    const day = new Date(event.start).toISOString().slice(0, 10);
    byDay.set(day, (byDay.get(day) || 0) + 1);
  });

  const cells = [];
  for (let i = 0; i < 35; i += 1) {
    const d = new Date();
    d.setDate(d.getDate() + i);
    const key = d.toISOString().slice(0, 10);
    const count = byDay.get(key) || 0;
    cells.push(`
      <div class="rounded-lg border border-slate-200 p-2 text-xs">
        <div class="font-semibold text-slate-700">${d.toLocaleDateString("en-US", { month: "short", day: "numeric" })}</div>
        <div class="mt-1 ${count ? "text-brand-primary" : "text-slate-400"}">${count ? `${count} event${count > 1 ? "s" : ""}` : "No events"}</div>
      </div>
    `);
  }
  monthGridEl.innerHTML = cells.join("");
}

function buildFilterState() {
  return {
    division: divisionEl.value,
    team: teamEl.value,
    location: locationEl.value,
    query: searchEl.value.trim().toLowerCase(),
  };
}

function applyFilters(events, state) {
  return events.filter((event) => {
    const meta = parseMeta(event);
    const haystack = `${event.summary || ""} ${event.description || ""} ${meta.team} ${meta.location}`.toLowerCase();
    return (state.division === "all" || meta.division === state.division)
      && (state.team === "all" || meta.team === state.team)
      && (state.location === "all" || meta.location === state.location)
      && (!state.query || haystack.includes(state.query));
  });
}

function toIcsDate(value) {
  const d = new Date(value);
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getUTCFullYear()}${pad(d.getUTCMonth() + 1)}${pad(d.getUTCDate())}T${pad(d.getUTCHours())}${pad(d.getUTCMinutes())}${pad(d.getUTCSeconds())}Z`;
}

function createIcs(events) {
  const body = events.map((event, idx) => [
    "BEGIN:VEVENT",
    `UID:agsa-${idx}-${toIcsDate(event.start)}@agsafastpitch.com`,
    `DTSTAMP:${toIcsDate(new Date().toISOString())}`,
    `DTSTART:${toIcsDate(event.start)}`,
    event.end ? `DTEND:${toIcsDate(event.end)}` : "",
    `SUMMARY:${(event.summary || "AGSA Event").replace(/\n/g, " ")}`,
    `LOCATION:${(event.location || "").replace(/\n/g, " ")}`,
    `DESCRIPTION:${(event.description || "").replace(/\n/g, " ")}`,
    "END:VEVENT"
  ].filter(Boolean).join("\r\n")).join("\r\n");
  return `BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//AGSA//Schedule//EN\r\n${body}\r\nEND:VCALENDAR\r\n`;
}

function writeUrl(state, view) {
  const params = new URLSearchParams(window.location.search);
  const pairs = { division: state.division, team: state.team, location: state.location, q: state.query, view };
  Object.entries(pairs).forEach(([key, val]) => {
    if (val && val !== "all") params.set(key, val);
    else params.delete(key);
  });
  const next = `${window.location.pathname}?${params.toString()}`;
  window.history.replaceState({}, "", next);
}

function readUrlState() {
  const params = new URLSearchParams(window.location.search);
  return {
    division: params.get("division") || "all",
    team: params.get("team") || "all",
    location: params.get("location") || "all",
    query: params.get("q") || "",
    view: params.get("view") || "list",
  };
}

function setView(view) {
  panels.forEach((panel) => {
    panel.hidden = panel.getAttribute("data-view-panel") !== view;
  });
  toggleButtons.forEach((btn) => {
    const active = btn.getAttribute("data-view-toggle") === view;
    btn.classList.toggle("btn-primary", active);
    btn.classList.toggle("btn-secondary", !active);
  });
}

async function copyLink() {
  try {
    await navigator.clipboard.writeText(window.location.href);
    statusEl.textContent = "Filtered link copied.";
  } catch {
    statusEl.textContent = "Unable to copy link in this browser.";
  }
}

function renderList(filtered) {
  statusEl.textContent = `${filtered.length} event${filtered.length === 1 ? "" : "s"} shown`;
  listEl.innerHTML = filtered.length
    ? filtered.map((event) => {
      const meta = parseMeta(event);
      return `
        <li class="rounded-lg border border-slate-200 p-4">
          <p class="m-0 text-xs font-semibold uppercase tracking-wide text-brand-primary">${meta.division}</p>
          <h3 class="mt-1 text-lg font-semibold text-brand-secondary">${event.summary || "Untitled event"}</h3>
          <p class="mt-1 text-sm text-slate-600">${formatDate(event.start)}</p>
          <p class="mt-1 text-sm text-slate-500">${meta.team} · ${meta.location}</p>
        </li>
      `;
    }).join("")
    : '<li class="rounded-lg border border-dashed border-slate-300 p-4 text-sm text-slate-600">No events match the current filters.</li>';
}

async function boot() {
  try {
    statusEl.textContent = "Loading events...";
    const allEvents = await getCalendarEvents();
    const decorated = allEvents.map((event) => ({ ...event, meta: parseMeta(event) }));

    const divisions = [...new Set(decorated.map((x) => x.meta.division))].sort();
    const teams = [...new Set(decorated.map((x) => x.meta.team))].sort();
    const locations = [...new Set(decorated.map((x) => x.meta.location))].sort();

    divisionEl.innerHTML = '<option value="all">All divisions</option>' + divisions.map((x) => `<option value="${x}">${x}</option>`).join("");
    teamEl.innerHTML = '<option value="all">All teams</option>' + teams.map((x) => `<option value="${x}">${x}</option>`).join("");
    locationEl.innerHTML = '<option value="all">All locations</option>' + locations.map((x) => `<option value="${x}">${x}</option>`).join("");

    const urlState = readUrlState();
    if (divisions.includes(urlState.division)) divisionEl.value = urlState.division;
    if (teams.includes(urlState.team)) teamEl.value = urlState.team;
    if (locations.includes(urlState.location)) locationEl.value = urlState.location;
    searchEl.value = urlState.query;
    setView(urlState.view === "calendar" ? "calendar" : "list");

    const refresh = () => {
      const state = buildFilterState();
      const activeView = panels.find((p) => !p.hidden)?.getAttribute("data-view-panel") || "list";
      const filtered = applyFilters(decorated, state);
      renderList(filtered);
      renderMonthGrid(filtered);
      writeUrl(state, activeView);
      downloadIcsBtn.onclick = () => {
        const ics = createIcs(filtered);
        const blob = new Blob([ics], { type: "text/calendar;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = "agsa-filtered-schedule.ics";
        a.click();
        URL.revokeObjectURL(url);
      };
    };

    [divisionEl, teamEl, locationEl, searchEl].forEach((el) => el.addEventListener("input", refresh));
    toggleButtons.forEach((btn) => {
      btn.addEventListener("click", () => {
        setView(btn.getAttribute("data-view-toggle"));
        refresh();
      });
    });
    copyLinkBtn.addEventListener("click", copyLink);
    refresh();
  } catch (error) {
    statusEl.textContent = error.message || "Unable to load calendar events.";
    listEl.innerHTML = `
      <li class="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
        Calendar feed could not be loaded. Check calendar.json and feed accessibility.
      </li>
    `;
  }
}

boot();
