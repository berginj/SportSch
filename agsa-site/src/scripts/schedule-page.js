import { getCalendarEvents } from "./calendar-adapter";

const listEl = document.querySelector("[data-schedule-list]");
const filterEl = document.querySelector("[data-schedule-filter]");
const searchEl = document.querySelector("[data-schedule-search]");
const statusEl = document.querySelector("[data-schedule-status]");
const monthGridEl = document.querySelector("[data-calendar-grid]");

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

function extractDivision(summary) {
  const match = summary.match(/\b(6U|8U|10U|12U|14U|16U|18U)\b/i);
  return match ? match[1].toUpperCase() : "General";
}

function renderMonthGrid(events) {
  const byDay = new Map();
  events.forEach((event) => {
    const day = new Date(event.start).toISOString().slice(0, 10);
    byDay.set(day, (byDay.get(day) || 0) + 1);
  });

  const nextDays = [];
  for (let i = 0; i < 35; i += 1) {
    const d = new Date();
    d.setDate(d.getDate() + i);
    nextDays.push(d);
  }

  monthGridEl.innerHTML = nextDays
    .map((date) => {
      const key = date.toISOString().slice(0, 10);
      const count = byDay.get(key) || 0;
      return `
        <div class="rounded-lg border border-slate-200 p-2 text-xs">
          <div class="font-semibold text-slate-700">${date.toLocaleDateString("en-US", { month: "short", day: "numeric" })}</div>
          <div class="mt-1 ${count ? "text-brand-primary" : "text-slate-400"}">${count ? `${count} event${count > 1 ? "s" : ""}` : "No events"}</div>
        </div>
      `;
    })
    .join("");
}

function renderList(events) {
  const query = (searchEl.value || "").trim().toLowerCase();
  const selectedDivision = filterEl.value;
  const filtered = events.filter((event) => {
    const division = extractDivision(event.summary);
    const haystack = `${event.summary} ${event.location} ${event.description}`.toLowerCase();
    const matchesQuery = !query || haystack.includes(query);
    const matchesDivision = selectedDivision === "all" || division === selectedDivision;
    return matchesQuery && matchesDivision;
  });

  statusEl.textContent = `${filtered.length} event${filtered.length === 1 ? "" : "s"} shown`;

  listEl.innerHTML = filtered.length
    ? filtered
        .map(
          (event) => `
          <li class="rounded-lg border border-slate-200 p-4">
            <p class="m-0 text-xs font-semibold uppercase tracking-wide text-brand-primary">${extractDivision(event.summary)}</p>
            <h3 class="mt-1 text-lg font-semibold text-brand-secondary">${event.summary}</h3>
            <p class="mt-2 text-sm text-slate-600">${formatDate(event.start)}</p>
            ${event.location ? `<p class="mt-1 text-sm text-slate-500">${event.location}</p>` : ""}
          </li>
        `
        )
        .join("")
    : '<li class="rounded-lg border border-dashed border-slate-300 p-4 text-sm text-slate-600">No events match the current filters.</li>';
}

async function boot() {
  try {
    statusEl.textContent = "Loading events...";
    const events = await getCalendarEvents();
    const divisions = Array.from(new Set(events.map((event) => extractDivision(event.summary)))).sort();
    filterEl.innerHTML =
      '<option value="all">All divisions</option>' +
      divisions.map((division) => `<option value="${division}">${division}</option>`).join("");

    renderMonthGrid(events);
    renderList(events);
    filterEl.addEventListener("change", () => renderList(events));
    searchEl.addEventListener("input", () => renderList(events));
  } catch (error) {
    statusEl.textContent = error.message || "Unable to load calendar events.";
    listEl.innerHTML = `
      <li class="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
        Calendar feed could not be loaded. This is commonly caused by ICS CORS restrictions.
        You can switch to gcal_api mode in calendar.json or use a CORS-friendly ICS URL.
      </li>
    `;
  }
}

boot();
