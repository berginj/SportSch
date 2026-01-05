import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";
import LeaguePicker from "../components/LeaguePicker";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";

function isPracticeSlot(slot) {
  return (slot?.gameType || "").trim().toLowerCase() === "practice";
}

function formatMatchup(slot) {
  if (isPracticeSlot(slot)) {
    const team = (slot?.confirmedTeamId || slot?.offeringTeamId || "").trim();
    return team ? `Practice: ${team}` : "Practice";
  }
  const home = (slot?.homeTeamId || slot?.offeringTeamId || "").trim();
  const away = (slot?.awayTeamId || "").trim();
  if (away) return `${home} vs ${away}`;
  return home ? `${home} vs TBD` : "";
}

function formatSlotTitle(slot) {
  const matchup = formatMatchup(slot);
  const location = slot.displayName || `${slot.parkName || ""} ${slot.fieldName || ""}`.trim();
  if (matchup && location) return `${matchup} @ ${location}`;
  if (matchup) return matchup;
  return location || slot.slotId || "";
}

function matchesTeamFilter(slot, teamId) {
  if (!teamId) return true;
  const normalized = teamId.trim();
  if (!normalized) return true;
  const candidates = [
    slot?.offeringTeamId,
    slot?.homeTeamId,
    slot?.awayTeamId,
    slot?.confirmedTeamId,
  ]
    .map((value) => (value || "").trim())
    .filter(Boolean);
  return candidates.includes(normalized);
}

function matchesFieldFilter(slot, fieldKey) {
  if (!fieldKey) return true;
  return (slot?.fieldKey || "").trim() === fieldKey.trim();
}

function csvEscape(value) {
  const safe = String(value ?? "");
  if (safe.includes("\"")) {
    return `"${safe.replaceAll("\"", "\"\"")}"`;
  }
  if (safe.includes(",") || safe.includes("\n")) {
    return `"${safe}"`;
  }
  return safe;
}

function statusClassForSlot(slot) {
  const raw = (slot?.status || "").toLowerCase();
  if (!raw) return "timelineItem timelineItem--slot";
  return `timelineItem timelineItem--slot status-${raw}`;
}

