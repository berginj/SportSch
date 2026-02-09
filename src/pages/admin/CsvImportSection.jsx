import CollapsibleSection from "../../components/CollapsibleSection";

export default function CsvImportSection({
  leagueId,
  slotsFile,
  setSlotsFile,
  slotsBusy,
  slotsErr,
  slotsOk,
  slotsErrors,
  slotsWarnings,
  importSlotsCsv,
  teamsFile,
  setTeamsFile,
  teamsBusy,
  teamsErr,
  teamsOk,
  teamsErrors,
  importTeamsCsv,
  downloadTeamsTemplate,
}) {
  return (
    <div className="stack gap-4">
      <div className="card">
        <h3 className="m-0">League admin uploads</h3>
        <p className="muted">
          Upload CSVs for schedules (slots) and teams. Team imports can prefill coach contact info.
        </p>
      </div>

      <CollapsibleSection
        title="Schedule (slots) CSV upload"
        subtitle="Import game/practice slots from CSV (used during setup and roster changes)"
        badge="Setup Phase"
        badgeColor="blue"
        defaultExpanded={false}
        icon="📅"
      >
        <div className="stack gap-3">
          <div className="subtle">
            Required columns: <code>division</code>, <code>offeringTeamId</code>, <code>gameDate</code>,{" "}
            <code>startTime</code>, <code>endTime</code>, <code>fieldKey</code>. Optional:{" "}
            <code>offeringEmail</code>, <code>gameType</code>, <code>notes</code>, <code>status</code>.
          </div>
          {slotsErr ? <div className="callout callout--error">{slotsErr}</div> : null}
          {slotsOk ? <div className="callout callout--ok">{slotsOk}</div> : null}
          <div className="row items-end gap-3">
            <label className="flex-1">
              CSV file
              <input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => setSlotsFile(e.target.files?.[0] || null)}
                disabled={slotsBusy}
              />
            </label>
            <button className="btn" onClick={importSlotsCsv} disabled={slotsBusy || !slotsFile}>
              {slotsBusy ? "Importing..." : "Upload & Import"}
            </button>
          </div>
          {slotsErrors.length ? (
            <div>
              <div className="font-bold mb-2">Rejected rows ({slotsErrors.length})</div>
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Row</th>
                      <th>Error</th>
                    </tr>
                  </thead>
                  <tbody>
                    {slotsErrors.slice(0, 50).map((x, idx) => (
                      <tr key={idx}>
                        <td>{x.row}</td>
                        <td>{x.error}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {slotsErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
          {slotsWarnings.length ? (
            <div>
              <div className="font-bold mb-2">Warnings ({slotsWarnings.length})</div>
              <div className="subtle mb-2">
                These rows were skipped because they overlapped existing slots or duplicates in the CSV.
              </div>
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Row</th>
                      <th>Warning</th>
                    </tr>
                  </thead>
                  <tbody>
                    {slotsWarnings.slice(0, 50).map((x, idx) => (
                      <tr key={idx}>
                        <td>{x.row}</td>
                        <td>{x.warning}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {slotsWarnings.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
        </div>
      </CollapsibleSection>

      <CollapsibleSection
        title="Teams CSV upload"
        subtitle="Import teams and coach contact info from CSV (used during setup and roster changes)"
        badge="Setup Phase"
        badgeColor="blue"
        defaultExpanded={false}
        icon="👥"
      >
        <div className="stack gap-3">
          <div className="subtle">
            Required columns: <code>division</code>, <code>teamId</code>, <code>name</code>. Optional:{" "}
            <code>coachName</code>, <code>coachEmail</code>, <code>coachPhone</code>.
          </div>
          <div className="subtle">
            Need a starting point? Download a template prefilled with this league's division codes.
          </div>
          {teamsErr ? <div className="callout callout--error">{teamsErr}</div> : null}
          {teamsOk ? <div className="callout callout--ok">{teamsOk}</div> : null}
          <div className="row items-end gap-3">
            <label className="flex-1">
              CSV file
              <input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => setTeamsFile(e.target.files?.[0] || null)}
                disabled={teamsBusy}
              />
            </label>
            <button className="btn" onClick={importTeamsCsv} disabled={teamsBusy || !teamsFile}>
              {teamsBusy ? "Importing..." : "Upload & Import"}
            </button>
            <button
              className="btn btn--ghost"
              onClick={downloadTeamsTemplate}
              disabled={!leagueId}
              title="Download a CSV template with division codes."
            >
              Download CSV template
            </button>
          </div>
          {teamsErrors.length ? (
            <div>
              <div className="font-bold mb-2">Rejected rows ({teamsErrors.length})</div>
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Row</th>
                      <th>Error</th>
                    </tr>
                  </thead>
                  <tbody>
                    {teamsErrors.slice(0, 50).map((x, idx) => (
                      <tr key={idx}>
                        <td>{x.row}</td>
                        <td>{x.error}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {teamsErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
        </div>
      </CollapsibleSection>
    </div>
  );
}
