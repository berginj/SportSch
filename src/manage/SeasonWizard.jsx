import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import { buildAvailabilityInsights } from "../lib/availabilityInsights";
import Toast from "../components/Toast";

const WEEKDAY_OPTIONS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const ISSUE_HINTS = {
  "unassigned-matchups": "Not enough availability slots, or constraints are too tight for the slot pool.",
  "unassigned-slots": "More availability than matchups. These can become extra offers or remain unused.",
  "double-header": "Not enough slots to spread games across dates. Add slots or relax no-doubleheaders.",
  "max-games-per-week": "Max games/week is too low for available slots. Increase the limit or add more slots.",
  "missing-opponent": "A slot is missing an opponent. Check team count or external/guest game settings.",
};

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
  const [strictPreferredWeeknights, setStrictPreferredWeeknights] = useState(false);
  const [guestGamesPerWeek, setGuestGamesPerWeek] = useState(0);
  const [maxGamesPerWeek, setMaxGamesPerWeek] = useState(2);
  const [noDoubleHeaders, setNoDoubleHeaders] = useState(true);
  const [balanceHomeAway, setBalanceHomeAway] = useState(true);

  const [step, setStep] = useState(0);
  const [preview, setPreview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);
  const [availabilityInsights, setAvailabilityInsights] = useState(null);
  const [autoAppliedPreferred, setAutoAppliedPreferred] = useState(false);
  const [availabilityLoading, setAvailabilityLoading] = useState(false);
  const [availabilityErr, setAvailabilityErr] = useState("");
  const [preferredTouched, setPreferredTouched] = useState(false);

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
    setPreferredTouched(true);
    setPreferredWeeknights((prev) => {
      if (prev.includes(day)) return prev.filter((d) => d !== day);
      return [...prev, day].slice(0, 2);
    });
  }

  useEffect(() => {
    if (!division) return;
    setPreferredTouched(false);
    setPreferredWeeknights([]);
    setAutoAppliedPreferred(false);
  }, [division]);

  useEffect(() => {
    if (!leagueId || !division) return;
    if (!seasonStart || !seasonEnd) return;
    (async () => {
      setAvailabilityErr("");
      setAvailabilityLoading(true);
      try {
        const qs = new URLSearchParams();
        qs.set("division", division);
        qs.set("dateFrom", seasonStart);
        qs.set("dateTo", seasonEnd);
        qs.set("status", "Open");
        const data = await apiFetch(`/api/slots?${qs.toString()}`);
        const list = Array.isArray(data) ? data : [];
        const availability = list.filter((s) => s.isAvailability);
        const insights = buildAvailabilityInsights(availability);
        setAvailabilityInsights(insights);
        if (!preferredTouched && insights.suggested.length) {
          setPreferredWeeknights(insights.suggested);
          setAutoAppliedPreferred(true);
        }
      } catch (e) {
        setAvailabilityErr(e?.message || "Failed to load availability insights.");
        setAvailabilityInsights(null);
      } finally {
        setAvailabilityLoading(false);
      }
    })();
  }, [leagueId, division, seasonStart, seasonEnd, preferredTouched]);

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
        strictPreferredWeeknights,
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
        strictPreferredWeeknights,
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

  function buildIssueHint(issue, summary) {
    if (!issue) return "";
    const base = ISSUE_HINTS[issue.ruleId] || "";
    if (!summary) return base;
    if (issue.ruleId === "unassigned-matchups") {
      const phase = summary.regularSeason || {};
      if (phase.matchupsTotal > phase.slotsTotal) {
        return `${base} Regular season has fewer slots (${phase.slotsTotal}) than matchups (${phase.matchupsTotal}).`;
      }
    }
    if (issue.ruleId === "double-header" && summary.teamCount && summary.teamCount % 2 === 1) {
      return `${base} With an odd team count (${summary.teamCount}), some byes help reduce doubleheaders.`;
    }
    return base;
  }

  function buildContextNotes(summary, issues) {
    if (!summary) return [];
    const notes = [];
    const regular = summary.regularSeason || {};
    const pool = summary.poolPlay || {};
    const bracket = summary.bracket || {};

    if (regular.slotsTotal < regular.matchupsTotal) {
      notes.push(`Regular season has ${regular.slotsTotal} slots for ${regular.matchupsTotal} matchups.`);
    }
    if (pool.matchupsTotal > 0 && pool.slotsTotal < pool.matchupsTotal) {
      notes.push(`Pool play has ${pool.slotsTotal} slots for ${pool.matchupsTotal} matchups.`);
    }
    if (bracket.matchupsTotal > 0 && bracket.slotsTotal < bracket.matchupsTotal) {
      notes.push(`Bracket has ${bracket.slotsTotal} slots for ${bracket.matchupsTotal} matchups.`);
    }
    if (summary.teamCount % 2 === 1) {
      notes.push(`Odd team count (${summary.teamCount}) adds BYEs and can create gaps.`);
    }
    if ((issues || []).some((i) => i.ruleId === "double-header")) {
      notes.push("Doubleheaders indicate tight slot density or too few usable dates.");
    }
    if ((issues || []).some((i) => i.ruleId === "max-games-per-week")) {
      notes.push("Max games/week is restricting assignments; increase it or add slots.");
    }
    if ((issues || []).some((i) => i.ruleId === "missing-opponent")) {
      notes.push("Guest games or external offers may be enabled; missing opponents are expected there.");
    }
    return notes;
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
              <div className="muted text-sm">Preferred weeknights (pick two; other nights can still be used)</div>
              {availabilityLoading ? (
                <div className="muted text-sm">Analyzing availability for recommended nights...</div>
              ) : availabilityInsights?.suggested?.length ? (
                <div className="callout">
                  Recommended nights based on availability: <b>{availabilityInsights.suggested.join(", ")}</b>
                  {autoAppliedPreferred ? (
                    <span className="pill ml-2">Auto-selected</span>
                  ) : null}
                </div>
              ) : availabilityErr ? (
                <div className="callout callout--error">{availabilityErr}</div>
              ) : null}
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
              <label className="inlineCheck">
                <input
                  type="checkbox"
                  checked={strictPreferredWeeknights}
                  onChange={(e) => setStrictPreferredWeeknights(e.target.checked)}
                />
                Only use preferred nights (ignore other days)
              </label>
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
                          <th>Hint</th>
                        </tr>
                      </thead>
                      <tbody>
                        {preview.issues.map((issue, idx) => (
                          <tr key={`${issue.ruleId || "issue"}-${idx}`}>
                            <td>{issue.ruleId || ""}</td>
                            <td>{issue.severity || ""}</td>
                            <td>{issue.message || ""}</td>
                            <td>{buildIssueHint(issue, preview.summary)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : null}
              {preview.summary ? (
                <div className="callout">
                  <div className="font-bold mb-2">Why issues happen</div>
                  {buildContextNotes(preview.summary, preview.issues).length ? (
                    <div className="stack gap-1">
                      {buildContextNotes(preview.summary, preview.issues).map((note, idx) => (
                        <div key={idx} className="subtle">{note}</div>
                      ))}
                    </div>
                  ) : (
                    <div className="subtle">No extra context available for these issues.</div>
                  )}
                </div>
              ) : null}
              {preview.summary ? (
                <div className="callout">
                  <div className="font-bold mb-2">Scheduling context</div>
                  <div className="subtle">Teams: {preview.summary.teamCount || 0} {preview.summary.teamCount % 2 === 1 ? "(odd team count adds byes)" : ""}</div>
                  <div className="tableWrap mt-2">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Phase</th>
                          <th>Slots</th>
                          <th>Matchups</th>
                          <th>Assigned</th>
                          <th>Unassigned</th>
                        </tr>
                      </thead>
                      <tbody>
                        {[preview.summary.regularSeason, preview.summary.poolPlay, preview.summary.bracket].map((phase) => (
                          <tr key={phase.phase}>
                            <td>{phase.phase}</td>
                            <td>{phase.slotsTotal}</td>
                            <td>{phase.matchupsTotal}</td>
                            <td>{phase.slotsAssigned}</td>
                            <td>{phase.unassignedMatchups}</td>
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
