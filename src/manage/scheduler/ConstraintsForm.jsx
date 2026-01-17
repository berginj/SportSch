/**
 * ConstraintsForm - Form for configuring schedule generation constraints
 * Allows setting division, date range, game constraints, and preferred days
 */
export default function ConstraintsForm({
  divisions,
  division,
  setDivision,
  dateFrom,
  setDateFrom,
  dateTo,
  setDateTo,
  maxGamesPerWeek,
  setMaxGamesPerWeek,
  noDoubleHeaders,
  setNoDoubleHeaders,
  balanceHomeAway,
  setBalanceHomeAway,
  externalOfferPerWeek,
  setExternalOfferPerWeek,
  preferredDays,
  setPreferredDays,
  effectiveSeason,
  loading,
  validationLoading,
  preview,
  runPreview,
  applySchedule,
  runValidation,
  exportCsv,
  exportSportsEngineCsv,
  exportGameChangerCsv,
}) {
  return (
    <div className="card">
      <div className="card__header">
        <div className="h2">Division scheduler</div>
        <div className="subtle">Build a balanced schedule from your open slots.</div>
      </div>

      {effectiveSeason ? (
        <div className="card__body">
          <div className="subtle">
            Season defaults: Spring {effectiveSeason.springStart || "?"} - {effectiveSeason.springEnd || "?"}, Fall {effectiveSeason.fallStart || "?"} - {effectiveSeason.fallEnd || "?"}. Game length: {effectiveSeason.gameLengthMinutes || "?"} min.
          </div>
        </div>
      ) : null}

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
          External offers per week
          <input
            type="number"
            min="0"
            value={externalOfferPerWeek}
            onChange={(e) => setExternalOfferPerWeek(e.target.value)}
          />
        </label>

        <div className="stack gap-1">
          <span className="muted">Preferred game days (optional)</span>
          <div className="row row--wrap gap-2">
            {Object.keys(preferredDays).map((day) => (
              <label key={day} className="inlineCheck">
                <input
                  type="checkbox"
                  checked={preferredDays[day]}
                  onChange={(e) => setPreferredDays((prev) => ({ ...prev, [day]: e.target.checked }))}
                />
                {day}
              </label>
            ))}
          </div>
        </div>
      </div>

      <div className="card__body row gap-2">
        <button className="btn" onClick={runPreview} disabled={loading || !division}>
          {loading ? "Working..." : "Preview schedule"}
        </button>
        <button className="btn btn--primary" onClick={applySchedule} disabled={loading || !division}>
          Apply schedule
        </button>
        <button className="btn" onClick={runValidation} disabled={validationLoading || !division}>
          {validationLoading ? "Validating..." : "Run validations"}
        </button>
        <button className="btn" onClick={exportCsv} disabled={!preview?.assignments?.length}>
          Export CSV
        </button>
        <button className="btn" onClick={exportSportsEngineCsv} disabled={!preview?.assignments?.length}>
          Export SportsEngine CSV
        </button>
        <button className="btn" onClick={exportGameChangerCsv} disabled={!preview?.assignments?.length}>
          Export GameChanger CSV
        </button>
      </div>
    </div>
  );
}
