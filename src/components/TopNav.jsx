import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import NotificationBell from "./NotificationBell";

export default function TopNav({ tab, setTab, me, leagueId, setLeagueId }) {
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const email = me?.email || "";
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const [isCollapsed, setIsCollapsed] = useState(() => {
    if (typeof window === "undefined") return true;
    const saved = window.localStorage.getItem("topnavCollapsed");
    if (saved === "true") return true;
    if (saved === "false") return false;
    return true;
  });
  const [globalLeagues, setGlobalLeagues] = useState([]);
  const [globalErr, setGlobalErr] = useState("");
  const tabLabels = {
    home: "Home",
    calendar: "Calendar",
    schedule: "Schedule",
    offers: "Offers",
    manage: "Manage",
    admin: "Admin",
    debug: "Debug",
    help: "Help"
  };
  const currentLabel = tabLabels[tab] || "Home";

  function pickLeague(id) {
    setLeagueId(id);
  }

  function goManage() {
    if (typeof window !== "undefined") {
      const params = new URLSearchParams(window.location.search);
      params.set("manageTab", "commissioner");
      const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
      window.history.replaceState({}, "", next);
    }
    setTab("manage");
  }

  useEffect(() => {
    if (!isGlobalAdmin) return;
    let cancelled = false;
    (async () => {
      setGlobalErr("");
      try {
        const list = await apiFetch("/api/global/leagues");
        if (!cancelled) setGlobalLeagues(Array.isArray(list) ? list : []);
      } catch (e) {
        if (!cancelled) {
          setGlobalErr(e?.message || "Failed to load leagues.");
          setGlobalLeagues([]);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [isGlobalAdmin]);

  const roleByLeague = useMemo(() => {
    const map = new Map();
    for (const m of memberships) {
      const id = (m?.leagueId || "").trim();
      const role = (m?.role || "").trim();
      if (id) map.set(id, role);
    }
    return map;
  }, [memberships]);

  const leagueOptions = useMemo(() => {
    if (isGlobalAdmin && globalLeagues.length) {
      return globalLeagues.map((l) => {
        const id = (l?.leagueId || "").trim();
        const name = (l?.name || "").trim();
        const role = roleByLeague.get(id) || "";
        const labelText = name ? `${name} (${id})${role ? ` — ${role}` : ""}` : `${id}${role ? ` — ${role}` : ""}`;
        return { id, label: labelText };
      }).filter((x) => x.id);
    }
    return memberships.map((m) => {
      const id = (m?.leagueId || "").trim();
      const role = (m?.role || "").trim();
      return { id, label: role ? `${id} (${role})` : id };
    }).filter((x) => x.id);
  }, [isGlobalAdmin, globalLeagues, memberships, roleByLeague]);

  const hasLeagues = leagueOptions.length > 0;

  useEffect(() => {
    if (typeof window === "undefined") return;
    window.localStorage.setItem("topnavCollapsed", isCollapsed ? "true" : "false");
  }, [isCollapsed]);

  return (
    <header className="topnav">
      <div className="topnav__inner">
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
            {!isCollapsed ? (
              <>
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
                  className={tab === "manage" ? "tab tab--active" : "tab"}
                  onClick={goManage}
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
                {isGlobalAdmin ? (
                  <button
                    className={tab === "debug" ? "tab tab--active" : "tab"}
                    onClick={() => setTab("debug")}
                    aria-current={tab === "debug" ? "page" : undefined}
                  >
                    Debug
                  </button>
                ) : null}
                <button
                  className={tab === "help" ? "tab tab--active" : "tab"}
                  onClick={() => setTab("help")}
                  aria-current={tab === "help" ? "page" : undefined}
                >
                  Help
                </button>
              </>
            ) : null}
            <button
              className="tab"
              onClick={() => setIsCollapsed((prev) => !prev)}
              title={isCollapsed ? "Expand navigation" : "Minimize navigation"}
              aria-label={isCollapsed ? "Expand navigation" : "Minimize navigation"}
            >
              {isCollapsed ? "[+]" : "[-]"}
            </button>
          </nav>

          {!hasLeagues ? (
            <div className="navHint">Select a league to unlock tabs and actions.</div>
          ) : null}

          <div className="topnav__account">
            <div className="whoami" title={email}>
              {email || "Signed in"}
            </div>
            <NotificationBell leagueId={leagueId} />
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
                  leagueOptions.map((opt) => (
                    <option key={opt.id} value={opt.id}>
                      {opt.label}
                    </option>
                  ))
                )}
              </select>
              {globalErr ? <div className="muted text-xs">{globalErr}</div> : null}
            </div>
          </div>
        </div>
      </div>
    </header>
  );
}
