export default function TopNav({ tab, setTab, me, leagueId, setLeagueId }) {
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const email = me?.email || "";
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const tabLabels = {
    home: "Home",
    calendar: "Calendar",
    offers: "Offers",
    manage: "Manage",
    admin: "Admin",
    help: "Help"
  };
  const currentLabel = tabLabels[tab] || "Home";

  function pickLeague(id) {
    setLeagueId(id);
  }

  const hasLeagues = memberships.length > 0;

  return (
    <header className="topnav">
      <div className="topnav__inner">
        <div className="navPresence" aria-live="polite">
          <div className="navPresence__title">Sports Scheduler</div>
        </div>

        <div className="topnav__controls">
          <nav className="tabs" aria-label="Primary">
            <button
              className={tab === "home" ? "tab tab--active" : "tab"}
              onClick={() => setTab("home")}
              disabled={!hasLeagues}
              title="Role-based landing dashboard."
              aria-current={tab === "home" ? "page" : undefined}
            >
              Home
            </button>
            <button
              className={tab === "calendar" ? "tab tab--active" : "tab"}
              onClick={() => setTab("calendar")}
              disabled={!hasLeagues}
              title="Browse and accept offers on the calendar."
              aria-current={tab === "calendar" ? "page" : undefined}
            >
              Calendar
            </button>
            <button
              className={tab === "offers" ? "tab tab--active" : "tab"}
              onClick={() => setTab("offers")}
              disabled={!hasLeagues}
              title="Create a new game offer."
              aria-current={tab === "offers" ? "page" : undefined}
            >
              Create Game Offer
            </button>
            <button
              className={tab === "manage" ? "tab tab--active" : "tab"}
              onClick={() => setTab("manage")}
              disabled={!hasLeagues}
              aria-current={tab === "manage" ? "page" : undefined}
            >
              League Management
            </button>
            {isGlobalAdmin ? (
              <button
                className={tab === "admin" ? "tab tab--active" : "tab"}
                onClick={() => setTab("admin")}
                aria-current={tab === "admin" ? "page" : undefined}
              >
                Admin
              </button>
            ) : null}
            <button
              className={tab === "help" ? "tab tab--active" : "tab"}
              onClick={() => setTab("help")}
              aria-current={tab === "help" ? "page" : undefined}
            >
              Help
            </button>
          </nav>

          <div className="topnav__account">
            <div className="whoami" title={email}>
              {email || "Signed in"}
            </div>
            <div className="control control--league">
              <label>League</label>
              <select
                className="leagueSelect"
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
          </div>
        </div>
      </div>
    </header>
  );
}
