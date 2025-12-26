export default function TopNav({ tab, setTab, me, leagueId, setLeagueId }) {
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const email = me?.email || "";
  const isGlobalAdmin = !!me?.isGlobalAdmin;

  function pickLeague(id) {
    setLeagueId(id);
  }

  const hasLeagues = memberships.length > 0;

  return (
    <header className="topnav">
      <div className="topnav__inner">
        <div className="brand">
          <div className="brand__title">GameSwap</div>
          <div className="brand__sub">League slot swaps &amp; approvals</div>
        </div>

        <div className="topnav__controls">
          <div className="control">
            <label>League</label>
            <select
              value={leagueId || ""}
              onChange={(e) => pickLeague(e.target.value)}
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
          </div>

          <nav className="tabs" aria-label="Primary">
            <button
              className={tab === "calendar" ? "tab tab--active" : "tab"}
              onClick={() => setTab("calendar")}
              disabled={!hasLeagues}
              title="Browse and accept offers on the calendar."
            >
              Calendar
            </button>
            <button
              className={tab === "offers" ? "tab tab--active" : "tab"}
              onClick={() => setTab("offers")}
              disabled={!hasLeagues}
              title="Create a new game offer."
            >
              Create Game Offer
            </button>
            <button
              className={tab === "manage" ? "tab tab--active" : "tab"}
              onClick={() => setTab("manage")}
              disabled={!hasLeagues}
            >
              League Management
            </button>
            {isGlobalAdmin ? (
              <button
                className={tab === "admin" ? "tab tab--active" : "tab"}
                onClick={() => setTab("admin")}
              >
                Admin
              </button>
            ) : null}
            <button
              className={tab === "help" ? "tab tab--active" : "tab"}
              onClick={() => setTab("help")}
            >
              Help
            </button>
          </nav>

          <div className="whoami" title={email}>
            {email || "Signed in"}
          </div>
        </div>
      </div>
    </header>
  );
}
