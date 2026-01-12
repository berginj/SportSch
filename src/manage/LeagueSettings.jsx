import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import { buildAvailabilityInsights } from "../lib/availabilityInsights";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";
import Toast from "../components/Toast";

const EMPTY_SEASON = {
  springStart: "",
  springEnd: "",
  fallStart: "",
  fallEnd: "",
  gameLengthMinutes: "",
  blackouts: [],
};

function BlackoutEditor({ blackouts, setBlackouts }) {
  const items = Array.isArray(blackouts) ? blackouts : [];

  const updateItem = (index, key, value) => {
    setBlackouts((prev) =>
      prev.map((item, i) => (i === index ? { ...item, [key]: value } : item))
    );
  };

  const removeItem = (index) => {
    setBlackouts((prev) => prev.filter((_, i) => i !== index));
  };

  const addItem = () => {
    setBlackouts((prev) => [...prev, { startDate: "", endDate: "", label: "" }]);
  };

  return (
    <div className="stack gap-2">
      {items.length === 0 ? <div className="muted">No blackouts yet.</div> : null}
      {items.map((b, i) => (
        <div key={`${i}-${b.startDate}-${b.endDate}`} className="row row--wrap gap-2 items-center">
          <input
            value={b.startDate || ""}
            onChange={(e) => updateItem(i, "startDate", e.target.value)}
            placeholder="YYYY-MM-DD"
          />
          <span className="muted">to</span>
          <input
            value={b.endDate || ""}
            onChange={(e) => updateItem(i, "endDate", e.target.value)}
            placeholder="YYYY-MM-DD"
          />
          <input
            value={b.label || ""}
            onChange={(e) => updateItem(i, "label", e.target.value)}
            placeholder="Label"
            className="flex-1 min-w-[180px]"
          />
          <button className="btn btn--ghost" type="button" onClick={() => removeItem(i)}>
            Remove
          </button>
        </div>
      ))}
      <button className="btn" type="button" onClick={addItem}>
        Add blackout
      </button>
    </div>
  );
}

function normalizeSeason(season) {
  if (!season) return { ...EMPTY_SEASON };
  return {
    springStart: season.springStart || "",
    springEnd: season.springEnd || "",
    fallStart: season.fallStart || "",
    fallEnd: season.fallEnd || "",
    gameLengthMinutes: season.gameLengthMinutes ? String(season.gameLengthMinutes) : "",
    blackouts: Array.isArray(season.blackouts) ? season.blackouts : [],
  };
}

function buildSeasonPayload(draft) {
  return {
    springStart: (draft.springStart || "").trim(),
    springEnd: (draft.springEnd || "").trim(),
    fallStart: (draft.fallStart || "").trim(),
    fallEnd: (draft.fallEnd || "").trim(),
    gameLengthMinutes: Number(draft.gameLengthMinutes) || 0,
    blackouts: Array.isArray(draft.blackouts) ? draft.blackouts : [],
  };
}

