export default function LeaguePicker({ leagueId, setLeagueId, me, label = "League", title }) {
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const hasLeagues = memberships.length > 0;

  return (
    <label className="leaguePicker" title={title || "Switch the active league for this view."}>
      {label}
      <select
        value={leagueId || ""}
        onChange={(e) => setLeagueId(e.target.value)}
        disabled={!hasLeagues}
        aria-label="Select league"
      >
        {!hasLeagues ? (
          <option value="">No leagues</option>
        ) : (
          memberships.map((m) => {
            const id = (m?.leagueId || "").trim();
            const role = (m?.role || "").trim();
            if (!id) return null;
            return (
              <option key={id} value={id}>
                {role ? `${id} (${role})` : id}
              </option>
            );
          })
        )}
      </select>
    </label>
  );
}
