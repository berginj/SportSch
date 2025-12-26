import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import Toast from "../components/Toast";

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

export default function SchedulerManager({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [maxGamesPerWeek, setMaxGamesPerWeek] = useState(2);
  const [noDoubleHeaders, setNoDoubleHeaders] = useState(true);
  const [balanceHomeAway, setBalanceHomeAway] = useState(true);
  const [externalOfferCount, setExternalOfferCount] = useState(0);
  const [preview, setPreview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      try {
        const divs = await apiFetch("/api/divisions");
        const list = Array.isArray(divs) ? divs : [];
        setDivisions(list);
        if (!division && list.length) setDivision(list[0].code || list[0].division || "");
      } catch (e) {
        setErr(e?.message || "Failed to load divisions");
        setDivisions([]);
      }
    })();
  }, [leagueId]);

  const payload = useMemo(() => {
    return {
      division,
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
      constraints: {
        maxGamesPerWeek: Number(maxGamesPerWeek) || undefined,
        noDoubleHeaders,
        balanceHomeAway,
        externalOfferCount: Number(externalOfferCount) || 0,
      },
    };
  }, [division, dateFrom, dateTo, maxGamesPerWeek, noDoubleHeaders, balanceHomeAway, externalOfferCount]);

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
            External offers to keep open
            <input
              type="number"
              min="0"
              value={externalOfferCount}
              onChange={(e) => setExternalOfferCount(e.target.value)}
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
        </div>
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
    </div>
  );
}
