import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import Toast from "../components/Toast";
import SeasonWizard from "./SeasonWizard";

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

export default function CommissionerHub({ leagueId, tableView = "A" }) {
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

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      setLoading(true);
      setErr("");
      try {
        const [divs, league, flds] = await Promise.all([
          apiFetch("/api/divisions"),
          apiFetch("/api/league"),
          apiFetch("/api/fields"),
        ]);
        setDivisions(Array.isArray(divs) ? divs : []);
        setFields(Array.isArray(flds) ? flds : []);
        setLeagueDraft(normalizeSeason(league?.season));
        if (!division && Array.isArray(divs) && divs.length) {
          setDivision(divs[0].code || divs[0].division || "");
        }
        if (!fieldKey && Array.isArray(flds) && flds.length) {
          setFieldKey(flds[0].fieldKey || "");
        }
      } catch (e) {
        setErr(e?.message || "Failed to load commissioner data");
        setDivisions([]);
        setFields([]);
      } finally {
        setLoading(false);
      }
    })();
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

  return (
    <div className="stack gap-4">
      {toast ? <Toast {...toast} onClose={() => setToast(null)} /> : null}
      {err ? <div className="callout callout--error">{err}</div> : null}


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

      <div className="card">
        <div className="card__header">
          <div className="h3">Division season overrides</div>
          <div className="subtle">Override season dates, game length, and blackout windows for a division.</div>
        </div>
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
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h3">Field blackouts</div>
          <div className="subtle">Block specific fields during school breaks, tournaments, or facility conflicts.</div>
        </div>
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
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h3">Season setup wizard</div>
          <div className="subtle">Plan backwards from pool play and bracket, then schedule regular season games.</div>
        </div>
        <div className="card__body">
          <SeasonWizard leagueId={leagueId} tableView={tableView} />
        </div>
      </div>
    </div>
  );
}
