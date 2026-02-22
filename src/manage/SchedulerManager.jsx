import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import Toast from "../components/Toast";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";
import ConstraintsForm from "./scheduler/ConstraintsForm";
import SchedulePreview from "./scheduler/SchedulePreview";
import ValidationResults from "./scheduler/ValidationResults";

function buildCsv(assignments, division) {
  const header = ["division", "gameDate", "startTime", "endTime", "fieldKey", "homeTeamId", "awayTeamId", "isExternalOffer"];
  const rows = (assignments || []).map((a) => [
    division || "",
    a.gameDate || "",
    a.startTime || "",
    a.endTime || "",
    a.fieldKey || "",
    a.homeTeamId || "",
    a.awayTeamId || "",
    a.isExternalOffer ? "true" : "false",
  ]);
  return [header, ...rows].map((r) => r.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(",")).join("\n");
}

function buildSportsEngineCsv(assignments, fieldsByKey) {
  const header = [
    "Event Type",
    "Event Name (Events Only)",
    "Description (Events Only)",
    "Date",
    "Start Time",
    "End Time",
    "Duration (minutes)",
    "All Day Event (Events Only)",
    "Home Team",
    "Away Team",
    "Teams (Events Only)",
    "Venue",
    "Status",
  ];

  const rows = (assignments || []).map((a) => {
    const start = (a.startTime || "").trim();
    const end = (a.endTime || "").trim();
    const duration = start && end ? calcDurationMinutes(start, end) : "";
    const venue = fieldsByKey.get(a.fieldKey || "") || a.fieldKey || "";
    return [
      "Game",
      "",
      "",
      a.gameDate || "",
      start,
      end,
      duration ? String(duration) : "",
      "",
      a.homeTeamId || "",
      a.awayTeamId || "",
      "",
      venue,
      "Scheduled",
    ];
  });

  return [header, ...rows]
    .map((r) => r.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(","))
    .join("\n");
}

function buildGameChangerCsv(assignments, fieldsByKey) {
  const header = ["Date", "Time", "Home Team", "Away Team", "Location", "Field", "Game Type", "Game Number"];

  const rows = (assignments || []).map((a, idx) => {
    const date = formatDateForGameChanger(a.gameDate);
    const time = formatTimeForGameChanger(a.startTime);
    const venue = fieldsByKey.get(a.fieldKey || "") || a.fieldKey || "";
    const { location, field } = parseVenueForGameChanger(venue);

    return [
      date,
      time,
      a.homeTeamId || "",
      a.awayTeamId || "",
      location,
      field,
      "Regular Season",
      String(idx + 1),
    ];
  });

  return [header, ...rows]
    .map((r) => r.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(","))
    .join("\n");
}

function formatDateForGameChanger(isoDate) {
  // Convert "2026-04-06" to "04/06/2026"
  if (!isoDate) return "";
  const parts = isoDate.split("-");
  if (parts.length !== 3) return isoDate;
  const [year, month, day] = parts;
  return `${month}/${day}/${year}`;
}

function formatTimeForGameChanger(time24) {
  // Convert "18:00" to "6:00 PM"
  if (!time24) return "";
  const parts = time24.split(":");
  if (parts.length < 2) return time24;
  const hour = parseInt(parts[0], 10);
  const minute = parts[1];
  if (isNaN(hour)) return time24;
  const period = hour >= 12 ? "PM" : "AM";
  const hour12 = hour === 0 ? 12 : hour > 12 ? hour - 12 : hour;
  return `${hour12}:${minute} ${period}`;
}

