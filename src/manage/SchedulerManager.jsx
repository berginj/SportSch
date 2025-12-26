import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
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

export default function SchedulerManager({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");
  const [fields, setFields] = useState([]);
  const [leagueSeason, setLeagueSeason] = useState(null);
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
  const [slotGenDivision, setSlotGenDivision] = useState("");
  const [slotGenFieldKey, setSlotGenFieldKey] = useState("");
  const [slotGenStartTime, setSlotGenStartTime] = useState("");
  const [slotGenEndTime, setSlotGenEndTime] = useState("");
  const [slotGenDays, setSlotGenDays] = useState({
    Mon: false,
    Tue: false,
    Wed: false,
    Thu: false,
    Fri: false,
    Sat: false,
    Sun: false,
  });
  const [slotGenPreview, setSlotGenPreview] = useState(null);
  const [overlaySlots, setOverlaySlots] = useState([]);
  const [overlayEvents, setOverlayEvents] = useState([]);
  const [overlayDivisions, setOverlayDivisions] = useState([]);
  const [overlayLoading, setOverlayLoading] = useState(false);
  const [overlayView, setOverlayView] = useState("list");

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
        if (!slotGenDivision && list.length) setSlotGenDivision(list[0].code || list[0].division || "");
        if (!slotGenFieldKey && Array.isArray(flds) && flds.length) setSlotGenFieldKey(flds[0].fieldKey || "");
      } catch (e) {
        setErr(e?.message || "Failed to load divisions");
        setDivisions([]);
        setFields([]);
        setLeagueSeason(null);
      }
    })();
  }, [leagueId]);

  const fieldsByKey = useMemo(() => {
    const map = new Map();
    for (const f of fields || []) {
      if (f?.fieldKey && f?.displayName) map.set(f.fieldKey, f.displayName);
    }
    return map;
  }, [fields]);

  const seasonRange = useMemo(() => {
    const fallback = getDefaultRangeFallback();
    const range = getSeasonRange(leagueSeason, new Date());
    return range || fallback;
  }, [leagueSeason]);

  useEffect(() => {
    if (!dateFrom) setDateFrom(seasonRange.from);
    if (!dateTo) setDateTo(seasonRange.to);
  }, [seasonRange, dateFrom, dateTo]);

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

  async function runPreview() {
    setErr("");
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

  const overlayWeeks = useMemo(() => {
    const start = parseIsoDate(dateFrom || seasonRange.from);
    const end = parseIsoDate(dateTo || seasonRange.to);
    if (!start || !end) return [];
    const weeks = [];
    let cursor = startOfWeek(start);
    const last = startOfWeek(end);
    while (cursor <= last) {
      const days = Array.from({ length: 7 }, (_, i) => addDays(cursor, i));
      weeks.push(days);
      cursor = addDays(cursor, 7);
    }
    return weeks;
  }, [dateFrom, dateTo, seasonRange]);

  async function previewSlotGeneration() {
    setErr("");
    setLoading(true);
    try {
      const days = Object.entries(slotGenDays)
        .filter(([, on]) => on)
        .map(([k]) => k);
      const data = await apiFetch("/api/schedule/slots/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: slotGenDivision,
          fieldKey: slotGenFieldKey,
          dateFrom,
          dateTo,
          daysOfWeek: days,
          startTime: slotGenStartTime,
          endTime: slotGenEndTime,
        }),
      });
      setSlotGenPreview(data || null);
    } catch (e) {
      setErr(e?.message || "Failed to preview slots");
      setSlotGenPreview(null);
    } finally {
      setLoading(false);
    }
  }

  async function applySlotGeneration(mode) {
    setErr("");
    setLoading(true);
    try {
      const days = Object.entries(slotGenDays)
        .filter(([, on]) => on)
        .map(([k]) => k);
      const data = await apiFetch(`/api/schedule/slots/apply?mode=${encodeURIComponent(mode)}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: slotGenDivision,
          fieldKey: slotGenFieldKey,
          dateFrom,
          dateTo,
          daysOfWeek: days,
          startTime: slotGenStartTime,
          endTime: slotGenEndTime,
        }),
      });
      setSlotGenPreview(null);
      setToast({ tone: "success", message: `Generated slots (${data?.created?.length || 0} created).` });
    } catch (e) {
      setErr(e?.message || "Failed to generate slots");
    } finally {
      setLoading(false);
    }
  }

  function toggleDay(day) {
    setSlotGenDays((prev) => ({ ...prev, [day]: !prev[day] }));
  }

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
        {leagueSeason ? (
          <div className="card__body">
            <div className="subtle">
              Season defaults: Spring {leagueSeason.springStart || "?"} - {leagueSeason.springEnd || "?"}, Fall {leagueSeason.fallStart || "?"} - {leagueSeason.fallEnd || "?"}. Game length: {leagueSeason.gameLengthMinutes || "?"} min.
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
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={runPreview} disabled={loading || !division}>
            {loading ? "Working..." : "Preview schedule"}
          </button>
          <button className="btn btn--primary" onClick={applySchedule} disabled={loading || !division}>
            Apply schedule
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
          <div className="h2">Field slot generator</div>
          <div className="subtle">
            Generate open availability slots for a division and field. Uses league game length (no buffers).
          </div>
        </div>
        {leagueSeason && !leagueSeason.gameLengthMinutes ? (
          <div className="card__body">
            <div className="callout callout--error">
              League game length is not set. Ask a global admin to configure season settings.
            </div>
          </div>
        ) : null}
        <div className="card__body grid2">
          <label>
            Division
            <select value={slotGenDivision} onChange={(e) => setSlotGenDivision(e.target.value)}>
              {divisions.map((d) => (
                <option key={d.code || d.division} value={d.code || d.division}>
                  {d.name ? `${d.name} (${d.code || d.division})` : d.code || d.division}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field
            <select value={slotGenFieldKey} onChange={(e) => setSlotGenFieldKey(e.target.value)}>
              <option value="">Select field</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName}
                </option>
              ))}
            </select>
          </label>
          <label>
            Start time
            <input value={slotGenStartTime} onChange={(e) => setSlotGenStartTime(e.target.value)} placeholder="17:00" />
          </label>
          <label>
            End time
            <input value={slotGenEndTime} onChange={(e) => setSlotGenEndTime(e.target.value)} placeholder="22:00" />
          </label>
          <label>
            Season range
            <div className="row gap-2 row--wrap">
              <input value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
              <input value={dateTo} onChange={(e) => setDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
            </div>
          </label>
          <label>
            Game length (minutes)
            <input value={leagueSeason?.gameLengthMinutes || 0} readOnly />
          </label>
          <div>
            <div className="mb-2">Days of week</div>
            <div className="row row--wrap gap-2">
              {Object.keys(slotGenDays).map((day) => (
                <label key={day} className="inlineCheck">
                  <input type="checkbox" checked={slotGenDays[day]} onChange={() => toggleDay(day)} />
                  {day}
                </label>
              ))}
            </div>
          </div>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={previewSlotGeneration} disabled={loading || !slotGenDivision || !slotGenFieldKey}>
            Preview slots
          </button>
          <button className="btn btn--primary" onClick={() => applySlotGeneration("skip")} disabled={loading || !slotGenDivision || !slotGenFieldKey}>
            Generate (skip conflicts)
          </button>
          <button className="btn" onClick={() => applySlotGeneration("overwrite")} disabled={loading || !slotGenDivision || !slotGenFieldKey}>
            Generate (overwrite availability)
          </button>
        </div>
        {slotGenPreview ? (
          <div className="card__body">
            <div className="h2">Preview</div>
            <div className="row row--wrap gap-4">
              <div className="layoutStat">
                <div className="layoutStat__value">{slotGenPreview.slots?.length || 0}</div>
                <div className="layoutStat__label">Slots</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{slotGenPreview.conflicts?.length || 0}</div>
                <div className="layoutStat__label">Conflicts</div>
              </div>
            </div>
            {slotGenPreview.conflicts?.length ? (
              <div className="mt-3 tableWrap">
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
                    {slotGenPreview.conflicts.slice(0, 20).map((c, idx) => (
                      <tr key={`${c.gameDate}-${c.startTime}-${idx}`}>
                        <td>{c.gameDate}</td>
                        <td>{c.startTime}-{c.endTime}</td>
                        <td>{c.fieldKey}</td>
                        <td>{c.division}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {slotGenPreview.conflicts.length > 20 ? (
                  <div className="subtle">Showing first 20 conflicts.</div>
                ) : null}
              </div>
            ) : null}
          </div>
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
                  {overlayWeeks.map((week, wIdx) => (
                    <tr key={`week-${wIdx}`}>
                      {week.map((day) => {
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
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
