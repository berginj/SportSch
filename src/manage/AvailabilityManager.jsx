import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";
import AvailabilityAllocationsManager from "./AvailabilityAllocationsManager";

const DAY_OPTIONS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

const emptyRule = {
  ruleId: "",
  fieldKey: "",
  division: "",
  startsOn: "",
  endsOn: "",
  daysOfWeek: [],
  startTimeLocal: "",
  endTimeLocal: "",
  timezone: "America/New_York",
  isActive: true,
};

const emptyException = {
  dateFrom: "",
  dateTo: "",
  startTimeLocal: "",
  endTimeLocal: "",
  reason: "",
};

export default function AvailabilityManager({ leagueId }) {
  const [fields, setFields] = useState([]);
  const [divisions, setDivisions] = useState([]);
  const [leagueSeason, setLeagueSeason] = useState(null);
  const [fieldKey, setFieldKey] = useState("");
  const [rules, setRules] = useState([]);
  const [ruleDraft, setRuleDraft] = useState(emptyRule);
  const [exceptionsByRule, setExceptionsByRule] = useState({});
  const [exceptionDrafts, setExceptionDrafts] = useState({});
  const [expandedRuleId, setExpandedRuleId] = useState("");
  const [previewRange, setPreviewRange] = useState({ dateFrom: "", dateTo: "" });
  const [previewSlots, setPreviewSlots] = useState([]);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [ok, setOk] = useState("");

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      setErr("");
      try {
        const [flds, divs, league] = await Promise.all([
          apiFetch("/api/fields"),
          apiFetch("/api/divisions"),
          apiFetch("/api/league"),
        ]);
        const list = Array.isArray(flds) ? flds : [];
        const divList = Array.isArray(divs) ? divs : [];
        setFields(list);
        setDivisions(divList);
        setLeagueSeason(league?.season || null);
        if (!fieldKey && list.length) setFieldKey(list[0].fieldKey || "");
        if (!ruleDraft.division && divList.length) {
          setRuleDraft((prev) => ({
            ...prev,
            division: divList[0].code || divList[0].division || "",
          }));
        }
      } catch (e) {
        setErr(e?.message || "Failed to load fields/divisions.");
        setFields([]);
        setDivisions([]);
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
    if (ruleDraft.ruleId) return;
    if (!ruleDraft.startsOn || !ruleDraft.endsOn) {
      setRuleDraft((prev) => ({
        ...prev,
        startsOn: prev.startsOn || seasonRange.from,
        endsOn: prev.endsOn || seasonRange.to,
      }));
    }
  }, [ruleDraft.ruleId, ruleDraft.startsOn, ruleDraft.endsOn, seasonRange]);

  useEffect(() => {
    if (!previewRange.dateFrom || !previewRange.dateTo) {
      setPreviewRange((prev) => ({
        dateFrom: prev.dateFrom || seasonRange.from,
        dateTo: prev.dateTo || seasonRange.to,
      }));
    }
  }, [previewRange.dateFrom, previewRange.dateTo, seasonRange]);

  useEffect(() => {
    if (!fieldKey) return;
    loadRules(fieldKey).catch(() => {});
  }, [fieldKey]);

  async function loadRules(nextFieldKey) {
    setErr("");
    setOk("");
    setBusy(true);
    try {
      const data = await apiFetch(`/api/availability/rules?fieldKey=${encodeURIComponent(nextFieldKey)}`);
      setRules(Array.isArray(data) ? data : []);
    } catch (e) {
      setErr(e?.message || "Failed to load rules.");
      setRules([]);
    } finally {
      setBusy(false);
    }
  }

  function toggleDay(day) {
    setRuleDraft((prev) => {
      const next = new Set(prev.daysOfWeek || []);
      if (next.has(day)) next.delete(day);
      else next.add(day);
      return { ...prev, daysOfWeek: Array.from(next) };
    });
  }

  async function saveRule() {
    setErr("");
    setOk("");
    if (!fieldKey) return setErr("Pick a field.");
    if (!ruleDraft.division) return setErr("Pick a division.");
    if (!ruleDraft.startsOn || !ruleDraft.endsOn) return setErr("Start and end dates are required.");
    const dateError = validateIsoDates([
      { label: "Starts on", value: ruleDraft.startsOn, required: true },
      { label: "Ends on", value: ruleDraft.endsOn, required: true },
    ]);
    if (dateError) return setErr(dateError);
    if (!ruleDraft.startTimeLocal || !ruleDraft.endTimeLocal) return setErr("Start and end times are required.");
    if (!ruleDraft.daysOfWeek?.length) return setErr("Select at least one day of week.");

    setBusy(true);
    try {
      const payload = {
        ...ruleDraft,
        fieldKey,
      };
      if (ruleDraft.ruleId) {
        await apiFetch(`/api/availability/rules/${encodeURIComponent(ruleDraft.ruleId)}`, {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
        });
        setOk("Rule updated.");
      } else {
        await apiFetch("/api/availability/rules", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
        });
        setOk("Rule created.");
      }
      setRuleDraft((prev) => ({ ...emptyRule, division: prev.division }));
      await loadRules(fieldKey);
    } catch (e) {
      setErr(e?.message || "Failed to save rule.");
    } finally {
      setBusy(false);
    }
  }

  function editRule(rule) {
    setRuleDraft({
      ruleId: rule.ruleId || "",
      fieldKey: rule.fieldKey || "",
      division: rule.division || "",
      startsOn: rule.startsOn || "",
      endsOn: rule.endsOn || "",
      daysOfWeek: Array.isArray(rule.daysOfWeek) ? rule.daysOfWeek : [],
      startTimeLocal: rule.startTimeLocal || "",
      endTimeLocal: rule.endTimeLocal || "",
      timezone: rule.timezone || "America/New_York",
      isActive: !!rule.isActive,
    });
  }

  async function deactivateRule(ruleId) {
    setErr("");
    setOk("");
    setBusy(true);
    try {
      await apiFetch(`/api/availability/rules/${encodeURIComponent(ruleId)}/deactivate`, { method: "PATCH" });
      setOk("Rule deactivated.");
      await loadRules(fieldKey);
    } catch (e) {
      setErr(e?.message || "Failed to deactivate rule.");
    } finally {
      setBusy(false);
    }
  }

  async function loadExceptions(ruleId) {
    try {
      const data = await apiFetch(`/api/availability/rules/${encodeURIComponent(ruleId)}/exceptions`);
      setExceptionsByRule((prev) => ({ ...prev, [ruleId]: Array.isArray(data) ? data : [] }));
    } catch (e) {
      setErr(e?.message || "Failed to load exceptions.");
    }
  }

  function toggleExceptions(ruleId) {
    const next = expandedRuleId === ruleId ? "" : ruleId;
    setExpandedRuleId(next);
    if (next && !exceptionsByRule[ruleId]) loadExceptions(ruleId).catch(() => {});
    if (!exceptionDrafts[ruleId]) {
      setExceptionDrafts((prev) => ({ ...prev, [ruleId]: { ...emptyException } }));
    }
  }

  async function saveException(ruleId) {
    setErr("");
    setOk("");
    const draft = exceptionDrafts[ruleId] || emptyException;
    if (!draft.dateFrom || !draft.dateTo) return setErr("Exception date range is required.");
    const dateError = validateIsoDates([
      { label: "Exception date from", value: draft.dateFrom, required: true },
      { label: "Exception date to", value: draft.dateTo, required: true },
    ]);
    if (dateError) return setErr(dateError);
    if (!draft.startTimeLocal || !draft.endTimeLocal) return setErr("Exception time range is required.");

    setBusy(true);
    try {
      await apiFetch(`/api/availability/rules/${encodeURIComponent(ruleId)}/exceptions`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(draft),
      });
      setOk("Exception saved.");
      setExceptionDrafts((prev) => ({ ...prev, [ruleId]: { ...emptyException } }));
      await loadExceptions(ruleId);
    } catch (e) {
      setErr(e?.message || "Failed to save exception.");
    } finally {
      setBusy(false);
    }
  }

  async function deleteException(ruleId, exceptionId) {
    setErr("");
    setOk("");
    setBusy(true);
    try {
      await apiFetch(`/api/availability/rules/${encodeURIComponent(ruleId)}/exceptions/${encodeURIComponent(exceptionId)}`, {
        method: "DELETE",
      });
      setOk("Exception deleted.");
      await loadExceptions(ruleId);
    } catch (e) {
      setErr(e?.message || "Failed to delete exception.");
    } finally {
      setBusy(false);
    }
  }

  async function loadPreviewSlots() {
    setErr("");
    setOk("");
    if (!previewRange.dateFrom || !previewRange.dateTo) return setErr("Pick preview date range.");
    const dateError = validateIsoDates([
      { label: "Preview date from", value: previewRange.dateFrom, required: true },
      { label: "Preview date to", value: previewRange.dateTo, required: true },
    ]);
    if (dateError) return setErr(dateError);
    setBusy(true);
    try {
      const data = await apiFetch(`/api/availability/preview?dateFrom=${encodeURIComponent(previewRange.dateFrom)}&dateTo=${encodeURIComponent(previewRange.dateTo)}`);
      const slots = Array.isArray(data?.slots) ? data.slots : [];
      const filtered = slots.filter((s) => (!fieldKey || s.fieldKey === fieldKey) && (!ruleDraft.division || s.division === ruleDraft.division));
      setPreviewSlots(filtered);
    } catch (e) {
      setErr(e?.message || "Failed to preview slots.");
      setPreviewSlots([]);
    } finally {
      setBusy(false);
    }
  }

  const divisionOptions = useMemo(
    () => divisions.map((d) => d.code || d.division).filter(Boolean),
    [divisions]
  );

  return (
    <div className="stack">
      {err ? <div className="callout callout--error">{err}</div> : null}
      {ok ? <div className="callout callout--ok">{ok}</div> : null}

      <AvailabilityAllocationsManager leagueId={leagueId} />

      <div className="card">
        <div className="card__header">
          <div className="h2">Availability rules</div>
          <div className="subtle">Create recurring availability windows and add exclusions.</div>
        </div>
        <div className="card__body grid2">
          <label>
            Field
            <select value={fieldKey} onChange={(e) => setFieldKey(e.target.value)}>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName || f.fieldKey}
                </option>
              ))}
            </select>
          </label>
          <label>
            Division
            <select
              value={ruleDraft.division}
              onChange={(e) => setRuleDraft((prev) => ({ ...prev, division: e.target.value }))}
            >
              {divisionOptions.map((d) => (
                <option key={d} value={d}>{d}</option>
              ))}
            </select>
          </label>
          <label>
            Starts on
            <input
              value={ruleDraft.startsOn}
              onChange={(e) => setRuleDraft((prev) => ({ ...prev, startsOn: e.target.value }))}
              placeholder="YYYY-MM-DD"
            />
          </label>
          <label>
            Ends on
            <input
              value={ruleDraft.endsOn}
              onChange={(e) => setRuleDraft((prev) => ({ ...prev, endsOn: e.target.value }))}
              placeholder="YYYY-MM-DD"
            />
          </label>
          <label>
            Start time
            <input
              value={ruleDraft.startTimeLocal}
              onChange={(e) => setRuleDraft((prev) => ({ ...prev, startTimeLocal: e.target.value }))}
              placeholder="17:00"
            />
          </label>
          <label>
            End time
            <input
              value={ruleDraft.endTimeLocal}
              onChange={(e) => setRuleDraft((prev) => ({ ...prev, endTimeLocal: e.target.value }))}
              placeholder="22:00"
            />
          </label>
          <label className="inlineCheck">
            <input
              type="checkbox"
              checked={ruleDraft.isActive}
              onChange={(e) => setRuleDraft((prev) => ({ ...prev, isActive: e.target.checked }))}
            />
            Active
          </label>
          <div>
            <div className="mb-2">Days of week</div>
            <div className="row row--wrap gap-2">
              {DAY_OPTIONS.map((d) => (
                <label key={d} className="inlineCheck">
                  <input type="checkbox" checked={ruleDraft.daysOfWeek.includes(d)} onChange={() => toggleDay(d)} />
                  {d}
                </label>
              ))}
            </div>
          </div>
        </div>
        <div className="card__body row gap-2">
          <button className="btn btn--primary" onClick={saveRule} disabled={busy || !fieldKey}>
            {ruleDraft.ruleId ? "Update rule" : "Create rule"}
          </button>
          {ruleDraft.ruleId ? (
            <button className="btn" onClick={() => setRuleDraft({ ...emptyRule, division: ruleDraft.division })}>
              Clear
            </button>
          ) : null}
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Rules for selected field</div>
          <div className="subtle">Edit, deactivate, and manage exceptions.</div>
        </div>
        {rules.length === 0 ? (
          <div className="card__body muted">No rules yet.</div>
        ) : (
          <div className="card__body tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Division</th>
                  <th>Dates</th>
                  <th>Days</th>
                  <th>Time</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {rules.map((r) => (
                  <tr key={r.ruleId}>
                    <td>{r.division}</td>
                    <td>{r.startsOn} → {r.endsOn}</td>
                    <td>{(r.daysOfWeek || []).join(", ")}</td>
                    <td>{r.startTimeLocal} - {r.endTimeLocal}</td>
                    <td>{r.isActive ? "Active" : "Inactive"}</td>
                    <td>
                      <div className="row row--wrap gap-2">
                        <button className="btn" onClick={() => editRule(r)}>Edit</button>
                        <button className="btn" onClick={() => toggleExceptions(r.ruleId)}>
                          {expandedRuleId === r.ruleId ? "Hide exceptions" : "Exceptions"}
                        </button>
                        {r.isActive ? (
                          <button className="btn" onClick={() => deactivateRule(r.ruleId)}>Deactivate</button>
                        ) : null}
                      </div>
                      {expandedRuleId === r.ruleId ? (
                        <div className="mt-3">
                          <div className="font-bold mb-2">Exceptions</div>
                          {exceptionsByRule[r.ruleId]?.length ? (
                            <div className="tableWrap mb-2">
                              <table className="table">
                                <thead>
                                  <tr>
                                    <th>Dates</th>
                                    <th>Time</th>
                                    <th>Reason</th>
                                    <th>Actions</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {exceptionsByRule[r.ruleId].map((ex) => (
                                    <tr key={ex.exceptionId}>
                                      <td>{ex.dateFrom} → {ex.dateTo}</td>
                                      <td>{ex.startTimeLocal} - {ex.endTimeLocal}</td>
                                      <td>{ex.reason || ""}</td>
                                      <td>
                                        <button className="btn" onClick={() => deleteException(r.ruleId, ex.exceptionId)}>
                                          Delete
                                        </button>
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </div>
                          ) : (
                            <div className="muted mb-2">No exceptions yet.</div>
                          )}
                          <div className="grid2">
                            <label>
                              Date from
                              <input
                                value={(exceptionDrafts[r.ruleId] || emptyException).dateFrom}
                                onChange={(e) => setExceptionDrafts((prev) => ({
                                  ...prev,
                                  [r.ruleId]: { ...(prev[r.ruleId] || emptyException), dateFrom: e.target.value },
                                }))}
                                placeholder="YYYY-MM-DD"
                              />
                            </label>
                            <label>
                              Date to
                              <input
                                value={(exceptionDrafts[r.ruleId] || emptyException).dateTo}
                                onChange={(e) => setExceptionDrafts((prev) => ({
                                  ...prev,
                                  [r.ruleId]: { ...(prev[r.ruleId] || emptyException), dateTo: e.target.value },
                                }))}
                                placeholder="YYYY-MM-DD"
                              />
                            </label>
                            <label>
                              Start time
                              <input
                                value={(exceptionDrafts[r.ruleId] || emptyException).startTimeLocal}
                                onChange={(e) => setExceptionDrafts((prev) => ({
                                  ...prev,
                                  [r.ruleId]: { ...(prev[r.ruleId] || emptyException), startTimeLocal: e.target.value },
                                }))}
                                placeholder="17:00"
                              />
                            </label>
                            <label>
                              End time
                              <input
                                value={(exceptionDrafts[r.ruleId] || emptyException).endTimeLocal}
                                onChange={(e) => setExceptionDrafts((prev) => ({
                                  ...prev,
                                  [r.ruleId]: { ...(prev[r.ruleId] || emptyException), endTimeLocal: e.target.value },
                                }))}
                                placeholder="22:00"
                              />
                            </label>
                            <label className="col-span-2">
                              Reason (optional)
                              <input
                                value={(exceptionDrafts[r.ruleId] || emptyException).reason}
                                onChange={(e) => setExceptionDrafts((prev) => ({
                                  ...prev,
                                  [r.ruleId]: { ...(prev[r.ruleId] || emptyException), reason: e.target.value },
                                }))}
                                placeholder="Holiday"
                              />
                            </label>
                          </div>
                          <button className="btn mt-2" onClick={() => saveException(r.ruleId)} disabled={busy}>
                            Add exception
                          </button>
                        </div>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Availability preview</div>
          <div className="subtle">Preview generated slots for the selected field/division.</div>
        </div>
        <div className="card__body grid2">
          <label>
            Date from
            <input
              value={previewRange.dateFrom}
              onChange={(e) => setPreviewRange((prev) => ({ ...prev, dateFrom: e.target.value }))}
              placeholder="YYYY-MM-DD"
            />
          </label>
          <label>
            Date to
            <input
              value={previewRange.dateTo}
              onChange={(e) => setPreviewRange((prev) => ({ ...prev, dateTo: e.target.value }))}
              placeholder="YYYY-MM-DD"
            />
          </label>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={loadPreviewSlots} disabled={busy || !fieldKey}>
            Preview slots
          </button>
        </div>
        {previewSlots.length ? (
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
                {previewSlots.slice(0, 30).map((s, idx) => (
                  <tr key={`${s.gameDate}-${s.startTime}-${idx}`}>
                    <td>{s.gameDate}</td>
                    <td>{s.startTime} - {s.endTime}</td>
                    <td>{s.fieldKey}</td>
                    <td>{s.division}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {previewSlots.length > 30 ? <div className="subtle">Showing first 30 slots.</div> : null}
          </div>
        ) : (
          <div className="card__body muted">No preview slots yet.</div>
        )}
      </div>
    </div>
  );
}