function parseVenueForGameChanger(venue) {
  if (!venue) return { location: "", field: "" };

  // Try splitting on " > "
  if (venue.includes(" > ")) {
    const parts = venue.split(" > ");
    return { location: parts[0].trim(), field: parts.length > 1 ? parts[1].trim() : "" };
  }

  // Try splitting on "/"
  if (venue.includes("/")) {
    const parts = venue.split("/");
    return { location: parts[0].trim(), field: parts.length > 1 ? parts[1].trim() : "" };
  }

  // Try to extract field number from end (e.g., "Oak Park Field 1")
  const match = venue.match(/^(.+?)\s+(Field\s*\d+|Diamond\s*\d+|\d+)$/i);
  if (match) {
    return { location: match[1].trim(), field: match[2].trim() };
  }

  // Fallback: use entire venue as location
  return { location: venue, field: "" };
}

function calcDurationMinutes(start, end) {
  const s = parseTimeMinutes(start);
  const e = parseTimeMinutes(end);
  if (s == null || e == null || e <= s) return null;
  return e - s;
}

function parseTimeMinutes(raw) {
  const parts = (raw || "").split(":");
  if (parts.length < 2) return null;
  const h = Number(parts[0]);
  const m = Number(parts[1]);
  if (!Number.isFinite(h) || !Number.isFinite(m)) return null;
  return h * 60 + m;
}

