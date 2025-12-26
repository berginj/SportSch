import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";

function buildLoginUrl(returnTo) {
  if (typeof window === "undefined") return "/.auth/login/aad";
  const target = returnTo || window.location.pathname;
  return `/.auth/login/aad?post_login_redirect_uri=${encodeURIComponent(target)}`;
}

export default function PublicLanding() {
  const [slots, setSlots] = useState([]);
  const [leagues, setLeagues] = useState([]);
  const [leagueId, setLeagueId] = useState("");
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");

  const selectedLeague = useMemo(() => {
    if (!leagueId) return null;
    return leagues.find((l) => l.leagueId === leagueId) || null;
  }, [leagueId, leagues]);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      setErr("");
      try {
        const [recentSlots, leagueList] = await Promise.all([
          apiFetch("/api/public/slots?limit=6"),
          apiFetch("/api/leagues"),
        ]);
        if (cancelled) return;
        setSlots(Array.isArray(recentSlots) ? recentSlots : []);
        const list = Array.isArray(leagueList) ? leagueList : [];
        setLeagues(list);
        if (!leagueId && list.length > 0) setLeagueId(list[0].leagueId || "");
      } catch (e) {
        if (!cancelled) setErr(e?.message || "Failed to load.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    load();
    return () => {
      cancelled = true;
    };
  }, []);

  function handleCreateAccount() {
    if (!leagueId) {
      setErr("Choose a league to continue.");
      return;
    }

    if (typeof window === "undefined") return;
    const url = new URL(window.location.href);
    url.searchParams.set("leagueId", leagueId);
    url.searchParams.set("requestRole", "Viewer");
    url.searchParams.set("autoSubmit", "1");
    const returnTo = `${url.pathname}${url.search}`;
    window.location.href = buildLoginUrl(returnTo);
  }

  return (
    <div className="appShell">
      <div className="card" style={{ maxWidth: 880, margin: "20px auto" }}>
        <h1>GameSwap</h1>
        <p className="muted" style={{ marginTop: 6 }}>
          Browse recent open game offers and create a viewer account to follow along.
        </p>

        {err ? <div className="callout callout--error">{err}</div> : null}

        <div className="grid2" style={{ marginTop: 16 }}>
          <div className="card" style={{ margin: 0 }}>
            <div className="cardTitle">Create a viewer account</div>
            <div className="stack">
              <label>
                League
                <select value={leagueId} onChange={(e) => setLeagueId(e.target.value)}>
                  {leagues.map((l) => (
                    <option key={l.leagueId} value={l.leagueId}>
                      {l.name ? `${l.name} (${l.leagueId})` : l.leagueId}
                    </option>
                  ))}
                </select>
              </label>
              {selectedLeague ? (
                <div className="muted">Timezone: {selectedLeague.timezone}</div>
              ) : null}
              <button className="btn primary" onClick={handleCreateAccount}>
                Create viewer account
              </button>
              <div className="subtle">
                Viewer access lets you see schedules without requesting or offering games.
              </div>
            </div>
          </div>

          <div className="card" style={{ margin: 0 }}>
            <div className="cardTitle">Recent open game offers</div>
            {loading ? (
              <div className="muted">Loading…</div>
            ) : slots.length === 0 ? (
              <div className="muted">No open offers yet.</div>
            ) : (
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>League</th>
                      <th>Date</th>
                      <th>Time</th>
                      <th>Field</th>
                    </tr>
                  </thead>
                  <tbody>
                    {slots.map((slot) => (
                      <tr key={slot.slotId}>
                        <td>{slot.leagueName || slot.leagueId}</td>
                        <td>{slot.gameDate}</td>
                        <td>
                          {slot.startTime}–{slot.endTime}
                        </td>
                        <td>{slot.displayName || slot.fieldKey}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
