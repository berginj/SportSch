import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import { LEAGUE_HEADER_NAME } from "../lib/constants";

const ROLE_OPTIONS = [
  { value: "Coach", label: "Coach" },
  { value: "Viewer", label: "Read-only viewer" },
];

export default function AccessPage({ me, leagueId, setLeagueId }) {
  const [leagues, setLeagues] = useState([]);
  const [role, setRole] = useState("Coach");
  const [notes, setNotes] = useState("");
  const [mine, setMine] = useState([]);
  const [mineLoaded, setMineLoaded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [ok, setOk] = useState("");

  const signedIn = (me?.userId || "UNKNOWN") !== "UNKNOWN";
  const email = me?.email || "";
  const autoSubmitted = useRef(false);
  const autoRequested = useRef(false);

  const applyFiltersFromUrl = useCallback(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const desiredLeague = (params.get("leagueId") || "").trim();
    const desiredRole = (params.get("role") || "").trim();
    if (desiredLeague && desiredLeague !== leagueId) {
      setLeagueId(desiredLeague);
    }
    if (desiredRole) {
      const normalized = desiredRole === "Viewer" ? "Viewer" : "Coach";
      setRole(normalized);
    }
  }, [leagueId, setLeagueId]);

  const accessIntent = useMemo(() => {
    if (typeof window === "undefined") return null;
    const params = new URLSearchParams(window.location.search);
    const desiredLeague = (params.get("leagueId") || "").trim();
    const requestedRole = (params.get("requestRole") || "").trim();
    const autoSubmit = ["1", "true", "yes"].includes((params.get("autoSubmit") || "").toLowerCase());
    return { desiredLeague, requestedRole, autoSubmit };
  }, []);

  const selectedLeague = useMemo(() => {
    const id = (leagueId || "").trim();
    if (!id) return null;
    return leagues.find((l) => (l.leagueId || "") === id) || null;
  }, [leagues, leagueId]);

  async function refresh() {
    setErr("");
    try {
      const [ls, my] = await Promise.all([
        apiFetch("/api/leagues"),
        signedIn ? apiFetch("/api/accessrequests/mine") : Promise.resolve([]),
      ]);
      setLeagues(Array.isArray(ls) ? ls : []);
      setMine(Array.isArray(my) ? my : []);
      setMineLoaded(true);
    } catch (e) {
      setErr(e?.message || "Failed to load.");
      setMineLoaded(true);
    }
  }

  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signedIn]);

  useEffect(() => {
    applyFiltersFromUrl();
  }, [applyFiltersFromUrl]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const onPopState = () => applyFiltersFromUrl();
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, [applyFiltersFromUrl]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (leagueId) params.set("leagueId", leagueId);
    else params.delete("leagueId");
    if (role) params.set("role", role);
    else params.delete("role");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [leagueId, role]);

  useEffect(() => {
    if (!accessIntent) return;
    if (accessIntent.desiredLeague && !leagueId) {
      setLeagueId(accessIntent.desiredLeague);
    }
    if (accessIntent.requestedRole) {
      const normalized = accessIntent.requestedRole === "Viewer" ? "Viewer" : "Coach";
      setRole(normalized);
    }
  }, [accessIntent, leagueId, setLeagueId]);

  useEffect(() => {
    // Pick the first active league if none selected and we have a list.
    if (!leagueId && leagues.length > 0) setLeagueId(leagues[0].leagueId);
  }, [leagueId, leagues, setLeagueId]);

  useEffect(() => {
    if (!signedIn || !mineLoaded || autoRequested.current) return;
    if (accessIntent?.autoSubmit) return;
    if (!leagueId || leagues.length === 0) return;
    if (mine.length > 0) return;
    autoRequested.current = true;
    submitRequest(role).catch(() => {});
  }, [signedIn, mineLoaded, mine, leagueId, leagues.length, role, accessIntent]);

  async function submitRequest(roleOverride) {
    setErr("");
    setOk("");
    if (!signedIn) {
      setErr("You must sign in before requesting access.");
      return;
    }

    const id = (leagueId || "").trim();
    if (!id) {
      setErr("Choose a league.");
      return;
    }

    const requestedRole = roleOverride || role;

    setBusy(true);
    try {
      await apiFetch("/api/accessrequests", {
        method: "POST",
        headers: { "Content-Type": "application/json", [LEAGUE_HEADER_NAME]: id },
        body: JSON.stringify({ requestedRole, notes }),
      });
      setOk("Request submitted. An admin will review it.");
      setNotes("");
      await refresh();
    } catch (e) {
      setErr(e?.message || "Request failed.");
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    if (!accessIntent || autoSubmitted.current) return;
    if (!accessIntent.autoSubmit || !signedIn) return;
    if (!leagueId) return;

    const requestedRole = accessIntent.requestedRole === "Viewer" ? "Viewer" : "Coach";
    autoSubmitted.current = true;
    submitRequest(requestedRole).catch(() => {});

    if (typeof window !== "undefined") {
      const url = new URL(window.location.href);
      url.searchParams.delete("autoSubmit");
      url.searchParams.delete("requestRole");
      window.history.replaceState({}, "", url.pathname + url.search);
    }
  }, [accessIntent, leagueId, signedIn]);

  return (
    <div className="page">
      <h1>Access</h1>

      {!signedIn ? (
        <div className="card">
          <h2>Sign in</h2>
          <p>
            You&apos;re not signed in. Use Azure AD login, then return here.
          </p>
          <a className="btn" href="/.auth/login/aad">
            Sign in with Microsoft
          </a>
          <p className="muted">
            If you signed in already and still see this, refresh the page.
          </p>
        </div>
      ) : (
        <div className="card">
          <h2>Request access</h2>
          <div className="muted">Signed in as {email || me?.userId}</div>

          <div className="formGrid">
            <label>
              League
              <select value={leagueId || ""} onChange={(e) => setLeagueId(e.target.value)}>
                {leagues.map((l) => (
                  <option key={l.leagueId} value={l.leagueId}>
                    {l.name ? `${l.name} (${l.leagueId})` : l.leagueId}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Requested role
              <select value={role} onChange={(e) => setRole(e.target.value)}>
                {ROLE_OPTIONS.map((r) => (
                  <option key={r.value} value={r.value}>
                    {r.label}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <label>
            Message (optional)
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Tell the admin who you are and why you need access."
              rows={4}
            />
          </label>

          <div className="row">
            <button className="btn" onClick={submitRequest} disabled={busy}>
              {busy ? "Submitting..." : "Submit request"}
            </button>
            {selectedLeague ? (
              <div className="muted">Timezone: {selectedLeague.timezone}</div>
            ) : null}
          </div>

          {err ? <div className="err">{err}</div> : null}
          {ok ? <div className="ok">{ok}</div> : null}
        </div>
      )}

      <div className="card">
        <h2>My requests</h2>
        {mine.length === 0 ? (
          <div className="muted">No access requests yet.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>League</th>
                  <th>Role</th>
                  <th>Status</th>
                  <th>Updated</th>
                </tr>
              </thead>
              <tbody>
                {mine.map((r) => (
                  <tr key={`${r.leagueId}-${r.userId}`}
                    >
                    <td>{r.leagueId}</td>
                    <td>{r.requestedRole}</td>
                    <td>{r.status}</td>
                    <td>{r.updatedUtc ? new Date(r.updatedUtc).toLocaleString() : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card">
        <h2>What happens next</h2>
        <ul>
          <li>An admin reviews your request and either approves or denies it.</li>
          <li>
            If approved, refresh the app. Your league should appear in the league dropdown.
          </li>
          <li>
            If denied, submit another request with more context.
          </li>
        </ul>
      </div>
    </div>
  );
}