function downloadCsv(csv, filename) {
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.setAttribute("download", filename);
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function parseIsoDate(value) {
  const parts = (value || "").split("-");
  if (parts.length !== 3) return null;
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return null;
  return new Date(year, month - 1, day);
}

function toIsoDate(d) {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function addDays(d, days) {
  const next = new Date(d.getTime());
  next.setDate(next.getDate() + days);
  return next;
}

function startOfWeek(d) {
  const date = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const day = date.getDay();
  return addDays(date, -day);
}

function startOfMonth(d) {
  return new Date(d.getFullYear(), d.getMonth(), 1);
}

function endOfMonth(d) {
  return new Date(d.getFullYear(), d.getMonth() + 1, 0);
}

function formatWeekRange(start) {
  const end = addDays(start, 6);
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  const startLabel = `${months[start.getMonth()]} ${start.getDate()}`;
  const endLabel = `${months[end.getMonth()]} ${end.getDate()}, ${end.getFullYear()}`;
  return `${startLabel} - ${endLabel}`;
}

function formatMonthLabel(date) {
  const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  return `${months[date.getMonth()]} ${date.getFullYear()}`;
}

function buildMonthWeeks(monthStart) {
  if (!monthStart) return [];
  const weeks = [];
  let cursor = startOfWeek(monthStart);
  const last = startOfWeek(endOfMonth(monthStart));
  while (cursor <= last) {
    weeks.push(Array.from({ length: 7 }, (_, i) => addDays(cursor, i)));
    cursor = addDays(cursor, 7);
  }
  return weeks;
}

function extractSlotItems(payload) {
  if (Array.isArray(payload)) return payload;
  if (!payload || typeof payload !== "object") return [];
  if (Array.isArray(payload.items)) return payload.items;
  if (payload.data && typeof payload.data === "object" && Array.isArray(payload.data.items)) {
    return payload.data.items;
  }
  if (Array.isArray(payload.data)) return payload.data;
  return [];
}

function extractContinuationToken(payload) {
  if (!payload || typeof payload !== "object") return "";
  const token = payload.continuationToken || payload.nextContinuationToken || payload.nextToken || "";
  return String(token || "").trim();
}

export default function SchedulerManager({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");
  const [fields, setFields] = useState([]);
  const [leagueSeason, setLeagueSeason] = useState(null);
  const [divisionSeason, setDivisionSeason] = useState(null);
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [maxGamesPerWeek, setMaxGamesPerWeek] = useState(2);
  const [noDoubleHeaders, setNoDoubleHeaders] = useState(true);
  const [balanceHomeAway, setBalanceHomeAway] = useState(true);
  const [externalOfferPerWeek, setExternalOfferPerWeek] = useState(1);
  const [preview, setPreview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);
  const [overlaySlots, setOverlaySlots] = useState([]);
  const [overlayEvents, setOverlayEvents] = useState([]);
  const [overlayDivisions, setOverlayDivisions] = useState([]);
  const [overlayLoading, setOverlayLoading] = useState(false);
  const [overlayErr, setOverlayErr] = useState("");
  const [overlayView, setOverlayView] = useState("grid");
  const [overlayWeekStart, setOverlayWeekStart] = useState("");
  const [overlayMonthStart, setOverlayMonthStart] = useState("");
  const [overlayAutoKey, setOverlayAutoKey] = useState("");
  const [validation, setValidation] = useState(null);
  const [validationLoading, setValidationLoading] = useState(false);
  const [resetLoading, setResetLoading] = useState(false);

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      try {
        const [divs, flds, league] = await Promise.all([
          apiFetch("/api/divisions"),
          apiFetch("/api/fields"),
          apiFetch("/api/league"),
        ]);
        const list = Array.isArray(divs) ? divs : [];
        setDivisions(list);
        setFields(Array.isArray(flds) ? flds : []);
        setLeagueSeason(league?.season || null);
        if (list.length) {
          setDivision((prev) => prev || list[0].code || list[0].division || "");
        }
      } catch (e) {
        setErr(formatErrorWithRequestId(e, "Failed to load scheduler setup data"));
        setDivisions([]);
        setFields([]);
        setLeagueSeason(null);
        setDivisionSeason(null);
      }
    })();
  }, [leagueId]);

  useEffect(() => {
    if (!leagueId || !division) return;
    (async () => {
      try {
        const data = await apiFetch(`/api/divisions/${encodeURIComponent(division)}/season`);
        setDivisionSeason(data?.season || null);
      } catch {
        setDivisionSeason(null);
      }
    })();
  }, [leagueId, division]);

  const fieldsByKey = useMemo(() => {
    const map = new Map();
    for (const f of fields || []) {
      if (f?.fieldKey && f?.displayName) map.set(f.fieldKey, f.displayName);
    }
    return map;
  }, [fields]);

  const effectiveSeason = useMemo(() => {
    if (!divisionSeason) return leagueSeason;
    if (!leagueSeason) return divisionSeason;
    return {
      springStart: divisionSeason.springStart || leagueSeason.springStart,
      springEnd: divisionSeason.springEnd || leagueSeason.springEnd,
      fallStart: divisionSeason.fallStart || leagueSeason.fallStart,
      fallEnd: divisionSeason.fallEnd || leagueSeason.fallEnd,
      gameLengthMinutes: divisionSeason.gameLengthMinutes || leagueSeason.gameLengthMinutes,
      blackouts: [
        ...(leagueSeason.blackouts || []),
        ...(divisionSeason.blackouts || []),
      ],
    };
  }, [divisionSeason, leagueSeason]);

  const seasonRange = useMemo(() => {
    const fallback = getDefaultRangeFallback();
    const range = getSeasonRange(effectiveSeason, new Date());
    return range || fallback;
  }, [effectiveSeason]);

  useEffect(() => {
    if (!dateFrom) setDateFrom(seasonRange.from);
    if (!dateTo) setDateTo(seasonRange.to);
  }, [seasonRange, dateFrom, dateTo]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const urlFrom = (params.get("dateFrom") || "").trim();
    const urlTo = (params.get("dateTo") || "").trim();
    if (!dateFrom && urlFrom) setDateFrom(urlFrom);
    if (!dateTo && urlTo) setDateTo(urlTo);
  }, [dateFrom, dateTo]);

  useEffect(() => {
    if (overlayWeekStart) return;
    const base = parseIsoDate(dateFrom || seasonRange.from);
    if (base) {
      const weekStart = startOfWeek(base);
      setOverlayWeekStart(toIsoDate(weekStart));
      setOverlayMonthStart(toIsoDate(startOfMonth(weekStart)));
    }
  }, [dateFrom, overlayWeekStart, seasonRange]);

  useEffect(() => {
    if (!leagueId || !dateFrom || !dateTo) return;
    const key = `${leagueId}|${dateFrom}|${dateTo}`;
    if (overlayAutoKey === key) return;
    setOverlayAutoKey(key);
    loadOverlay();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId, dateFrom, dateTo, overlayAutoKey]);

  useEffect(() => {
    if (!overlayDivisions.length && divisions.length) {
      setOverlayDivisions(divisions.map((d) => d.code || d.division).filter(Boolean));
    }
  }, [divisions, overlayDivisions.length]);


  const payload = useMemo(() => {
    return {
      division,
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
      constraints: {
        maxGamesPerWeek: Number(maxGamesPerWeek) || undefined,
        noDoubleHeaders,
        balanceHomeAway,
        externalOfferPerWeek: Number(externalOfferPerWeek) || 0,
      },
    };
  }, [division, dateFrom, dateTo, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, externalOfferPerWeek]);

  function formatErrorWithRequestId(error, fallbackMessage) {
    const message = error?.message || fallbackMessage;
    const requestId = error?.details?.requestId;
    if (!requestId) return message;
    return `${message} (Request ID: ${requestId})`;
  }

  async function runPreview() {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Date from", value: dateFrom, required: false },
      { label: "Date to", value: dateTo, required: false },
    ]);
    if (dateError) return setErr(dateError);
    setLoading(true);
    try {
      const data = await apiFetch("/api/schedule/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setPreview(data || null);
    } catch (e) {
      setErr(formatErrorWithRequestId(e, "Failed to preview schedule"));
      setPreview(null);
    } finally {
      setLoading(false);
    }
  }

  async function applySchedule() {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Date from", value: dateFrom, required: false },
      { label: "Date to", value: dateTo, required: false },
    ]);
    if (dateError) return setErr(dateError);
    setLoading(true);
    try {
      const data = await apiFetch("/api/schedule/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setPreview(data || null);
      setToast({ tone: "success", message: `Schedule applied (run ${data?.runId || "saved"}).` });
    } catch (e) {
      setErr(formatErrorWithRequestId(e, "Failed to apply schedule"));
    } finally {
      setLoading(false);
    }
  }

  async function runValidation() {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Date from", value: dateFrom, required: false },
      { label: "Date to", value: dateTo, required: false },
    ]);
    if (dateError) return setErr(dateError);
    setValidationLoading(true);
    try {
      const data = await apiFetch("/api/schedule/validate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setValidation(data || null);
    } catch (e) {
      setErr(formatErrorWithRequestId(e, "Failed to run validations"));
      setValidation(null);
    } finally {
      setValidationLoading(false);
    }
  }

  async function resetSlotUsage() {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Date from", value: dateFrom, required: false },
      { label: "Date to", value: dateTo, required: false },
    ]);
    if (dateError) return setErr(dateError);

    const rangeLabel = `${dateFrom || "start"} to ${dateTo || "end"}`;
    const confirmed = window.confirm(
      `Reset slot usage for ${division || "this division"} (${rangeLabel})?\n\n` +
      "This keeps slot rows but clears game/practice assignments and deletes related requests in the range."
    );
    if (!confirmed) return;

    setResetLoading(true);
    try {
      const data = await apiFetch("/api/schedule/reset-usage", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division,
          dateFrom: dateFrom || undefined,
          dateTo: dateTo || undefined,
        }),
      });

      setPreview(null);
      setValidation(null);
      setToast({
        tone: "success",
        message:
          `Reset ${data?.resetSlots ?? 0} slot(s); ` +
          `deleted ${data?.slotRequestsDeleted ?? 0} slot request(s) and ${data?.practiceRequestsDeleted ?? 0} practice request(s).`,
      });
    } catch (e) {
      setErr(formatErrorWithRequestId(e, "Failed to reset slot usage"));
    } finally {
      setResetLoading(false);
    }
  }

  function exportCsv() {
    if (!preview?.assignments?.length) return;
    const csv = buildCsv(preview.assignments, division);
    const safeDivision = (division || "division").replace(/[^a-z0-9_-]+/gi, "_");
    downloadCsv(csv, `schedule_${safeDivision}.csv`);
  }

  function exportSportsEngineCsv() {
    if (!preview?.assignments?.length) return;
    const csv = buildSportsEngineCsv(preview.assignments, fieldsByKey);
    const safeDivision = (division || "division").replace(/[^a-z0-9_-]+/gi, "_");
    downloadCsv(csv, `sportsengine_${safeDivision}.csv`);
  }

  function exportGameChangerCsv() {
    if (!preview?.assignments?.length) return;
    const csv = buildGameChangerCsv(preview.assignments, fieldsByKey);
    const safeDivision = (division || "division").replace(/[^a-z0-9_-]+/gi, "_");
    downloadCsv(csv, `gamechanger_${safeDivision}.csv`);
  }

  async function loadOverlay() {
    setOverlayErr("");
    const dateError = validateIsoDates([
      { label: "Date from", value: dateFrom, required: false },
      { label: "Date to", value: dateTo, required: false },
    ]);
    if (dateError) return setOverlayErr(dateError);
    setOverlayLoading(true);
    try {
      const baseQuery = new URLSearchParams();
      if (dateFrom) baseQuery.set("dateFrom", dateFrom);
      if (dateTo) baseQuery.set("dateTo", dateTo);

      const slotList = [];
      let continuationToken = "";
      for (let page = 0; page < 50; page += 1) {
        const slotQuery = new URLSearchParams(baseQuery);
        slotQuery.set("pageSize", "200");
        if (continuationToken) slotQuery.set("continuationToken", continuationToken);
        const slotPage = await apiFetch(`/api/slots?${slotQuery.toString()}`);
        const pageItems = extractSlotItems(slotPage);
        slotList.push(...pageItems);
        const nextToken = extractContinuationToken(slotPage);
        if (!nextToken) break;
        continuationToken = nextToken;
      }

      const eventList = await apiFetch(`/api/events?${baseQuery.toString()}`);
      setOverlaySlots(slotList);
      setOverlayEvents(Array.isArray(eventList) ? eventList : []);
    } catch (e) {
      setOverlayErr(formatErrorWithRequestId(e, "Failed to load overlay data"));
      setOverlaySlots([]);
      setOverlayEvents([]);
    } finally {
      setOverlayLoading(false);
    }
  }

  function toggleOverlayDivision(code) {
    setOverlayDivisions((prev) => {
      if (prev.includes(code)) return prev.filter((c) => c !== code);
      return [...prev, code];
    });
  }

  const divisionColors = useMemo(() => {
    const palette = ["#1f4d7a", "#2f7a5a", "#8c4b2f", "#6b3fa0", "#8a6d2d", "#2a6f6f"];
    const map = new Map();
    let idx = 0;
    for (const d of divisions || []) {
      const code = d.code || d.division;
      if (!code) continue;
      map.set(code, palette[idx % palette.length]);
      idx += 1;
    }
    return map;
  }, [divisions]);

  const knownDivisions = useMemo(() => {
    return new Set((divisions || []).map((d) => d.code || d.division).filter(Boolean));
  }, [divisions]);

  const overlayItems = useMemo(() => {
    const allowed = new Set(overlayDivisions);
    const slotItems = (overlaySlots || [])
      .filter((s) => !allowed.size || !s.division || allowed.has(s.division) || !knownDivisions.has(s.division))
      .map((s) => ({
        kind: s.isAvailability ? "availability" : "slot",
        date: s.gameDate,
        time: `${s.startTime}-${s.endTime}`,
        division: s.division,
        label: s.isAvailability
          ? "Availability"
          : `${s.homeTeamId || s.offeringTeamId || "TBD"} vs ${s.awayTeamId || "TBD"}`,
        field: s.displayName || s.fieldKey || "",
        status: s.status,
        isExternal: !!s.isExternalOffer,
      }));

    const eventItems = (overlayEvents || [])
      .filter((e) => !allowed.size || !e.division || allowed.has(e.division) || !knownDivisions.has(e.division))
      .map((e) => ({
        kind: "event",
        date: e.eventDate,
        time: `${e.startTime || ""}-${e.endTime || ""}`,
        division: e.division || "",
        label: `${e.type ? `${e.type}: ` : ""}${e.title || "Event"}`,
        field: e.location || "",
        status: e.status || "Scheduled",
        isExternal: false,
      }));

    return [...slotItems, ...eventItems]
      .filter((i) => i.date)
      .sort((a, b) => `${a.date} ${a.time}`.localeCompare(`${b.date} ${b.time}`));
  }, [overlaySlots, overlayEvents, overlayDivisions, knownDivisions]);

  const overlayByDate = useMemo(() => {
    const map = new Map();
    for (const item of overlayItems) {
      const list = map.get(item.date) || [];
      list.push(item);
      map.set(item.date, list);
    }
    return map;
  }, [overlayItems]);

  const overlayWeek = useMemo(() => {
    const start = parseIsoDate(overlayWeekStart);
    if (!start) return [];
    return Array.from({ length: 7 }, (_, i) => addDays(start, i));
  }, [overlayWeekStart]);

  const overlayMonthWeeks = useMemo(() => {
    const base = parseIsoDate(overlayMonthStart || overlayWeekStart);
    if (!base) return [];
    return buildMonthWeeks(startOfMonth(base));
  }, [overlayMonthStart, overlayWeekStart]);


  return (
    <div className="stack">
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />
      {err ? <div className="callout callout--error">{err}</div> : null}

      <ConstraintsForm
        divisions={divisions}
        division={division}
        setDivision={setDivision}
        dateFrom={dateFrom}
        setDateFrom={setDateFrom}
        dateTo={dateTo}
        setDateTo={setDateTo}
        maxGamesPerWeek={maxGamesPerWeek}
        setMaxGamesPerWeek={setMaxGamesPerWeek}
        noDoubleHeaders={noDoubleHeaders}
        setNoDoubleHeaders={setNoDoubleHeaders}
        balanceHomeAway={balanceHomeAway}
        setBalanceHomeAway={setBalanceHomeAway}
        externalOfferPerWeek={externalOfferPerWeek}
        setExternalOfferPerWeek={setExternalOfferPerWeek}
        effectiveSeason={effectiveSeason}
        loading={loading}
        validationLoading={validationLoading}
        resetLoading={resetLoading}
        preview={preview}
        runPreview={runPreview}
        applySchedule={applySchedule}
        runValidation={runValidation}
        resetSlotUsage={resetSlotUsage}
        exportCsv={exportCsv}
        exportSportsEngineCsv={exportSportsEngineCsv}
        exportGameChangerCsv={exportGameChangerCsv}
      />

      <SchedulePreview preview={preview} />

      <ValidationResults validation={validation} />

      <div className="card">
        <div className="card__header">
          <div className="h2">Season overlay</div>
          <div className="subtle">See events and scheduled games across divisions with color coding.</div>
        </div>
        <div className="card__body">
          {overlayErr ? <div className="callout callout--error mb-3">{overlayErr}</div> : null}
          <div className="row row--wrap gap-2 mb-3">
            <button
              className={`btn btn--ghost ${overlayView === "list" ? "is-active" : ""}`}
              type="button"
              onClick={() => setOverlayView("list")}
            >
              List view
            </button>
            <button
              className={`btn btn--ghost ${overlayView === "grid" ? "is-active" : ""}`}
              type="button"
              onClick={() => setOverlayView("grid")}
            >
              Calendar view
            </button>
          </div>
          <div className="row row--wrap gap-2 mb-3">
            {divisions.map((d) => {
              const code = d.code || d.division;
              const color = divisionColors.get(code) || "#333";
              return (
                <label key={code} className="inlineCheck" style={{ borderLeft: `4px solid ${color}`, paddingLeft: 8 }}>
                  <input
                    type="checkbox"
                    checked={overlayDivisions.includes(code)}
                    onChange={() => toggleOverlayDivision(code)}
                  />
                  {code}
                </label>
              );
            })}
          </div>
          <div className="row gap-2 mb-3">
            <button className="btn" onClick={loadOverlay} disabled={overlayLoading}>
              {overlayLoading ? "Loading..." : "Refresh overlay"}
            </button>
          </div>
          {overlayItems.length === 0 ? (
            <div className="muted">No items to display for the selected range.</div>
          ) : overlayView === "list" ? (
            <div className="tableWrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Time</th>
                    <th>Division</th>
                    <th>Type</th>
                    <th>Details</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {overlayItems.map((i, idx) => {
                    const color = divisionColors.get(i.division) || "#333";
                    const typeLabel = i.kind === "event"
                      ? "Event"
                      : i.kind === "availability"
                        ? "Availability"
                        : i.isExternal
                          ? (i.status === "Confirmed" ? "External (filled)" : "External (open)")
                          : "Matchup";
                    return (
                      <tr key={`${i.kind}-${i.date}-${i.time}-${idx}`}>
                        <td>{i.date}</td>
                        <td>{i.time}</td>
                        <td>
                          <span className="pill" style={{ borderLeft: `4px solid ${color}` }}>
                            {i.division || "All"}
                          </span>
                        </td>
                        <td>{typeLabel}</td>
                        <td>{i.label} {i.field ? `@ ${i.field}` : ""}</td>
                        <td>{i.status}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : (
            <>
              <div className="row row--between mb-2">
                <div className="font-semibold">
                  {overlayWeek.length ? formatWeekRange(overlayWeek[0]) : "Week view"}
                </div>
                <div className="row gap-2">
                  <button
                    className="btn btn--ghost"
                    type="button"
                    onClick={() => {
                      const current = parseIsoDate(overlayWeekStart);
                      if (!current) return;
                      const next = addDays(current, -7);
                      setOverlayWeekStart(toIsoDate(next));
                      setOverlayMonthStart(toIsoDate(startOfMonth(next)));
                    }}
                  >
                    Prev week
                  </button>
                  <button
                    className="btn btn--ghost"
                    type="button"
                    onClick={() => {
                      const current = parseIsoDate(overlayMonthStart || overlayWeekStart);
                      if (!current) return;
                      const prev = new Date(current.getFullYear(), current.getMonth() - 1, 1);
                      setOverlayMonthStart(toIsoDate(prev));
                    }}
                  >
                    Prev month
                  </button>
                  <button
                    className="btn btn--ghost"
                    type="button"
                    onClick={() => {
                      const base = parseIsoDate(dateFrom || seasonRange.from);
                      if (!base) return;
                      const weekStart = startOfWeek(base);
                      setOverlayWeekStart(toIsoDate(weekStart));
                      setOverlayMonthStart(toIsoDate(startOfMonth(weekStart)));
                    }}
                  >
                    Reset
                  </button>
                  <button
                    className="btn btn--ghost"
                    type="button"
                    onClick={() => {
                      const current = parseIsoDate(overlayMonthStart || overlayWeekStart);
                      if (!current) return;
                      const next = new Date(current.getFullYear(), current.getMonth() + 1, 1);
                      setOverlayMonthStart(toIsoDate(next));
                    }}
                  >
                    Next month
                  </button>
                  <button
                    className="btn btn--ghost"
                    type="button"
                    onClick={() => {
                      const current = parseIsoDate(overlayWeekStart);
                      if (!current) return;
                      const next = addDays(current, 7);
                      setOverlayWeekStart(toIsoDate(next));
                      setOverlayMonthStart(toIsoDate(startOfMonth(next)));
                    }}
                  >
                    Next week
                  </button>
                </div>
              </div>
              <div className="tableWrap mb-3">
                <table className="table text-xs">
                  <thead>
                    <tr>
                      <th colSpan={7} className="text-left">
                        {overlayMonthWeeks.length ? formatMonthLabel(overlayMonthWeeks[0][0]) : "Month"}
                      </th>
                    </tr>
                    <tr>
                      {["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"].map((d) => (
                        <th key={d}>{d}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {overlayMonthWeeks.map((week, wIdx) => (
                      <tr key={`mini-${wIdx}`}>
                        {week.map((day) => {
                          const key = toIsoDate(day);
                          const dayItems = overlayByDate.get(key) || [];
                          const matchups = dayItems.filter((i) => i.kind === "slot" && !i.isExternal).length;
                          const externals = dayItems.filter((i) => i.kind === "slot" && i.isExternal).length;
                          const availability = dayItems.filter((i) => i.kind === "availability").length;
                          const events = dayItems.filter((i) => i.kind === "event").length;
                          const monthBase = overlayMonthWeeks[0][0];
                          const inMonth = day.getMonth() === monthBase.getMonth();
                          const isCurrentWeek = overlayWeekStart === toIsoDate(startOfWeek(day));
                          const badgeItems = [
                            matchups ? { label: `M${matchups}`, color: "#1f4d7a" } : null,
                            externals ? { label: `X${externals}`, color: "#8c4b2f" } : null,
                            availability ? { label: `A${availability}`, color: "#4f5b6a" } : null,
                            events ? { label: `E${events}`, color: "#2a6f6f" } : null,
                          ].filter(Boolean);
                          return (
                            <td key={key}>
                              <button
                                className={`btn btn--ghost ${isCurrentWeek ? "is-active" : ""}`}
                                type="button"
                                onClick={() => {
                                  const weekStart = startOfWeek(day);
                                  setOverlayWeekStart(toIsoDate(weekStart));
                                  setOverlayMonthStart(toIsoDate(startOfMonth(weekStart)));
                                }}
                                title="Jump to week"
                              >
                                <span className={inMonth ? "" : "muted"}>{day.getDate()}</span>
                              </button>
                              {badgeItems.length ? (
                                <div className="text-[10px] mt-1 row row--wrap gap-1">
                                  {badgeItems.map((b) => (
                                    <span key={b.label} className="pill" style={{ borderLeft: `3px solid ${b.color}` }}>
                                      {b.label}
                                    </span>
                                  ))}
                                </div>
                              ) : null}
                            </td>
                          );
                        })}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            <div className="tableWrap">
              <table className="table">
                <thead>
                  <tr>
                    {["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"].map((d) => (
                      <th key={d}>{d}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    {overlayWeek.map((day) => {
                      const key = toIsoDate(day);
                      const items = overlayByDate.get(key) || [];
                      return (
                        <td key={key} className="align-top">
                          <div className="text-xs font-semibold mb-1">{key}</div>
                          {items.length === 0 ? (
                            <div className="muted text-xs">-</div>
                          ) : (
                            <div className="stack gap-1">
                              {items.map((i, idx) => {
                                const color = divisionColors.get(i.division) || "#333";
                                const typeLabel = i.kind === "event"
                                  ? "Event"
                                  : i.kind === "availability"
                                    ? "Availability"
                                    : i.isExternal
                                      ? (i.status === "Confirmed" ? "External (filled)" : "External (open)")
                                      : "Matchup";
                                return (
                                  <div key={`${key}-${idx}`} className="subtle" style={{ borderLeft: `4px solid ${color}`, paddingLeft: 6 }}>
                                    <div className="text-xs">{i.time} {typeLabel}</div>
                                    <div className="text-xs">{i.label}</div>
                                  </div>
                                );
                              })}
                            </div>
                          )}
                        </td>
                      );
                    })}
                  </tr>
                </tbody>
              </table>
            </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
