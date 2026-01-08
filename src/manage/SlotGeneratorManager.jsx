import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import Toast from "../components/Toast";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";

function csvEscape(value) {
  const raw = String(value ?? "");
  if (!/[",\n]/.test(raw)) return raw;
  return `"${raw.replace(/"/g, '""')}"`;
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

function buildAvailabilityTemplateCsv(divisions, fields) {
  const header = ["division", "gameDate", "startTime", "endTime", "fieldKey", "notes", "parkName", "fieldName", "displayName"];
  const divs = (divisions || [])
    .filter((d) => d && d.isActive !== false)
    .map((d) => (typeof d === "string" ? d : d.code || d.division || ""))
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b));
  const rows = [];
  for (const div of divs) {
    for (const f of fields || []) {
      if (!f?.fieldKey) continue;
      rows.push([
        div,
        "",
        "",
        "",
        f.fieldKey,
        "",
        f.parkName || "",
        f.fieldName || "",
        f.displayName || "",
      ]);
    }
  }
  return [header, ...rows].map((row) => row.map(csvEscape).join(",")).join("\n");
}

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
  const [availFile, setAvailFile] = useState(null);
  const [availBusy, setAvailBusy] = useState(false);
  const [availErr, setAvailErr] = useState("");
  const [availOk, setAvailOk] = useState("");
  const [availErrors, setAvailErrors] = useState([]);
  const [availWarnings, setAvailWarnings] = useState([]);
  const [availDivision, setAvailDivision] = useState("");
  const [availFieldKey, setAvailFieldKey] = useState("");
  const [availDateFrom, setAvailDateFrom] = useState("");
  const [availDateTo, setAvailDateTo] = useState("");
  const [availSlots, setAvailSlots] = useState([]);
  const [availListLoading, setAvailListLoading] = useState(false);
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
    if (!availDateFrom) setAvailDateFrom(seasonRange.from);
    if (!availDateTo) setAvailDateTo(seasonRange.to);
  }, [seasonRange, availDateFrom, availDateTo]);

  useEffect(() => {
    if (!slotGenDivision && divisions.length) {
      setSlotGenDivision(divisions[0].code || divisions[0].division || "");
    }
  }, [divisions, slotGenDivision]);

  useEffect(() => {
    if (!availDivision && divisions.length) {
      setAvailDivision(divisions[0].code || divisions[0].division || "");
    }
  }, [divisions, availDivision]);

  useEffect(() => {
    if (!slotGenFieldKey && fields.length) {
      setSlotGenFieldKey(fields[0].fieldKey || "");
    }
  }, [fields, slotGenFieldKey]);

  useEffect(() => {
    if (!availFieldKey && fields.length) {
      setAvailFieldKey("");
    }
  }, [fields, availFieldKey]);

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

  async function importAvailabilityCsv() {
    setAvailErr("");
    setAvailOk("");
    setAvailErrors([]);
    setAvailWarnings([]);
    if (!availFile) return setAvailErr("Choose a CSV file to upload.");

    setAvailBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", availFile);
      const res = await apiFetch("/api/import/availability-slots", { method: "POST", body: fd });
      setAvailOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      if (Array.isArray(res?.errors) && res.errors.length) setAvailErrors(res.errors);
      if (Array.isArray(res?.warnings) && res.warnings.length) setAvailWarnings(res.warnings);
    } catch (e) {
      setAvailErr(e?.message || "Import failed");
    } finally {
      setAvailBusy(false);
    }
  }

  function downloadAvailabilityTemplate() {
    const csv = buildAvailabilityTemplateCsv(divisions, fields);
    const safeLeague = (leagueId || "league").replace(/[^a-z0-9_-]+/gi, "_");
    downloadCsv(csv, `availability_template_${safeLeague}.csv`);
  }

  async function loadAvailabilitySlots() {
    setAvailListLoading(true);
    setAvailErr("");
    try {
      const qs = new URLSearchParams();
      if (availDivision) qs.set("division", availDivision);
      if (availDateFrom) qs.set("dateFrom", availDateFrom);
      if (availDateTo) qs.set("dateTo", availDateTo);
      const data = await apiFetch(`/api/slots?${qs.toString()}`);
      const list = Array.isArray(data) ? data : [];
      const filtered = list.filter((s) => s.isAvailability && (!availFieldKey || s.fieldKey === availFieldKey));
      setAvailSlots(filtered);
      if (filtered.length === 0) setAvailOk("No availability slots found for this filter.");
    } catch (e) {
      setAvailErr(e?.message || "Failed to load availability slots.");
      setAvailSlots([]);
    } finally {
      setAvailListLoading(false);
    }
  }

  async function deleteAvailabilitySlots() {
    if (!availDivision) return;
    const confirmText = window.prompt(
      "Type DELETE AVAILABILITY to remove availability slots for the selected filters."
    );
    if (confirmText !== "DELETE AVAILABILITY") return;

    setAvailListLoading(true);
    setAvailErr("");
    try {
      const res = await apiFetch("/api/availability-slots/clear", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: availDivision,
          dateFrom: availDateFrom,
          dateTo: availDateTo,
          fieldKey: availFieldKey || undefined,
        }),
      });
      setAvailOk(`Deleted ${res?.deleted ?? 0} availability slots.`);
      await loadAvailabilitySlots();
    } catch (e) {
      setAvailErr(e?.message || "Delete failed.");
    } finally {
      setAvailListLoading(false);
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
          <div className="h2">Availability CSV import</div>
          <div className="subtle">
            Upload bulk availability slots. Required columns: <code>division</code>, <code>gameDate</code>, <code>startTime</code>, <code>endTime</code>, <code>fieldKey</code>.
          </div>
        </div>
        <div className="card__body">
          {availErr ? <div className="callout callout--error">{availErr}</div> : null}
          {availOk ? <div className="callout callout--ok">{availOk}</div> : null}
          <div className="row items-end gap-3">
            <label className="flex-1">
              CSV file
              <input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => setAvailFile(e.target.files?.[0] || null)}
                disabled={availBusy}
              />
            </label>
            <button className="btn" onClick={importAvailabilityCsv} disabled={availBusy || !availFile}>
              {availBusy ? "Importing..." : "Upload & Import"}
            </button>
            <button className="btn btn--ghost" onClick={downloadAvailabilityTemplate}>
              Download CSV template
            </button>
          </div>
          {availErrors.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Rejected rows ({availErrors.length})</div>
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Error</th>
                  </tr>
                </thead>
                <tbody>
                  {availErrors.slice(0, 50).map((x, idx) => (
                    <tr key={idx}>
                      <td>{x.row}</td>
                      <td>{x.error}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {availErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
          {availWarnings.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Warnings ({availWarnings.length})</div>
              <div className="subtle mb-2">
                These rows were skipped because they overlapped existing slots or duplicates in the CSV.
              </div>
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Warning</th>
                  </tr>
                </thead>
                <tbody>
                  {availWarnings.slice(0, 50).map((x, idx) => (
                    <tr key={idx}>
                      <td>{x.row}</td>
                      <td>{x.warning}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {availWarnings.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Availability slots</div>
          <div className="subtle">Review and bulk delete availability slots.</div>
        </div>
        <div className="card__body grid2">
          <label>
            Division
            <select value={availDivision} onChange={(e) => setAvailDivision(e.target.value)}>
              <option value="">All divisions</option>
              {divisions.map((d) => (
                <option key={d.code || d.division} value={d.code || d.division}>
                  {d.name ? `${d.name} (${d.code || d.division})` : d.code || d.division}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field (optional)
            <select value={availFieldKey} onChange={(e) => setAvailFieldKey(e.target.value)}>
              <option value="">All fields</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName}
                </option>
              ))}
            </select>
          </label>
          <label>
            Date from
            <input value={availDateFrom} onChange={(e) => setAvailDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Date to
            <input value={availDateTo} onChange={(e) => setAvailDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={loadAvailabilitySlots} disabled={availListLoading}>
            {availListLoading ? "Loading..." : "Load availability slots"}
          </button>
          <button className="btn btn--danger" onClick={deleteAvailabilitySlots} disabled={availListLoading || !availDivision}>
            Delete filtered availability
          </button>
        </div>
        {availSlots.length ? (
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
                {availSlots.slice(0, 200).map((s) => (
                  <tr key={s.slotId}>
                    <td>{s.gameDate}</td>
                    <td>{s.startTime}-{s.endTime}</td>
                    <td>{s.displayName || s.fieldKey}</td>
                    <td>{s.division}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {availSlots.length > 200 ? <div className="subtle">Showing first 200.</div> : null}
          </div>
        ) : null}
      </div>

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
