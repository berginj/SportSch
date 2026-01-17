import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import NotificationBell from "./NotificationBell";

export default function TopNav({ tab, setTab, me, leagueId, setLeagueId }) {
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const email = me?.email || "";
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const [globalLeagues, setGlobalLeagues] = useState([]);
  const [globalErr, setGlobalErr] = useState("");

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

  return (
    <header className="topnav">
      <div className="topnav__inner">
        <nav className="topnav__nav" aria-label="Primary navigation">
          <button
            className={tab === "home" ? "tab tab--active" : "tab"}
            onClick={() => setTab("home")}
            disabled={!hasLeagues}
            title="Home dashboard"
            aria-current={tab === "home" ? "page" : undefined}
          >
            Home
          </button>
          <button
            className={tab === "calendar" ? "tab tab--active" : "tab"}
            onClick={() => setTab("calendar")}
            disabled={!hasLeagues}
            title="Calendar view"
            aria-current={tab === "calendar" ? "page" : undefined}
          >
            Calendar
          </button>
          <button
            className={tab === "manage" ? "tab tab--active" : "tab"}
            onClick={goManage}
            disabled={!hasLeagues}
            title="League management"
            aria-current={tab === "manage" ? "page" : undefined}
          >
            Manage
          </button>
          {isGlobalAdmin && (
            <button
              className={tab === "admin" ? "tab tab--active" : "tab"}
              onClick={() => setTab("admin")}
              title="Admin panel"
              aria-current={tab === "admin" ? "page" : undefined}
            >
              Admin
            </button>
          )}
          {isGlobalAdmin && (
            <button
              className={tab === "debug" ? "tab tab--active" : "tab"}
              onClick={() => setTab("debug")}
              title="Debug tools"
              aria-current={tab === "debug" ? "page" : undefined}
            >
              Debug
            </button>
          )}
          <button
            className={tab === "help" ? "tab tab--active" : "tab"}
            onClick={() => setTab("help")}
            title="Help and documentation"
            aria-current={tab === "help" ? "page" : undefined}
          >
            Help
          </button>
        </nav>

        <div className="topnav__account">
          <span className="topnav__user" title={email}>
            {email || "Signed in"}
          </span>
          <NotificationBell leagueId={leagueId} />
          <div className="topnav__league">
            <select
              className="topnav__league-select"
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
            {globalErr && <div className="topnav__error">{globalErr}</div>}
          </div>
        </div>
      </div>
    </header>
  );
}
