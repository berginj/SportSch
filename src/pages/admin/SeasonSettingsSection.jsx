/**
 * Season settings section for global admins.
 * Allows configuring season dates, blackout windows, and game length for any league.
 */
export default function SeasonSettingsSection({
  seasonLeagueId,
  setSeasonLeagueId,
  seasonDraft,
  setSeasonDraft,
  blackoutsDraft,
  setBlackoutsDraft,
  saveSeasonConfig,
  applySeasonFromLeague,
  globalLoading,
  globalLeagues,
}) {
  return (
    <div className="card mt-4">
      <h4 className="m-0">Season settings</h4>
      <p className="muted">Set league season dates, blackout windows, and default game length.</p>

      <div className="row gap-3 row--wrap mb-3">
        <label className="min-w-[220px]">
          League
          <select value={seasonLeagueId} onChange={(e) => setSeasonLeagueId(e.target.value)}>
            {globalLeagues.map((l) => (
              <option key={l.leagueId} value={l.leagueId}>
                {l.name ? `${l.name} (${l.leagueId})` : l.leagueId}
              </option>
            ))}
          </select>
        </label>
        <label className="min-w-[180px]">
          Game length (minutes)
          <input
            type="number"
            min="1"
            value={seasonDraft.gameLengthMinutes}
            onChange={(e) => setSeasonDraft((p) => ({ ...p, gameLengthMinutes: e.target.value }))}
          />
        </label>
      </div>

      <div className="grid2 mb-3">
        <label>
          Spring start
          <input
            value={seasonDraft.springStart}
            onChange={(e) => setSeasonDraft((p) => ({ ...p, springStart: e.target.value }))}
            placeholder="YYYY-MM-DD"
          />
        </label>
        <label>
          Spring end
          <input
            value={seasonDraft.springEnd}
            onChange={(e) => setSeasonDraft((p) => ({ ...p, springEnd: e.target.value }))}
            placeholder="YYYY-MM-DD"
          />
        </label>
        <label>
          Fall start
          <input
            value={seasonDraft.fallStart}
            onChange={(e) => setSeasonDraft((p) => ({ ...p, fallStart: e.target.value }))}
            placeholder="YYYY-MM-DD"
          />
        </label>
        <label>
          Fall end
          <input
            value={seasonDraft.fallEnd}
            onChange={(e) => setSeasonDraft((p) => ({ ...p, fallEnd: e.target.value }))}
            placeholder="YYYY-MM-DD"
          />
        </label>
      </div>

      <div className="mb-3">
        <div className="font-bold mb-2">Blackout windows</div>
        {blackoutsDraft.length === 0 ? <div className="muted">No blackouts yet.</div> : null}
        {blackoutsDraft.map((b, idx) => (
          <div key={`${b.startDate}-${b.endDate}-${idx}`} className="row gap-2 row--wrap mb-2">
            <input
              className="min-w-[160px]"
              value={b.startDate}
              onChange={(e) => {
                const next = [...blackoutsDraft];
                next[idx] = { ...next[idx], startDate: e.target.value };
                setBlackoutsDraft(next);
              }}
              placeholder="Start (YYYY-MM-DD)"
            />
            <input
              className="min-w-[160px]"
              value={b.endDate}
              onChange={(e) => {
                const next = [...blackoutsDraft];
                next[idx] = { ...next[idx], endDate: e.target.value };
                setBlackoutsDraft(next);
              }}
              placeholder="End (YYYY-MM-DD)"
            />
            <input
              className="min-w-[220px]"
              value={b.label}
              onChange={(e) => {
                const next = [...blackoutsDraft];
                next[idx] = { ...next[idx], label: e.target.value };
                setBlackoutsDraft(next);
              }}
              placeholder="Label (optional)"
            />
            <button
              className="btn"
              type="button"
              onClick={() => setBlackoutsDraft((prev) => prev.filter((_, i) => i !== idx))}
            >
              Remove
            </button>
          </div>
        ))}
        <button
          className="btn btn--ghost"
          type="button"
          onClick={() => setBlackoutsDraft((prev) => [...prev, { startDate: "", endDate: "", label: "" }])}
        >
          Add blackout
        </button>
      </div>

      <div className="row gap-2">
        <button className="btn btn--primary" onClick={saveSeasonConfig} disabled={globalLoading}>
          Save season settings
        </button>
        <button
          className="btn"
          onClick={() => {
            const league = globalLeagues.find((l) => l.leagueId === seasonLeagueId);
            if (league) applySeasonFromLeague(league);
          }}
          disabled={globalLoading}
        >
          Reset
        </button>
      </div>
    </div>
  );
}
