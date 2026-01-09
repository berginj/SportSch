import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import Toast from "../components/Toast";

const WEEKDAY_OPTIONS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

function StepButton({ active, onClick, children }) {
  return (
    <button
      className={`btn btn--ghost ${active ? "is-active" : ""}`}
      type="button"
      onClick={onClick}
    >
      {children}
    </button>
  );
}

export default function SeasonWizard({ leagueId, tableView = "A" }) {
  const [division, setDivision] = useState("");
  const [divisions, setDivisions] = useState([]);
  const [seasonStart, setSeasonStart] = useState("");
  const [seasonEnd, setSeasonEnd] = useState("");
  const [poolStart, setPoolStart] = useState("");
  const [poolEnd, setPoolEnd] = useState("");
  const [bracketStart, setBracketStart] = useState("");
  const [bracketEnd, setBracketEnd] = useState("");

  const [minGamesPerTeam, setMinGamesPerTeam] = useState(0);
  const [poolGamesPerTeam, setPoolGamesPerTeam] = useState(1);
  const [preferredWeeknights, setPreferredWeeknights] = useState([]);
  const [guestGamesPerWeek, setGuestGamesPerWeek] = useState(0);
  const [maxGamesPerWeek, setMaxGamesPerWeek] = useState(2);
  const [noDoubleHeaders, setNoDoubleHeaders] = useState(true);
  const [balanceHomeAway, setBalanceHomeAway] = useState(true);

  const [step, setStep] = useState(0);
  const [preview, setPreview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      setErr("");
      try {
        const [divs, league] = await Promise.all([
          apiFetch("/api/divisions"),
          apiFetch("/api/league"),
        ]);
        const list = Array.isArray(divs) ? divs : [];
        setDivisions(list);
        if (!division && list.length) {
          setDivision(list[0].code || list[0].division || "");
        }
        const season = league?.season || {};
        setSeasonStart(season.springStart || "");
        setSeasonEnd(season.springEnd || "");
      } catch (e) {
        setErr(e?.message || "Failed to load wizard data.");
      }
    })();
  }, [leagueId]);

  const steps = useMemo(
    () => ["Basics", "Postseason", "Rules", "Preview"],
    []
  );

  function toggleWeeknight(day) {
    setPreferredWeeknights((prev) => {
      if (prev.includes(day)) return prev.filter((d) => d !== day);
      return [...prev, day].slice(0, 2);
    });
  }

  async function runPreview() {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Season start", value: seasonStart, required: true },
      { label: "Season end", value: seasonEnd, required: true },
      { label: "Pool start", value: poolStart, required: false },
      { label: "Pool end", value: poolEnd, required: false },
      { label: "Bracket start", value: bracketStart, required: false },
      { label: "Bracket end", value: bracketEnd, required: false },
    ]);
    if (dateError) return setErr(dateError);
    setLoading(true);
    try {
      const payload = {
        division,
        seasonStart,
        seasonEnd,
        poolStart: poolStart || undefined,
        poolEnd: poolEnd || undefined,
        bracketStart: bracketStart || undefined,
        bracketEnd: bracketEnd || undefined,
        minGamesPerTeam: Number(minGamesPerTeam) || 0,
        poolGamesPerTeam: Number(poolGamesPerTeam) || 1,
        preferredWeeknights,
        externalOfferPerWeek: Number(guestGamesPerWeek) || 0,
        maxGamesPerWeek: Number(maxGamesPerWeek) || 0,
        noDoubleHeaders,
        balanceHomeAway,
      };
      const data = await apiFetch("/api/schedule/wizard/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setPreview(data || null);
      setStep(3);
    } catch (e) {
      setErr(e?.message || "Preview failed.");
      setPreview(null);
    } finally {
      setLoading(false);
    }
  }

  async function applySchedule() {
    if (!preview) return;
    setErr("");
    const dateError = validateIsoDates([
      { label: "Season start", value: seasonStart, required: true },
      { label: "Season end", value: seasonEnd, required: true },
      { label: "Pool start", value: poolStart, required: false },
      { label: "Pool end", value: poolEnd, required: false },
      { label: "Bracket start", value: bracketStart, required: false },
      { label: "Bracket end", value: bracketEnd, required: false },
    ]);
    if (dateError) return setErr(dateError);
    setLoading(true);
    try {
      const payload = {
        division,
        seasonStart,
        seasonEnd,
        poolStart: poolStart || undefined,
        poolEnd: poolEnd || undefined,
        bracketStart: bracketStart || undefined,
        bracketEnd: bracketEnd || undefined,
        minGamesPerTeam: Number(minGamesPerTeam) || 0,
        poolGamesPerTeam: Number(poolGamesPerTeam) || 1,
        preferredWeeknights,
        externalOfferPerWeek: Number(guestGamesPerWeek) || 0,
        maxGamesPerWeek: Number(maxGamesPerWeek) || 0,
        noDoubleHeaders,
        balanceHomeAway,
      };
      await apiFetch("/api/schedule/wizard/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setToast({ tone: "success", message: "Wizard schedule applied." });
    } catch (e) {
      setErr(e?.message || "Apply failed.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="stack gap-3">
      {toast ? <Toast {...toast} onClose={() => setToast(null)} /> : null}
      {err ? <div className="callout callout--error">{err}</div> : null}

      <div className="row row--wrap gap-2">
        {steps.map((label, idx) => (
          <StepButton key={label} active={step === idx} onClick={() => setStep(idx)}>
            {label}
          </StepButton>
        ))}
      </div>

      {step === 0 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Season basics</div>
            <div className="subtle">Pick the division and season range.</div>
          </div>
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
              Season start
              <input value={seasonStart} onChange={(e) => setSeasonStart(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
            <label>
              Season end
              <input value={seasonEnd} onChange={(e) => setSeasonEnd(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
          </div>
          <div className="row row--end">
            <button className="btn btn--primary" type="button" onClick={() => setStep(1)}>
              Next
            </button>
          </div>
        </div>
      ) : null}

      {step === 1 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Postseason windows</div>
            <div className="subtle">Reserve the last week for pool play and the following week for the bracket.</div>
          </div>
          <div className="card__body grid2">
            <label>
              Pool play start
              <input value={poolStart} onChange={(e) => setPoolStart(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
            <label>
              Pool play end
              <input value={poolEnd} onChange={(e) => setPoolEnd(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
            <label>
              Bracket start
              <input value={bracketStart} onChange={(e) => setBracketStart(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
            <label>
              Bracket end
              <input value={bracketEnd} onChange={(e) => setBracketEnd(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(0)}>
              Back
            </button>
            <button className="btn btn--primary" type="button" onClick={() => setStep(2)}>
              Next
            </button>
          </div>
        </div>
      ) : null}

      {step === 2 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Scheduling rules</div>
            <div className="subtle">Set regular season and pool play constraints.</div>
          </div>
          <div className="card__body grid2">
            <label>
              Min games per team (regular season)
              <input
                type="number"
                min="0"
                value={minGamesPerTeam}
                onChange={(e) => setMinGamesPerTeam(e.target.value)}
              />
            </label>
            <label>
              Pool games per team (pool week)
              <input
                type="number"
                min="0"
                value={poolGamesPerTeam}
                onChange={(e) => setPoolGamesPerTeam(e.target.value)}
              />
            </label>
            <label>
              Guest games per week
              <input
                type="number"
                min="0"
                value={guestGamesPerWeek}
                onChange={(e) => setGuestGamesPerWeek(e.target.value)}
              />
            </label>
            <label>
              Max games per team per week
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
            <div className="stack gap-2">
              <div className="muted text-sm">Preferred weeknights (pick two)</div>
              <div className="row row--wrap gap-2">
                {WEEKDAY_OPTIONS.map((day) => (
                  <button
                    key={day}
                    className={`pill ${preferredWeeknights.includes(day) ? "is-active" : ""}`}
                    type="button"
                    onClick={() => toggleWeeknight(day)}
                  >
                    {day}
                  </button>
                ))}
              </div>
            </div>
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(1)}>
              Back
            </button>
            <button className="btn btn--primary" type="button" onClick={runPreview} disabled={loading}>
              {loading ? "Previewing..." : "Preview schedule"}
            </button>
          </div>
        </div>
      ) : null}

      {step === 3 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Preview & apply</div>
            <div className="subtle">Review assignments and apply when ready.</div>
          </div>
          <div className="card__body stack gap-3">
            {!preview ? (
              <div className="muted">Run a preview to see results.</div>
            ) : (
              <>
                <div className="layoutStatRow">
                  <div className="layoutStat">
                    <div className="layoutStat__value">{preview.summary?.regularSeason?.slotsAssigned ?? 0}</div>
                    <div className="layoutStat__label">Regular season games</div>
                  </div>
                  <div className="layoutStat">
                    <div className="layoutStat__value">{preview.summary?.poolPlay?.slotsAssigned ?? 0}</div>
                    <div className="layoutStat__label">Pool play games</div>
                  </div>
                  <div className="layoutStat">
                    <div className="layoutStat__value">{preview.summary?.bracket?.slotsAssigned ?? 0}</div>
                    <div className="layoutStat__label">Bracket games</div>
                  </div>
                  <div className="layoutStat">
                    <div className="layoutStat__value">{preview.summary?.totalSlots ?? 0}</div>
                    <div className="layoutStat__label">Total availability slots</div>
                  </div>
                </div>

              {preview.warnings?.length ? (
                <div className="callout">
                  {preview.warnings.map((w, idx) => (
                    <div key={idx} className="subtle">{w.message}</div>
                  ))}
                </div>
              ) : null}
              {preview.issues?.length ? (
                <div className="callout callout--error">
                  <div className="font-bold mb-2">Schedule rule issues ({preview.totalIssues || preview.issues.length})</div>
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
                        {preview.issues.map((issue, idx) => (
                          <tr key={`${issue.ruleId || "issue"}-${idx}`}>
                            <td>{issue.ruleId || ""}</td>
                            <td>{issue.severity || ""}</td>
                            <td>{issue.message || ""}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : null}

                <div className={`tableWrap ${tableView === "B" ? "tableWrap--sticky" : ""}`}>
                  <table className={`table ${tableView === "B" ? "table--compact table--sticky" : ""}`}>
                    <thead>
                      <tr>
                        <th>Phase</th>
                        <th>Date</th>
                        <th>Time</th>
                        <th>Field</th>
                        <th>Home</th>
                        <th>Away</th>
                      </tr>
                    </thead>
                    <tbody>
                      {(preview.assignments || []).slice(0, 250).map((a) => (
                        <tr key={`${a.phase}-${a.slotId}`}>
                          <td>{a.phase}</td>
                          <td>{a.gameDate}</td>
                          <td>{a.startTime}-{a.endTime}</td>
                          <td>{a.fieldKey}</td>
                          <td>{a.homeTeamId || "-"}</td>
                          <td>{a.awayTeamId || "-"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {(preview.assignments || []).length > 250 ? (
                    <div className="subtle mt-2">Showing first 250 assignments.</div>
                  ) : null}
                </div>
              </>
            )}
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(2)}>
              Back
            </button>
            <button className="btn btn--primary" type="button" onClick={applySchedule} disabled={loading || !preview}>
              {loading ? "Applying..." : "Apply schedule"}
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
