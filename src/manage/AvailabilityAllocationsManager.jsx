import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";
import Toast from "../components/Toast";

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

function buildAllocationTemplateCsv(divisions, fields) {
  const header = [
    "division",
    "fieldKey",
    "dateFrom",
    "dateTo",
    "daysOfWeek",
    "startTime",
    "endTime",
    "notes",
    "isActive",
    "parkName",
    "fieldName",
    "displayName",
  ];
  const divs = (divisions || [])
    .filter((d) => d && d.isActive !== false)
    .map((d) => (typeof d === "string" ? d : d.code || d.division || ""))
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b));
  const scopes = ["LEAGUE", ...divs];
  const rows = [];
  for (const scope of scopes) {
    for (const f of fields || []) {
      if (!f?.fieldKey) continue;
      rows.push([
        scope,
        f.fieldKey,
        "",
        "",
        "",
        "",
        "",
        "",
        "true",
        f.parkName || "",
        f.fieldName || "",
        f.displayName || "",
      ]);
    }
  }
  return [header, ...rows].map((row) => row.map(csvEscape).join(",")).join("\n");
}

export default function AvailabilityAllocationsManager({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [fields, setFields] = useState([]);
  const [leagueSeason, setLeagueSeason] = useState(null);
  const [toast, setToast] = useState(null);

  const [allocFile, setAllocFile] = useState(null);
  const [allocBusy, setAllocBusy] = useState(false);
  const [allocErr, setAllocErr] = useState("");
  const [allocOk, setAllocOk] = useState("");
  const [allocErrors, setAllocErrors] = useState([]);

  const [allocations, setAllocations] = useState([]);
  const [allocScope, setAllocScope] = useState("");
  const [allocFieldKey, setAllocFieldKey] = useState("");
  const [allocDateFrom, setAllocDateFrom] = useState("");
  const [allocDateTo, setAllocDateTo] = useState("");
  const [allocListLoading, setAllocListLoading] = useState(false);

  const [genDivision, setGenDivision] = useState("");
  const [genFieldKey, setGenFieldKey] = useState("");
  const [genDateFrom, setGenDateFrom] = useState("");
  const [genDateTo, setGenDateTo] = useState("");
  const [genPreview, setGenPreview] = useState(null);
  const [genLoading, setGenLoading] = useState(false);

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      setAllocErr("");
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
        if (!genDivision && list.length) setGenDivision(list[0].code || list[0].division || "");
      } catch (e) {
        setAllocErr(e?.message || "Failed to load divisions/fields.");
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
    if (!allocDateFrom) setAllocDateFrom(seasonRange.from);
    if (!allocDateTo) setAllocDateTo(seasonRange.to);
  }, [seasonRange, allocDateFrom, allocDateTo]);

  useEffect(() => {
    if (!genDateFrom) setGenDateFrom(seasonRange.from);
    if (!genDateTo) setGenDateTo(seasonRange.to);
  }, [seasonRange, genDateFrom, genDateTo]);

  useEffect(() => {
    if (!genDivision && divisions.length) {
      setGenDivision(divisions[0].code || divisions[0].division || "");
    }
  }, [divisions, genDivision]);

  function downloadTemplate() {
    const csv = buildAllocationTemplateCsv(divisions, fields);
    const safeLeague = (leagueId || "league").replace(/[^a-z0-9_-]+/gi, "_");
    downloadCsv(csv, `availability_allocations_${safeLeague}.csv`);
  }

  async function importAllocationsCsv() {
    setAllocErr("");
    setAllocOk("");
    setAllocErrors([]);
    if (!allocFile) return setAllocErr("Choose a CSV file to upload.");

    setAllocBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", allocFile);
      const res = await apiFetch("/api/import/availability-allocations", { method: "POST", body: fd });
      setAllocOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      setToast({ tone: "success", message: "Availability allocations imported." });
      if (Array.isArray(res?.errors) && res.errors.length) setAllocErrors(res.errors);
    } catch (e) {
      setAllocErr(e?.message || "Import failed");
    } finally {
      setAllocBusy(false);
    }
  }

  async function loadAllocations() {
    setAllocErr("");
    setAllocListLoading(true);
    try {
      const qs = new URLSearchParams();
      if (allocScope) qs.set("division", allocScope);
      if (allocFieldKey) qs.set("fieldKey", allocFieldKey);
      const data = await apiFetch(`/api/availability/allocations?${qs.toString()}`);
      setAllocations(Array.isArray(data) ? data : []);
      if (!data || data.length === 0) setAllocOk("No allocations found for this filter.");
    } catch (e) {
      setAllocErr(e?.message || "Failed to load allocations.");
      setAllocations([]);
    } finally {
      setAllocListLoading(false);
    }
  }

  async function clearAllocations() {
    if (!allocScope) return;
    const confirmText = window.prompt(
      "Type DELETE ALLOCATIONS to remove allocations for the selected filter."
    );
    if (confirmText !== "DELETE ALLOCATIONS") return;

    setAllocListLoading(true);
    setAllocErr("");
    try {
      const res = await apiFetch("/api/availability/allocations/clear", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          scope: allocScope,
          dateFrom: allocDateFrom,
          dateTo: allocDateTo,
          fieldKey: allocFieldKey || undefined,
        }),
      });
      setAllocOk(`Deleted ${res?.deleted ?? 0} allocations.`);
      setToast({ tone: "success", message: `Deleted ${res?.deleted ?? 0} allocations.` });
      await loadAllocations();
    } catch (e) {
      setAllocErr(e?.message || "Delete failed.");
    } finally {
      setAllocListLoading(false);
    }
  }

  async function previewAllocations() {
    if (!genDivision) return;
    setGenLoading(true);
    setAllocErr("");
    try {
      const data = await apiFetch("/api/availability/allocations/slots/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: genDivision,
          dateFrom: genDateFrom,
          dateTo: genDateTo,
          fieldKey: genFieldKey || undefined,
        }),
      });
      setGenPreview(data || null);
    } catch (e) {
      setAllocErr(e?.message || "Failed to preview allocation slots.");
      setGenPreview(null);
    } finally {
      setGenLoading(false);
    }
  }

  async function applyAllocations() {
    if (!genDivision) return;
    setGenLoading(true);
    setAllocErr("");
    try {
      const data = await apiFetch("/api/availability/allocations/slots/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: genDivision,
          dateFrom: genDateFrom,
          dateTo: genDateTo,
          fieldKey: genFieldKey || undefined,
        }),
      });
      setGenPreview(null);
      setToast({ tone: "success", message: `Created ${data?.created?.length ?? 0} availability slots.` });
    } catch (e) {
      setAllocErr(e?.message || "Failed to apply allocation slots.");
    } finally {
      setGenLoading(false);
    }
  }

  const scopes = useMemo(() => {
    const divs = (divisions || [])
      .filter((d) => d && d.isActive !== false)
      .map((d) => (typeof d === "string" ? d : d.code || d.division || ""))
      .filter(Boolean)
      .sort((a, b) => a.localeCompare(b));
    return ["LEAGUE", ...divs];
  }, [divisions]);

  const fieldLabelMap = useMemo(() => {
    const map = new Map();
    for (const f of fields || []) {
      if (!f?.fieldKey) continue;
      map.set(f.fieldKey, f.displayName || f.fieldKey);
    }
    return map;
  }, [fields]);

  return (
    <div className="stack">
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />
      {allocErr ? <div className="callout callout--error">{allocErr}</div> : null}
      {allocOk ? <div className="callout callout--ok">{allocOk}</div> : null}

      <div className="card">
        <div className="card__header">
          <div className="h2">Allocation CSV import</div>
          <div className="subtle">
            Upload per-division or league-wide field allocations (use <code>division</code> = <code>LEAGUE</code> or leave blank).
          </div>
        </div>
        <div className="card__body">
          <div className="subtle mb-2">
            Required columns: <code>fieldKey</code>, <code>dateFrom</code>, <code>dateTo</code>, <code>startTime</code>, <code>endTime</code>.
            Optional: <code>division</code>, <code>daysOfWeek</code>, <code>notes</code>, <code>isActive</code>.
          </div>
          <div className="row items-end gap-3">
            <label className="flex-1">
              CSV file
              <input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => setAllocFile(e.target.files?.[0] || null)}
                disabled={allocBusy}
              />
            </label>
            <button className="btn" onClick={importAllocationsCsv} disabled={allocBusy || !allocFile}>
              {allocBusy ? "Importing..." : "Upload & Import"}
            </button>
            <button className="btn btn--ghost" onClick={downloadTemplate} disabled={!leagueId}>
              Download CSV template
            </button>
          </div>
          {allocErrors.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Rejected rows ({allocErrors.length})</div>
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Error</th>
                  </tr>
                </thead>
                <tbody>
                  {allocErrors.slice(0, 50).map((x, idx) => (
                    <tr key={idx}>
                      <td>{x.row}</td>
                      <td>{x.error}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {allocErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Allocation list</div>
          <div className="subtle">Review and bulk delete allocation rows.</div>
        </div>
        <div className="card__body grid2">
          <label>
            Scope
            <select value={allocScope} onChange={(e) => setAllocScope(e.target.value)}>
              <option value="">All scopes</option>
              {scopes.map((s) => (
                <option key={s} value={s}>
                  {s === "LEAGUE" ? "League-wide" : s}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field (optional)
            <select value={allocFieldKey} onChange={(e) => setAllocFieldKey(e.target.value)}>
              <option value="">All fields</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName || f.fieldKey}
                </option>
              ))}
            </select>
          </label>
          <label>
            Date from
            <input value={allocDateFrom} onChange={(e) => setAllocDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Date to
            <input value={allocDateTo} onChange={(e) => setAllocDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={loadAllocations} disabled={allocListLoading}>
            {allocListLoading ? "Loading..." : "Load allocations"}
          </button>
          <button className="btn btn--danger" onClick={clearAllocations} disabled={allocListLoading || !allocScope}>
            Delete filtered allocations
          </button>
        </div>
        {allocations.length ? (
          <div className="card__body tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Scope</th>
                  <th>Field</th>
                  <th>Dates</th>
                  <th>Days</th>
                  <th>Time</th>
                  <th>Active</th>
                  <th>Notes</th>
                </tr>
              </thead>
              <tbody>
                {allocations.map((a) => (
                  <tr key={a.allocationId}>
                    <td>{a.scope === "LEAGUE" ? "League-wide" : a.scope}</td>
                    <td>{fieldLabelMap.get(a.fieldKey) || a.fieldKey}</td>
                    <td>{a.startsOn} - {a.endsOn}</td>
                    <td>{(a.daysOfWeek || []).join(", ") || "Any"}</td>
                    <td>{a.startTimeLocal} - {a.endTimeLocal}</td>
                    <td>{a.isActive ? "Yes" : "No"}</td>
                    <td>{a.notes || ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Generate availability slots from allocations</div>
          <div className="subtle">Allocations turn into availability slots for scheduling.</div>
        </div>
        <div className="card__body grid2">
          <label>
            Division
            <select value={genDivision} onChange={(e) => setGenDivision(e.target.value)}>
              {divisions.map((d) => (
                <option key={d.code || d.division} value={d.code || d.division}>
                  {d.name ? `${d.name} (${d.code || d.division})` : d.code || d.division}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field (optional)
            <select value={genFieldKey} onChange={(e) => setGenFieldKey(e.target.value)}>
              <option value="">All fields</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName || f.fieldKey}
                </option>
              ))}
            </select>
          </label>
          <label>
            Date from
            <input value={genDateFrom} onChange={(e) => setGenDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Date to
            <input value={genDateTo} onChange={(e) => setGenDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={previewAllocations} disabled={genLoading || !genDivision}>
            {genLoading ? "Loading..." : "Preview slots"}
          </button>
          <button className="btn btn--primary" onClick={applyAllocations} disabled={genLoading || !genDivision}>
            Generate slots
          </button>
        </div>
        {genPreview ? (
          <div className="card__body">
            <div className="row row--wrap gap-4">
              <div className="layoutStat">
                <div className="layoutStat__value">{genPreview.slots?.length || 0}</div>
                <div className="layoutStat__label">Slots</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{genPreview.conflicts?.length || 0}</div>
                <div className="layoutStat__label">Conflicts</div>
              </div>
            </div>
            {genPreview.conflicts?.length ? (
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
                    {genPreview.conflicts.slice(0, 20).map((c, idx) => (
                      <tr key={`${c.gameDate}-${c.startTime}-${idx}`}>
                        <td>{c.gameDate}</td>
                        <td>{c.startTime}-{c.endTime}</td>
                        <td>{fieldLabelMap.get(c.fieldKey) || c.fieldKey}</td>
                        <td>{c.division}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {genPreview.conflicts.length > 20 ? <div className="subtle">Showing first 20 conflicts.</div> : null}
              </div>
            ) : null}
          </div>
        ) : null}
      </div>
    </div>
  );
}
