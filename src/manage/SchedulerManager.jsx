import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import Toast from "../components/Toast";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";

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

const DAY_LABELS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

function buildAvailabilityInsights(slots) {
  const dayStats = DAY_LABELS.map((day) => ({ day, slots: 0, minutes: 0 }));
  for (const s of slots || []) {
    const dt = parseIsoDate(s.gameDate);
    if (!dt) continue;
    const idx = dt.getDay();
    const bucket = dayStats[idx];
    if (!bucket) continue;
    bucket.slots += 1;
    const start = parseTimeMinutes(s.startTime);
    const end = parseTimeMinutes(s.endTime);
    if (start != null && end != null && end > start) bucket.minutes += end - start;
  }
  const totalSlots = dayStats.reduce((sum, d) => sum + d.slots, 0);
  const totalMinutes = dayStats.reduce((sum, d) => sum + d.minutes, 0);
  const ranked = [...dayStats].filter((d) => d.slots > 0)
    .sort((a, b) => (b.slots - a.slots) || (b.minutes - a.minutes));
  const suggested = ranked.slice(0, 2).map((d) => d.day);
  return { dayStats, totalSlots, totalMinutes, suggested };
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
  const [preferredDays, setPreferredDays] = useState({
    Mon: false,
    Tue: false,
    Wed: false,
    Thu: false,
    Fri: false,
    Sat: false,
    Sun: false,
  });
  const [preview, setPreview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);
  const [overlaySlots, setOverlaySlots] = useState([]);
  const [overlayEvents, setOverlayEvents] = useState([]);
  const [overlayDivisions, setOverlayDivisions] = useState([]);
  const [overlayLoading, setOverlayLoading] = useState(false);
  const [overlayView, setOverlayView] = useState("list");
  const [overlayWeekStart, setOverlayWeekStart] = useState("");
  const [overlayMonthStart, setOverlayMonthStart] = useState("");
  const [validation, setValidation] = useState(null);
  const [validationLoading, setValidationLoading] = useState(false);
  const [availabilityInsights, setAvailabilityInsights] = useState(null);
  const [availabilitySlots, setAvailabilitySlots] = useState([]);
  const [availabilityLoading, setAvailabilityLoading] = useState(false);
  const [availabilityErr, setAvailabilityErr] = useState("");
  const [availabilityAllDivisions, setAvailabilityAllDivisions] = useState(true);
  const [availabilityDivision, setAvailabilityDivision] = useState("");
  const [availabilityDateFrom, setAvailabilityDateFrom] = useState("");
  const [availabilityDateTo, setAvailabilityDateTo] = useState("");

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
        if (!division && list.length) setDivision(list[0].code || list[0].division || "");
      } catch (e) {
        setErr(e?.message || "Failed to load divisions");
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
      } catch (e) {
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
    if (overlayWeekStart) return;
    const base = parseIsoDate(dateFrom || seasonRange.from);
    if (base) {
      const weekStart = startOfWeek(base);
      setOverlayWeekStart(toIsoDate(weekStart));
      setOverlayMonthStart(toIsoDate(startOfMonth(weekStart)));
    }
  }, [dateFrom, overlayWeekStart, seasonRange]);

  useEffect(() => {
    if (!overlayDivisions.length && divisions.length) {
      setOverlayDivisions(divisions.map((d) => d.code || d.division).filter(Boolean));
    }
  }, [divisions, overlayDivisions.length]);

  useEffect(() => {
    if (!availabilityDivision && division) setAvailabilityDivision(division);
  }, [availabilityDivision, division]);

  useEffect(() => {
    if (!availabilityDateFrom && dateFrom) setAvailabilityDateFrom(dateFrom);
    if (!availabilityDateTo && dateTo) setAvailabilityDateTo(dateTo);
  }, [availabilityDateFrom, availabilityDateTo, dateFrom, dateTo]);

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
        preferredDays: Object.entries(preferredDays)
          .filter(([, enabled]) => enabled)
          .map(([day]) => day),
      },
    };
  }, [division, dateFrom, dateTo, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, externalOfferPerWeek, preferredDays]);

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
      setErr(e?.message || "Failed to preview schedule");
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
      setErr(e?.message || "Failed to apply schedule");
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
      setErr(e?.message || "Failed to run validations");
      setValidation(null);
    } finally {
      setValidationLoading(false);
    }
  }

  async function loadAvailabilityInsights() {
    setAvailabilityErr("");
    const dateError = validateIsoDates([
      { label: "Date from", value: availabilityDateFrom, required: false },
      { label: "Date to", value: availabilityDateTo, required: false },
    ]);
    if (dateError) return setAvailabilityErr(dateError);
    setAvailabilityLoading(true);
    try {
      const qs = new URLSearchParams();
      if (!availabilityAllDivisions && availabilityDivision) qs.set("division", availabilityDivision);
      if (availabilityDateFrom) qs.set("dateFrom", availabilityDateFrom);
      if (availabilityDateTo) qs.set("dateTo", availabilityDateTo);
      qs.set("status", "Open");
      const data = await apiFetch(`/api/slots?${qs.toString()}`);
      const list = Array.isArray(data) ? data : [];
      const availability = list.filter((s) => s.isAvailability);
      setAvailabilitySlots(availability);
      const insights = buildAvailabilityInsights(availability);
      setAvailabilityInsights(insights);
      const hasPreferred = Object.values(preferredDays).some(Boolean);
      if (!hasPreferred && insights.suggested.length) {
        setPreferredDays((prev) => {
          const next = { ...prev };
          insights.suggested.forEach((day) => { next[day] = true; });
          return next;
        });
      }
    } catch (e) {
      setAvailabilityErr(e?.message || "Failed to load availability slots.");
      setAvailabilitySlots([]);
      setAvailabilityInsights(null);
    } finally {
      setAvailabilityLoading(false);
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

  async function loadOverlay() {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Date from", value: dateFrom, required: false },
      { label: "Date to", value: dateTo, required: false },
    ]);
    if (dateError) return setErr(dateError);
    setOverlayLoading(true);
    try {
      const baseQuery = new URLSearchParams();
      if (dateFrom) baseQuery.set("dateFrom", dateFrom);
      if (dateTo) baseQuery.set("dateTo", dateTo);
      const [slotList, eventList] = await Promise.all([
        apiFetch(`/api/slots?${baseQuery.toString()}`),
        apiFetch(`/api/events?${baseQuery.toString()}`),
      ]);
      setOverlaySlots(Array.isArray(slotList) ? slotList : []);
      setOverlayEvents(Array.isArray(eventList) ? eventList : []);
    } catch (e) {
      setErr(e?.message || "Failed to load overlay data");
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

  const overlayItems = useMemo(() => {
    const allowed = new Set(overlayDivisions);
    const slotItems = (overlaySlots || [])
      .filter((s) => !s.isAvailability)
      .filter((s) => !allowed.size || allowed.has(s.division))
      .map((s) => ({
        kind: "slot",
        date: s.gameDate,
        time: `${s.startTime}-${s.endTime}`,
        division: s.division,
        label: `${s.homeTeamId || s.offeringTeamId || "TBD"} vs ${s.awayTeamId || "TBD"}`,
        field: s.displayName || s.fieldKey || "",
        status: s.status,
        isExternal: !!s.isExternalOffer,
      }));

    const eventItems = (overlayEvents || [])
      .filter((e) => !allowed.size || !e.division || allowed.has(e.division))
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
  }, [overlaySlots, overlayEvents, overlayDivisions]);

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

      <div className="card">
        <div className="card__header">
          <div className="h2">Division scheduler</div>
          <div className="subtle">Build a balanced schedule from your open slots.</div>
        </div>
        {effectiveSeason ? (
          <div className="card__body">
            <div className="subtle">
              Season defaults: Spring {effectiveSeason.springStart || "?"} - {effectiveSeason.springEnd || "?"}, Fall {effectiveSeason.fallStart || "?"} - {effectiveSeason.fallEnd || "?"}. Game length: {effectiveSeason.gameLengthMinutes || "?"} min.
            </div>
          </div>
        ) : null}
        <div className="card__body grid2">
          <label>
            Division
            <select value={division} onChange={(e) => setDivision(e.target.value)}>
              {divisions.map((d) => (
                <option key={d.code || d.division} value={d.code || d.division}>
                  {d.name ? `${d.name} (${d.code || d.division})` : d.code || d.division}
                </option>
              ))}
            </select>
          </label>
          <label>
            Date from (optional)
            <input value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Date to (optional)
            <input value={dateTo} onChange={(e) => setDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Max games/week
            <input
              type="number"
              min="0"
              value={maxGamesPerWeek}
              onChange={(e) => setMaxGamesPerWeek(e.target.value)}
            />
          </label>
          <label className="inlineCheck">
            <input type="checkbox" checked={noDoubleHeaders} onChange={(e) => setNoDoubleHeaders(e.target.checked)} />
            No doubleheaders
          </label>
          <label className="inlineCheck">
            <input type="checkbox" checked={balanceHomeAway} onChange={(e) => setBalanceHomeAway(e.target.checked)} />
            Balance home/away
          </label>
          <label>
            External offers per week
            <input
              type="number"
              min="0"
              value={externalOfferPerWeek}
              onChange={(e) => setExternalOfferPerWeek(e.target.value)}
            />
          </label>
          <div className="stack gap-1">
            <span className="muted">Preferred game days (optional)</span>
            <div className="row row--wrap gap-2">
              {Object.keys(preferredDays).map((day) => (
                <label key={day} className="inlineCheck">
                  <input
                    type="checkbox"
                    checked={preferredDays[day]}
                    onChange={(e) => setPreferredDays((prev) => ({ ...prev, [day]: e.target.checked }))}
                  />
                  {day}
                </label>
              ))}
            </div>
          </div>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={runPreview} disabled={loading || !division}>
            {loading ? "Working..." : "Preview schedule"}
          </button>
          <button className="btn btn--primary" onClick={applySchedule} disabled={loading || !division}>
            Apply schedule
          </button>
          <button className="btn" onClick={runValidation} disabled={validationLoading || !division}>
            {validationLoading ? "Validating..." : "Run validations"}
          </button>
          <button className="btn" onClick={exportCsv} disabled={!preview?.assignments?.length}>
            Export CSV
          </button>
          <button className="btn" onClick={exportSportsEngineCsv} disabled={!preview?.assignments?.length}>
            Export SportsEngine CSV
          </button>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Availability insights</div>
          <div className="subtle">Analyze open availability slots to suggest the best game nights.</div>
        </div>
        <div className="card__body">
          {availabilityErr ? <div className="callout callout--error">{availabilityErr}</div> : null}
          <div className="row gap-2">
            <button className="btn" onClick={loadAvailabilityInsights} disabled={availabilityLoading || (!availabilityAllDivisions && !availabilityDivision)}>
              {availabilityLoading ? "Analyzing..." : "Analyze availability"}
            </button>
            <label className="inlineCheck">
              <input
                type="checkbox"
                checked={availabilityAllDivisions}
                onChange={(e) => setAvailabilityAllDivisions(e.target.checked)}
              />
              All divisions
            </label>
          </div>
        </div>
        <div className="card__body grid2">
          {!availabilityAllDivisions ? (
            <label>
              Division
              <select value={availabilityDivision} onChange={(e) => setAvailabilityDivision(e.target.value)}>
                <option value="">Select division</option>
                {divisions.map((d) => (
                  <option key={d.code || d.division} value={d.code || d.division}>
                    {d.name ? `${d.name} (${d.code || d.division})` : d.code || d.division}
                  </option>
                ))}
              </select>
            </label>
          ) : (
            <div />
          )}
          <label>
            Date from
            <input value={availabilityDateFrom} onChange={(e) => setAvailabilityDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Date to
            <input value={availabilityDateTo} onChange={(e) => setAvailabilityDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
        </div>
        {availabilityInsights ? (
          <div className="card__body">
            <div className="row row--wrap gap-4">
              <div className="layoutStat">
                <div className="layoutStat__value">{availabilityInsights.totalSlots}</div>
                <div className="layoutStat__label">Total slots</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{(availabilityInsights.totalMinutes / 60).toFixed(1)}</div>
                <div className="layoutStat__label">Total hours</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">
                  {availabilityInsights.suggested.length ? availabilityInsights.suggested.join(", ") : "—"}
                </div>
                <div className="layoutStat__label">Suggested nights</div>
              </div>
            </div>
          </div>
        ) : null}
        {availabilityInsights?.dayStats?.length ? (
          <div className="card__body tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Day</th>
                  <th>Slots</th>
                  <th>Hours</th>
                </tr>
              </thead>
              <tbody>
                {availabilityInsights.dayStats.map((d) => (
                  <tr key={d.day}>
                    <td>{d.day}</td>
                    <td>{d.slots}</td>
                    <td>{(d.minutes / 60).toFixed(1)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
        {availabilitySlots.length ? (
          <div className="card__body tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Time</th>
                  <th>Field</th>
                  <th>Division</th>
                </tr>
              </thead>
              <tbody>
                {availabilitySlots.slice(0, 200).map((s) => (
                  <tr key={s.slotId}>
                    <td>{s.gameDate}</td>
                    <td>{s.startTime}-{s.endTime}</td>
                    <td>{s.displayName || s.fieldKey}</td>
                    <td>{s.division}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {availabilitySlots.length > 200 ? <div className="subtle">Showing first 200.</div> : null}
          </div>
        ) : availabilityInsights ? (
          <div className="card__body muted">No availability slots found for this range.</div>
        ) : null}
      </div>

      {preview ? (
        <div className="card">
          <div className="card__header">
            <div className="h2">Preview</div>
            <div className="subtle">Assignments for open slots.</div>
          </div>
          <div className="card__body">
            <div className="row row--wrap gap-4">
              {Object.entries(preview.summary || {}).map(([k, v]) => (
                <div key={k} className="layoutStat">
                  <div className="layoutStat__value">{v}</div>
                  <div className="layoutStat__label">{k}</div>
                </div>
              ))}
            </div>
          </div>
          <div className="card__body">
            {!preview.assignments?.length ? (
              <div className="muted">No assignments yet.</div>
            ) : (
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Time</th>
                      <th>Field</th>
                      <th>Home</th>
                      <th>Away</th>
                      <th>External</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.assignments.map((a) => (
                      <tr key={a.slotId}>
                        <td>{a.gameDate}</td>
                        <td>{a.startTime}-{a.endTime}</td>
                        <td>{a.fieldKey}</td>
                        <td>{a.homeTeamId || "-"}</td>
                        <td>{a.awayTeamId || "TBD"}</td>
                        <td>{a.isExternalOffer ? "Yes" : "No"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
          {preview.unassignedMatchups?.length ? (
            <div className="card__body">
              <div className="h2">Unassigned matchups</div>
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Home</th>
                      <th>Away</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.unassignedMatchups.map((m, idx) => (
                      <tr key={`${m.homeTeamId}-${m.awayTeamId}-${idx}`}>
                        <td>{m.homeTeamId}</td>
                        <td>{m.awayTeamId}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ) : null}
          {preview.unassignedSlots?.length ? (
            <div className="card__body">
              <div className="h2">Unused slots</div>
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Time</th>
                      <th>Field</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.unassignedSlots.map((s) => (
                      <tr key={s.slotId}>
                        <td>{s.gameDate}</td>
                        <td>{s.startTime}-{s.endTime}</td>
                        <td>{s.fieldKey}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ) : null}
          {preview.failures?.length ? (
            <div className="card__body">
              <div className="h2">Validation issues</div>
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Rule</th>
                      <th>Severity</th>
                      <th>Message</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.failures.map((f, idx) => (
                      <tr key={`${f.ruleId || "issue"}-${idx}`}>
                        <td>{f.ruleId || ""}</td>
                        <td>{f.severity || ""}</td>
                        <td>{f.message || ""}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ) : null}
        </div>
      ) : null}

      {validation ? (
        <div className="card">
          <div className="card__header">
            <div className="h2">Validation results</div>
            <div className="subtle">Checks against scheduled games in the selected range.</div>
          </div>
          <div className="card__body">
            <div className="row row--wrap gap-4">
              <div className="layoutStat">
                <div className="layoutStat__value">{validation.totalIssues ?? 0}</div>
                <div className="layoutStat__label">Total issues</div>
              </div>
            </div>
          </div>
          {validation.issues?.length ? (
            <div className="card__body tableWrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Rule</th>
                    <th>Severity</th>
                    <th>Message</th>
                  </tr>
                </thead>
                <tbody>
                  {validation.issues.map((f, idx) => (
                    <tr key={`${f.ruleId || "issue"}-${idx}`}>
                      <td>{f.ruleId || ""}</td>
                      <td>{f.severity || ""}</td>
                      <td>{f.message || ""}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="card__body muted">No validation issues found.</div>
          )}
        </div>
      ) : null}

      <div className="card">
        <div className="card__header">
          <div className="h2">Season overlay</div>
          <div className="subtle">See events and scheduled games across divisions with color coding.</div>
        </div>
        <div className="card__body">
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
                          const events = dayItems.filter((i) => i.kind === "event").length;
                          const monthBase = overlayMonthWeeks[0][0];
                          const inMonth = day.getMonth() === monthBase.getMonth();
                          const isCurrentWeek = overlayWeekStart === toIsoDate(startOfWeek(day));
                          const badgeItems = [
                            matchups ? { label: `M${matchups}`, color: "#1f4d7a" } : null,
                            externals ? { label: `X${externals}`, color: "#8c4b2f" } : null,
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
