/**
 * ValidationResults - Displays schedule validation results
 * Shows validation issues with severity and details
 */
export default function ValidationResults({ validation }) {
  if (!validation) return null;

  return (
    <div className="card">
      <div className="card__header">
        <div className="h2">Validation results</div>
        <div className="subtle">Checks against scheduled games in the selected range.</div>
      </div>

      <div className="card__body">
        <div className="row row--wrap gap-4">
          <div className="layoutStat">
            <div className="layoutStat__value">{validation.totalIssues ?? 0}</div>
            <div className="layoutStat__label">Total issues</div>
          </div>
        </div>
      </div>

      {validation.issues?.length ? (
        <div className="card__body tableWrap">
          <table className="table">
            <thead>
              <tr>
                <th>Rule</th>
                <th>Severity</th>
                <th>Message</th>
              </tr>
            </thead>
            <tbody>
              {validation.issues.map((f, idx) => (
                <tr key={`${f.ruleId || "issue"}-${idx}`}>
                  <td>{f.ruleId || ""}</td>
                  <td>{f.severity || ""}</td>
                  <td>{f.message || ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="card__body muted">No validation issues found.</div>
      )}
    </div>
  );
}