export default function SchedulePage({ me, leagueId, setLeagueId }) {
  const [mode, setMode] = useState("calendar");
  const [divisions, setDivisions] = useState([]);
  const [teams, setTeams] = useState([]);
  const [fields, setFields] = useState([]);
  const [division, setDivision] = useState("");
  const [teamId, setTeamId] = useState("");
  const [fieldKey, setFieldKey] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [slots, setSlots] = useState([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);
  const defaultsRef = useRef(getDefaultRangeFallback());

  const applyFiltersFromUrl = useCallback((defaults) => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    setMode((params.get("mode") || "calendar").trim().toLowerCase() === "list" ? "list" : "calendar");
    setDivision((params.get("division") || "").trim());
    setTeamId((params.get("teamId") || "").trim());
    setFieldKey((params.get("fieldKey") || "").trim());
    setDateFrom((params.get("dateFrom") || "").trim() || defaults.from);
    setDateTo((params.get("dateTo") || "").trim() || defaults.to);
  }, []);

  async function loadMeta() {
    const [divs, flds] = await Promise.all([apiFetch("/api/divisions"), apiFetch("/api/fields")]);
    setDivisions(Array.isArray(divs) ? divs : []);
    setFields(Array.isArray(flds) ? flds : []);
    try {
      const tms = await apiFetch("/api/teams");
      setTeams(Array.isArray(tms) ? tms : []);
    } catch {
      setTeams([]);
    }
  }

  async function loadSlots(overrides = null) {
    const current = overrides || {
      division,
      dateFrom,
      dateTo,
    };
    setErr("");
    setLoading(true);
    try {
      const params = new URLSearchParams();
      if (current.division) params.set("division", current.division);
      if (current.dateFrom) params.set("dateFrom", current.dateFrom);
      if (current.dateTo) params.set("dateTo", current.dateTo);
      params.set("status", "Open,Confirmed");
      const result = await apiFetch(`/api/slots?${params.toString()}`);
      setSlots(Array.isArray(result) ? result : []);
    } catch (e) {
      setErr(e?.message || String(e));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    (async () => {
      let defaults = getDefaultRangeFallback();
      try {
        const league = await apiFetch("/api/league");
        const seasonRange = getSeasonRange(league?.season, new Date());
        if (seasonRange) defaults = seasonRange;
      } catch {
        // ignore season config
      }
      defaultsRef.current = defaults;
      applyFiltersFromUrl(defaults);
      try {
        await loadMeta();
      } catch {
        // ignore meta errors
      }
      const params = new URLSearchParams(typeof window !== "undefined" ? window.location.search : "");
      const initial = {
        division: (params.get("division") || "").trim(),
        dateFrom: (params.get("dateFrom") || "").trim() || defaults.from,
        dateTo: (params.get("dateTo") || "").trim() || defaults.to,
      };
      await loadSlots(initial);
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const onPopState = () => applyFiltersFromUrl(defaultsRef.current);
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, [applyFiltersFromUrl]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (division) params.set("division", division);
    else params.delete("division");
    if (teamId) params.set("teamId", teamId);
    else params.delete("teamId");
    if (fieldKey) params.set("fieldKey", fieldKey);
    else params.delete("fieldKey");
    if (dateFrom) params.set("dateFrom", dateFrom);
    else params.delete("dateFrom");
    if (dateTo) params.set("dateTo", dateTo);
    else params.delete("dateTo");
    if (mode === "list") params.set("mode", "list");
    else params.delete("mode");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [division, teamId, fieldKey, dateFrom, dateTo, mode]);

  const filteredSlots = useMemo(() => {
    return (slots || [])
      .filter((s) => !s.isAvailability)
      .filter((s) => matchesTeamFilter(s, teamId))
      .filter((s) => matchesFieldFilter(s, fieldKey))
      .sort((a, b) => {
        const ad = `${a.gameDate || ""}T${a.startTime || "00:00"}`;
        const bd = `${b.gameDate || ""}T${b.startTime || "00:00"}`;
        return ad.localeCompare(bd);
      });
  }, [slots, teamId, fieldKey]);

  const timeline = useMemo(() => {
    return filteredSlots
      .map((s) => ({
        id: s.slotId,
        date: s.gameDate,
        start: s.startTime || "",
        end: s.endTime || "",
        title: formatSlotTitle(s),
        subtitle: [
          s.division ? `Division: ${s.division}` : "",
          s.status ? `Status: ${s.status}` : "",
          s.confirmedTeamId ? `Confirmed: ${s.confirmedTeamId}` : "",
        ]
          .filter(Boolean)
          .join(" | "),
        raw: s,
      }))
      .filter((x) => x.date)
      .sort((a, b) => {
        const ad = `${a.date}T${a.start || "00:00"}`;
        const bd = `${b.date}T${b.start || "00:00"}`;
        return ad.localeCompare(bd) || (a.title || "").localeCompare(b.title || "");
      });
  }, [filteredSlots]);

  function refreshData() {
    loadSlots();
  }

  function exportFiltered() {
    if (!filteredSlots.length) {
      setToast({ tone: "warning", message: "No schedule items match the current filters." });
      return;
    }
    const headers = [
      "Date",
      "Start",
      "End",
      "Division",
      "Field",
      "FieldKey",
      "Matchup",
      "Status",
      "OfferingTeam",
      "AwayTeam",
      "ConfirmedTeam",
    ];
    const rows = filteredSlots.map((s) => [
      s.gameDate,
      s.startTime,
      s.endTime,
      s.division,
      s.displayName || s.fieldName || "",
      s.fieldKey,
      formatMatchup(s),
      s.status,
      s.offeringTeamId,
      s.awayTeamId,
      s.confirmedTeamId,
    ]);
    const csv = [headers, ...rows].map((row) => row.map(csvEscape).join(",")).join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    const stamp = new Date().toISOString().slice(0, 10);
    link.href = url;
    link.download = `schedule-export-${stamp}.csv`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }

  if (loading) return <StatusCard title="Loading" message="Loading schedule..." />;

  return (
    <div className="stack">
      {err ? <StatusCard tone="error" title="Unable to load schedule" message={err} /> : null}
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />

      <div className="card">
        <div className="cardTitle">Schedule filters</div>
        <div className="row filterRow row--wrap">
          <LeaguePicker leagueId={leagueId} setLeagueId={setLeagueId} me={me} label="League" />
          <label>
            View
            <select value={mode} onChange={(e) => setMode(e.target.value)}>
              <option value="calendar">Calendar</option>
              <option value="list">List</option>
            </select>
          </label>
          <label>
            Division
            <select value={division} onChange={(e) => setDivision(e.target.value)}>
              <option value="">All</option>
              {divisions.map((d) => (
                <option key={d.code} value={d.code}>
                  {d.name} ({d.code})
                </option>
              ))}
            </select>
          </label>
          <label>
            Team
            <select value={teamId} onChange={(e) => setTeamId(e.target.value)}>
              <option value="">All</option>
              {teams.map((t) => (
                <option key={t.teamId} value={t.teamId}>
                  {t.name || t.teamId}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field
            <select value={fieldKey} onChange={(e) => setFieldKey(e.target.value)}>
              <option value="">All</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName || f.fieldName || f.fieldKey}
                </option>
              ))}
            </select>
          </label>
          <label>
            From
            <input value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            To
            <input value={dateTo} onChange={(e) => setDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <button className="btn" onClick={refreshData} title="Refresh the schedule list with the current filters.">
            Refresh
          </button>
          <button
            className="btn btn--ghost"
            onClick={exportFiltered}
            title="Export the filtered schedule to CSV."
          >
            Export
          </button>
        </div>
        <div className="muted mt-2">
          Showing {filteredSlots.length} item{filteredSlots.length === 1 ? "" : "s"} for {leagueId || "(no league)"}.
        </div>
      </div>

      {mode === "calendar" ? (
        <div className="card">
          <div className="cardTitle">Calendar</div>
          {timeline.length === 0 ? <div className="muted">No scheduled games in this range.</div> : null}
          <div className="stack">
            {timeline.map((it) => (
              <div key={it.id} className={statusClassForSlot(it.raw)}>
                <div className="row row--between">
                  <div>
                    <div className="font-bold">
                      {it.date} {it.start ? `${it.start}${it.end ? `-${it.end}` : ""}` : ""} - {it.title}
                    </div>
                    {it.subtitle ? <div className="muted">{it.subtitle}</div> : null}
                  </div>
                  {it.raw?.status ? (
                    <span className={`statusBadge status-${(it.raw.status || "").toLowerCase()}`}>
                      {it.raw.status}
                    </span>
                  ) : null}
                </div>
              </div>
            ))}
          </div>
        </div>
      ) : (
        <div className="card">
          <div className="cardTitle">Schedule list</div>
          {filteredSlots.length === 0 ? (
            <div className="muted">No scheduled games match these filters.</div>
          ) : (
            <div className="tableWrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Time</th>
                    <th>Field</th>
                    <th>Division</th>
                    <th>Teams</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredSlots.map((s) => (
                    <tr key={s.slotId}>
                      <td>{s.gameDate}</td>
                      <td>
                        {s.startTime}-{s.endTime}
                      </td>
                      <td>{s.displayName || s.fieldName || s.fieldKey}</td>
                      <td>{s.division}</td>
                      <td>{formatMatchup(s) || s.offeringTeamId}</td>
                      <td>{s.status}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
