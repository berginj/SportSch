/**
 * SchedulePreview - Displays the generated schedule preview
 * Shows assignments, unassigned matchups, unused slots, and validation issues
 */
export default function SchedulePreview({ preview }) {
  if (!preview) return null;

  return (
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

      {preview.failures?.length ? (
        <div className="card__body">
          <div className="h2">Validation issues</div>
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
                {preview.failures.map((f, idx) => (
                  <tr key={`${f.ruleId || "issue"}-${idx}`}>
                    <td>{f.ruleId || ""}</td>
                    <td>{f.severity || ""}</td>
                    <td>{f.message || ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}
    </div>
  );
}