export default function LeagueSettings({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [fields, setFields] = useState([]);
  const [division, setDivision] = useState("");
  const [fieldKey, setFieldKey] = useState("");
  const [leagueDraft, setLeagueDraft] = useState({ ...EMPTY_SEASON });
  const [divisionDraft, setDivisionDraft] = useState({ ...EMPTY_SEASON });
  const [fieldBlackouts, setFieldBlackouts] = useState([]);
  const [loading, setLoading] = useState(false);
  const [toast, setToast] = useState(null);
  const [err, setErr] = useState("");
  const [backupInfo, setBackupInfo] = useState(null);
  const [backupLoading, setBackupLoading] = useState(false);
  const [availabilityInsights, setAvailabilityInsights] = useState(null);
  const [availabilitySlots, setAvailabilitySlots] = useState([]);
  const [availabilityLoading, setAvailabilityLoading] = useState(false);
  const [availabilityErr, setAvailabilityErr] = useState("");
  const [availabilityAllDivisions, setAvailabilityAllDivisions] = useState(true);
  const [availabilityDivision, setAvailabilityDivision] = useState("");
  const [availabilityDateFrom, setAvailabilityDateFrom] = useState("");
  const [availabilityDateTo, setAvailabilityDateTo] = useState("");
  const [seasonRange, setSeasonRange] = useState(getDefaultRangeFallback());

  async function loadSettings() {
    if (!leagueId) return;
    setLoading(true);
    setErr("");
    try {
      const [divs, league, flds, backup] = await Promise.all([
        apiFetch("/api/divisions"),
        apiFetch("/api/league"),
        apiFetch("/api/fields"),
        apiFetch("/api/league/backup"),
      ]);
      const list = Array.isArray(divs) ? divs : [];
      setDivisions(list);
      setFields(Array.isArray(flds) ? flds : []);
      setLeagueDraft(normalizeSeason(league?.season));
      setBackupInfo(backup?.backup || null);
      if (!division && list.length) {
        setDivision(list[0].code || list[0].division || "");
      }
      if (!fieldKey && Array.isArray(flds) && flds.length) {
        setFieldKey(flds[0].fieldKey || "");
      }
      const range = getSeasonRange(league?.season, new Date());
      setSeasonRange(range || getDefaultRangeFallback());
    } catch (e) {
      setErr(e?.message || "Failed to load league settings");
      setDivisions([]);
      setFields([]);
      setBackupInfo(null);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadSettings();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    if (!leagueId || !division) return;
    (async () => {
      try {
        const data = await apiFetch(`/api/divisions/${encodeURIComponent(division)}/season`);
        setDivisionDraft(normalizeSeason(data?.season));
      } catch (e) {
        setDivisionDraft({ ...EMPTY_SEASON });
        setToast({ tone: "error", message: e?.message || "Failed to load division season settings." });
      }
    })();
  }, [leagueId, division]);

  useEffect(() => {
    if (!fieldKey) return;
    const current = fields.find((f) => f.fieldKey === fieldKey);
    setFieldBlackouts(Array.isArray(current?.blackouts) ? current.blackouts : []);
  }, [fieldKey, fields]);

  useEffect(() => {
    if (!availabilityDivision && division) setAvailabilityDivision(division);
  }, [availabilityDivision, division]);

  useEffect(() => {
    if (!availabilityDateFrom && seasonRange?.from) setAvailabilityDateFrom(seasonRange.from);
    if (!availabilityDateTo && seasonRange?.to) setAvailabilityDateTo(seasonRange.to);
  }, [availabilityDateFrom, availabilityDateTo, seasonRange]);

  const divisionOptions = useMemo(
    () => (divisions || []).map((d) => ({
      code: d.code || d.division || "",
      name: d.name || d.code || d.division || "",
    })),
    [divisions]
  );

  async function saveLeagueSeason() {
    if (!leagueId) return;
    const payload = buildSeasonPayload(leagueDraft);
    const blackoutFields = (payload.blackouts || []).flatMap((b, idx) => ([
      { label: `Blackout ${idx + 1} start`, value: b.startDate, required: !!b.endDate },
      { label: `Blackout ${idx + 1} end`, value: b.endDate, required: !!b.startDate },
    ]));
    const dateError = validateIsoDates([
      { label: "Spring start", value: payload.springStart, required: false },
      { label: "Spring end", value: payload.springEnd, required: false },
      { label: "Fall start", value: payload.fallStart, required: false },
      { label: "Fall end", value: payload.fallEnd, required: false },
      ...blackoutFields,
    ]);
    if (dateError) return setToast({ tone: "error", message: dateError });
    if (!payload.gameLengthMinutes) {
      return setToast({ tone: "error", message: "League game length must be set." });
    }
    setLoading(true);
    try {
      await apiFetch("/api/league/season", {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ season: payload }),
      });
      setToast({ tone: "success", message: "League season settings saved." });
      await loadSettings();
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to save league season settings." });
    } finally {
      setLoading(false);
    }
  }

  async function saveDivisionSeason() {
    if (!leagueId || !division) return;
    const payload = buildSeasonPayload(divisionDraft);
    const blackoutFields = (payload.blackouts || []).flatMap((b, idx) => ([
      { label: `Blackout ${idx + 1} start`, value: b.startDate, required: !!b.endDate },
      { label: `Blackout ${idx + 1} end`, value: b.endDate, required: !!b.startDate },
    ]));
    const dateError = validateIsoDates([
      { label: "Spring start", value: payload.springStart, required: false },
      { label: "Spring end", value: payload.springEnd, required: false },
      { label: "Fall start", value: payload.fallStart, required: false },
      { label: "Fall end", value: payload.fallEnd, required: false },
      ...blackoutFields,
    ]);
    if (dateError) return setToast({ tone: "error", message: dateError });
    setLoading(true);
    try {
      await apiFetch(`/api/divisions/${encodeURIComponent(division)}/season`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ season: payload }),
      });
      setToast({ tone: "success", message: "Division season overrides saved." });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to save division season overrides." });
    } finally {
      setLoading(false);
    }
  }

  async function saveFieldBlackouts() {
    if (!leagueId || !fieldKey) return;
    const [parkCode, fieldCode] = fieldKey.split("/");
    if (!parkCode || !fieldCode) return;
    setLoading(true);
    try {
      const updated = await apiFetch(`/api/fields/${encodeURIComponent(parkCode)}/${encodeURIComponent(fieldCode)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ blackouts: fieldBlackouts }),
      });
      setFields((prev) => prev.map((f) => (f.fieldKey === fieldKey ? { ...f, blackouts: updated.blackouts || [] } : f)));
      setToast({ tone: "success", message: "Field blackouts saved." });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to save field blackouts." });
    } finally {
      setLoading(false);
    }
  }

  async function saveBackup() {
    if (!leagueId) return;
    setBackupLoading(true);
    try {
      const data = await apiFetch("/api/league/backup", { method: "POST" });
      setBackupInfo(data?.backup || null);
      setToast({ tone: "success", message: "Backup saved." });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to save backup." });
    } finally {
      setBackupLoading(false);
    }
  }

  async function restoreBackup() {
    if (!leagueId) return;
    if (!backupInfo) return setToast({ tone: "error", message: "No backup found to restore." });
    const ok = window.confirm("Restore fields, divisions, and league season settings from the saved backup? This will overwrite current data.");
    if (!ok) return;
    setBackupLoading(true);
    try {
      const result = await apiFetch("/api/league/backup/restore", { method: "POST" });
      await loadSettings();
      setToast({
        tone: "success",
        message: `Backup restored. Fields: ${result?.fieldsRestored ?? 0}, divisions: ${result?.divisionsRestored ?? 0}.`
      });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to restore backup." });
    } finally {
      setBackupLoading(false);
    }
  }

  async function downloadBackup() {
    if (!leagueId) return;
    if (!backupInfo) return setToast({ tone: "error", message: "No backup found to download." });
    setBackupLoading(true);
    try {
      const data = await apiFetch("/api/league/backup?includeSnapshot=1");
      if (!data?.snapshot) {
        setToast({ tone: "error", message: "Backup snapshot is missing." });
        return;
      }
      const safeLeague = (leagueId || "league").replace(/[^a-z0-9_-]+/gi, "_");
      const stamp = (backupInfo?.savedUtc || new Date().toISOString()).replace(/[:.]/g, "-");
      const filename = `${safeLeague}_backup_${stamp}.json`;
      const blob = new Blob([JSON.stringify(data.snapshot, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      setToast({ tone: "success", message: "Backup downloaded." });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to download backup." });
    } finally {
      setBackupLoading(false);
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
    } catch (e) {
      setAvailabilityErr(e?.message || "Failed to load availability slots.");
      setAvailabilitySlots([]);
      setAvailabilityInsights(null);
    } finally {
      setAvailabilityLoading(false);
    }
  }

  return (
    <div className="stack gap-4">
      {toast ? <Toast {...toast} onClose={() => setToast(null)} /> : null}
      {err ? <div className="callout callout--error">{err}</div> : null}

      <div className="card">
        <div className="card__header">
          <div className="h3">League backup</div>
          <div className="subtle">Save a snapshot of fields, divisions, and season dates for quick recovery.</div>
        </div>
        <div className="card__body stack gap-3">
          <div className="callout callout--warning">
            Backups store a full snapshot in a single row. If your league has a very large number of fields or divisions, the snapshot may exceed storage limits.
          </div>
          {backupInfo ? (
            <>
              <div className="row row--wrap gap-3">
                <div className="stack gap-1">
                  <span className="muted">Last saved</span>
                  <div>{backupInfo.savedUtc || "Unknown time"}</div>
                </div>
                <div className="stack gap-1">
                  <span className="muted">Saved by</span>
                  <div>{backupInfo.savedBy || "Unknown user"}</div>
                </div>
                <div className="stack gap-1">
                  <span className="muted">Fields</span>
                  <div>{backupInfo.fieldsCount ?? 0}</div>
                </div>
                <div className="stack gap-1">
                  <span className="muted">Divisions</span>
                  <div>{backupInfo.divisionsCount ?? 0}</div>
                </div>
              </div>
              <div className="row row--wrap gap-3">
                <div className="stack gap-1">
                  <span className="muted">Spring</span>
                  <div>{backupInfo.season?.springStart || "TBD"} to {backupInfo.season?.springEnd || "TBD"}</div>
                </div>
                <div className="stack gap-1">
                  <span className="muted">Fall</span>
                  <div>{backupInfo.season?.fallStart || "TBD"} to {backupInfo.season?.fallEnd || "TBD"}</div>
                </div>
              </div>
            </>
          ) : (
            <div className="muted">No backup saved yet.</div>
          )}
          <div className="row row--wrap gap-2">
            <button className="btn btn--primary" type="button" onClick={saveBackup} disabled={backupLoading}>
              {backupLoading ? "Saving..." : "Save backup"}
            </button>
            <button className="btn btn--ghost" type="button" onClick={downloadBackup} disabled={backupLoading || !backupInfo}>
              Download snapshot
            </button>
            <button className="btn btn--ghost" type="button" onClick={restoreBackup} disabled={backupLoading || !backupInfo}>
              Restore backup
            </button>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h3">League season settings</div>
          <div className="subtle">Set dates, game length, and league-wide blackout windows.</div>
        </div>
        <div className="card__body stack gap-3">
          <div className="row row--wrap gap-3">
            <label className="stack gap-1">
              <span className="muted">Game length (minutes)</span>
              <input
                type="number"
                min="0"
                value={leagueDraft.gameLengthMinutes}
                onChange={(e) => setLeagueDraft((p) => ({ ...p, gameLengthMinutes: e.target.value }))}
              />
            </label>
            <label className="stack gap-1">
              <span className="muted">Spring start</span>
              <input
                value={leagueDraft.springStart}
                onChange={(e) => setLeagueDraft((p) => ({ ...p, springStart: e.target.value }))}
                placeholder="YYYY-MM-DD"
              />
            </label>
            <label className="stack gap-1">
              <span className="muted">Spring end</span>
              <input
                value={leagueDraft.springEnd}
                onChange={(e) => setLeagueDraft((p) => ({ ...p, springEnd: e.target.value }))}
                placeholder="YYYY-MM-DD"
              />
            </label>
            <label className="stack gap-1">
              <span className="muted">Fall start</span>
              <input
                value={leagueDraft.fallStart}
                onChange={(e) => setLeagueDraft((p) => ({ ...p, fallStart: e.target.value }))}
                placeholder="YYYY-MM-DD"
              />
            </label>
            <label className="stack gap-1">
              <span className="muted">Fall end</span>
              <input
                value={leagueDraft.fallEnd}
                onChange={(e) => setLeagueDraft((p) => ({ ...p, fallEnd: e.target.value }))}
                placeholder="YYYY-MM-DD"
              />
            </label>
          </div>
          <BlackoutEditor blackouts={leagueDraft.blackouts} setBlackouts={(next) => setLeagueDraft((p) => ({ ...p, blackouts: next }))} />
          <div>
            <button className="btn btn--primary" type="button" onClick={saveLeagueSeason} disabled={loading}>
              Save league season
            </button>
          </div>
        </div>
      </div>

      <details className="card">
        <summary className="card__header cursor-pointer">
          <div className="h3">Division season overrides</div>
          <div className="subtle">Override season dates, game length, and blackout windows for a division.</div>
        </summary>
        <div className="card__body stack gap-3">
          <div className="row row--wrap gap-3 items-end">
            <label className="stack gap-1">
              <span className="muted">Division</span>
              <select value={division} onChange={(e) => setDivision(e.target.value)}>
                {divisionOptions.map((d) => (
                  <option key={d.code} value={d.code}>
                    {d.name}
                  </option>
                ))}
              </select>
            </label>
            <button
              className="btn btn--ghost"
              type="button"
              onClick={() => setDivisionDraft({ ...EMPTY_SEASON })}
            >
              Clear overrides
            </button>
          </div>
          <div className="row row--wrap gap-3">
            <label className="stack gap-1">
              <span className="muted">Game length (minutes)</span>
              <input
                type="number"
                min="0"
                value={divisionDraft.gameLengthMinutes}
                onChange={(e) => setDivisionDraft((p) => ({ ...p, gameLengthMinutes: e.target.value }))}
              />
            </label>
            <label className="stack gap-1">
              <span className="muted">Spring start</span>
              <input
                value={divisionDraft.springStart}
                onChange={(e) => setDivisionDraft((p) => ({ ...p, springStart: e.target.value }))}
                placeholder="YYYY-MM-DD"
              />
            </label>
            <label className="stack gap-1">
              <span className="muted">Spring end</span>
              <input
                value={divisionDraft.springEnd}
                onChange={(e) => setDivisionDraft((p) => ({ ...p, springEnd: e.target.value }))}
                placeholder="YYYY-MM-DD"
              />
            </label>
            <label className="stack gap-1">
              <span className="muted">Fall start</span>
              <input
                value={divisionDraft.fallStart}
                onChange={(e) => setDivisionDraft((p) => ({ ...p, fallStart: e.target.value }))}
                placeholder="YYYY-MM-DD"
              />
            </label>
            <label className="stack gap-1">
              <span className="muted">Fall end</span>
              <input
                value={divisionDraft.fallEnd}
                onChange={(e) => setDivisionDraft((p) => ({ ...p, fallEnd: e.target.value }))}
                placeholder="YYYY-MM-DD"
              />
            </label>
          </div>
          <BlackoutEditor blackouts={divisionDraft.blackouts} setBlackouts={(next) => setDivisionDraft((p) => ({ ...p, blackouts: next }))} />
          <div>
            <button className="btn btn--primary" type="button" onClick={saveDivisionSeason} disabled={loading}>
              Save division overrides
            </button>
          </div>
        </div>
      </details>

      <details className="card">
        <summary className="card__header cursor-pointer">
          <div className="h3">Field blackouts</div>
          <div className="subtle">Block specific fields during school breaks, tournaments, or facility conflicts.</div>
        </summary>
        <div className="card__body stack gap-3">
          <label className="stack gap-1 max-w-md">
            <span className="muted">Field</span>
            <select value={fieldKey} onChange={(e) => setFieldKey(e.target.value)}>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName || f.fieldKey}
                </option>
              ))}
            </select>
          </label>
          <BlackoutEditor blackouts={fieldBlackouts} setBlackouts={setFieldBlackouts} />
          <div>
            <button className="btn btn--primary" type="button" onClick={saveFieldBlackouts} disabled={loading}>
              Save field blackouts
            </button>
          </div>
        </div>
      </details>

      <details className="card">
        <summary className="card__header cursor-pointer">
          <div className="h3">Availability insights</div>
          <div className="subtle">Analyze open availability slots to suggest the best game nights.</div>
        </summary>
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
                {divisionOptions.map((d) => (
                  <option key={d.code} value={d.code}>
                    {d.name}
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
                  {availabilityInsights.suggested.length ? availabilityInsights.suggested.join(", ") : "-"}
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
      </details>
    </div>
  );
}
