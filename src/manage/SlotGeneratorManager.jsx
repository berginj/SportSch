import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import Toast from "../components/Toast";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";

const DEFAULT_DAYS = {
  Mon: false,
  Tue: false,
  Wed: false,
  Thu: false,
  Fri: false,
  Sat: false,
  Sun: false,
};

export default function SlotGeneratorManager({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [fields, setFields] = useState([]);
  const [leagueSeason, setLeagueSeason] = useState(null);
  const [slotGenDivision, setSlotGenDivision] = useState("");
  const [slotGenFieldKey, setSlotGenFieldKey] = useState("");
  const [slotGenStartTime, setSlotGenStartTime] = useState("");
  const [slotGenEndTime, setSlotGenEndTime] = useState("");
  const [slotGenDays, setSlotGenDays] = useState(DEFAULT_DAYS);
  const [slotGenPreview, setSlotGenPreview] = useState(null);
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);

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
        if (!slotGenDivision && list.length) setSlotGenDivision(list[0].code || list[0].division || "");
        if (!slotGenFieldKey && Array.isArray(flds) && flds.length) setSlotGenFieldKey(flds[0].fieldKey || "");
      } catch (e) {
        setErr(e?.message || "Failed to load divisions/fields.");
        setDivisions([]);
        setFields([]);
        setLeagueSeason(null);
      }
    })();
  }, [leagueId]);

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
    if (!slotGenDivision && divisions.length) {
      setSlotGenDivision(divisions[0].code || divisions[0].division || "");
    }
  }, [divisions, slotGenDivision]);

  useEffect(() => {
    if (!slotGenFieldKey && fields.length) {
      setSlotGenFieldKey(fields[0].fieldKey || "");
    }
  }, [fields, slotGenFieldKey]);

  function toggleDay(day) {
    setSlotGenDays((prev) => ({ ...prev, [day]: !prev[day] }));
  }

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
          <div className="h2">Field slot generator</div>
          <div className="subtle">
            Generate open availability slots for a division and field. Uses season game length (no buffers).
          </div>
        </div>
        {leagueSeason && !leagueSeason.gameLengthMinutes ? (
          <div className="card__body">
            <div className="callout callout--error">
              Season game length is not set. Update league or division season settings.
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
              <option value="">Select a field</option>
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
          <button className="btn" onClick={() => applySlotGeneration("regenerate")} disabled={loading || !slotGenDivision || !slotGenFieldKey}>
            Regenerate
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
    </div>
  );
}
