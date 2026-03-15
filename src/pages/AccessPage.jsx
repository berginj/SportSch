import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { LEAGUE_HEADER_NAME } from "../lib/constants";
import { trackEvent } from "../lib/telemetry";

const ROLE_OPTIONS = [
  { value: "Coach", label: "Coach" },
  { value: "LeagueAdmin", label: "League admin" },
  { value: "Viewer", label: "Read-only viewer" },
];
const NEW_LEAGUE_VALUE = "NEW_LEAGUE";
const NEW_LEAGUE_LABEL = "New league (enter details in message)";

function getAccessStatusBadgeClass(status) {
  const value = (status || "").trim().toLowerCase();
  if (value === "approved") return "statusBadge status-confirmed";
  if (value === "denied") return "statusBadge status-cancelled";
  return "statusBadge status-open";
}

export default function AccessPage({ me, leagueId, setLeagueId }) {
  const [leagues, setLeagues] = useState([]);
  const [role, setRole] = useState("Coach");
  const [notes, setNotes] = useState("");
  const [requestLeagueId, setRequestLeagueId] = useState(leagueId || "");
  const [mine, setMine] = useState([]);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [ok, setOk] = useState("");

  const signedIn = (me?.userId || "UNKNOWN") !== "UNKNOWN";
  const email = me?.email || "";
  const requestSummary = useMemo(() => ({
    total: mine.length,
    pending: mine.filter((request) => (request?.status || "").trim() === "Pending").length,
    approved: mine.filter((request) => (request?.status || "").trim() === "Approved").length,
  }), [mine]);

  const applyFiltersFromUrl = useCallback(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const desiredLeague = (params.get("leagueId") || "").trim();
    const desiredRole = (params.get("role") || "").trim();
    if (desiredLeague) {
      setRequestLeagueId((prev) => (desiredLeague !== prev ? desiredLeague : prev));
      if (desiredLeague === NEW_LEAGUE_VALUE) {
        setLeagueId("");
      } else {
        setLeagueId((prev) => (desiredLeague !== prev ? desiredLeague : prev));
      }
    }
    if (desiredRole) {
      const normalized = desiredRole === "Viewer" ? "Viewer" : "Coach";
      setRole((prev) => (normalized !== prev ? normalized : prev));
    }
  }, [setLeagueId]);

  const accessIntent = useMemo(() => {
    if (typeof window === "undefined") return null;
    const params = new URLSearchParams(window.location.search);
    const desiredLeague = (params.get("leagueId") || "").trim();
    const requestedRole = (params.get("requestRole") || "").trim();
    return { desiredLeague, requestedRole };
  }, []);

  const refresh = useCallback(async () => {
    setErr("");
    try {
      const [ls, my] = await Promise.all([
        apiFetch("/api/leagues"),
        signedIn ? apiFetch("/api/accessrequests/mine") : Promise.resolve([]),
      ]);
      setLeagues(Array.isArray(ls) ? ls : []);
      setMine(Array.isArray(my) ? my : []);
    } catch (e) {
      setErr(e?.message || "Failed to load.");
    }
  }, [signedIn]);

  const submitRequest = useCallback(async (roleOverride, source = "manual") => {
    setErr("");
    setOk("");
    if (!signedIn) {
      setErr("You must sign in before requesting access.");
      return;
    }

    const id = (requestLeagueId || "").trim();
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
      trackEvent("ui_access_request_submit", {
        leagueId: id,
        requestedRole,
        source,
      });
      setNotes("");
      await refresh();
    } catch (e) {
      setErr(e?.message || "Request failed.");
    } finally {
      setBusy(false);
    }
  }, [notes, refresh, requestLeagueId, role, signedIn]);

  useEffect(() => {
    refresh();
  }, [refresh]);

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
    params.delete("autoSubmit");
    params.delete("requestRole");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [leagueId, role]);

  useEffect(() => {
    if (!accessIntent) return;
    if (accessIntent.desiredLeague) {
      setRequestLeagueId(accessIntent.desiredLeague);
      if (accessIntent.desiredLeague === NEW_LEAGUE_VALUE) {
        setLeagueId("");
      } else {
        setLeagueId(accessIntent.desiredLeague);
      }
    }
    if (accessIntent.requestedRole) {
      const normalized = accessIntent.requestedRole === "Viewer" ? "Viewer" : "Coach";
      setRole(normalized);
    }
  }, [accessIntent, setLeagueId]);

  useEffect(() => {
    // Pick the first active league if none selected and we have a list.
    if (!leagueId && leagues.length > 0 && !requestLeagueId) {
      setRequestLeagueId(leagues[0].leagueId);
      setLeagueId(leagues[0].leagueId);
    }
  }, [leagueId, leagues, setLeagueId, requestLeagueId]);

  useEffect(() => {
    if (!leagueId && requestLeagueId && requestLeagueId !== NEW_LEAGUE_VALUE) {
      setRequestLeagueId("");
    }
  }, [leagueId, requestLeagueId]);

  useEffect(() => {
    if (leagueId && leagueId !== requestLeagueId && requestLeagueId !== NEW_LEAGUE_VALUE) {
      setRequestLeagueId(leagueId);
    }
  }, [leagueId, requestLeagueId]);

  return (
    <div className="page">
      <div className="card">
        <div className="card__header">
          <div className="h1">Access</div>
          <div className="subtle">Request league access, track status, and reapply with context when needed.</div>
        </div>
        <div className="layoutStatRow">
          <div className="layoutStat">
            <div className="layoutStat__value">{requestSummary.total}</div>
            <div className="layoutStat__label">Requests</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{requestSummary.pending}</div>
            <div className="layoutStat__label">Pending</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{requestSummary.approved}</div>
            <div className="layoutStat__label">Approved</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{leagues.length}</div>
            <div className="layoutStat__label">Available leagues</div>
          </div>
        </div>
      </div>

      {!signedIn ? (
        <div className="card">
          <div className="card__header">
            <h2>Sign in</h2>
            <div className="subtle">Authentication is required before an access request can be created.</div>
          </div>
          <p>
            You&apos;re not signed in. Choose a sign-in method below.
          </p>
          <div className="stack gap-2">
            <a className="btn" href="/.auth/login/aad">
              Sign in with Microsoft
            </a>
            <a className="btn" href="/.auth/login/google">
              Sign in with Google
            </a>
          </div>
          <p className="muted">
            If you signed in already and still see this, refresh the page.
          </p>
        </div>
      ) : (
        <div className="card">
          <div className="card__header">
            <h2>Request access</h2>
            <div className="subtle">Signed in as {email || me?.userId}</div>
          </div>

          <div className="controlBand">
            <div className="formGrid">
            <label>
              League
              <select
                value={requestLeagueId || ""}
                onChange={(e) => {
                  const next = e.target.value;
                  setRequestLeagueId(next);
                  if (next === NEW_LEAGUE_VALUE) {
                    setLeagueId("");
                  } else {
                    setLeagueId(next);
                  }
                }}
              >
                <option value={NEW_LEAGUE_VALUE}>{NEW_LEAGUE_LABEL}</option>
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
          </div>
          <div className="muted">
            Global admin access is assigned from the global admin management page only.
          </div>

          <label>
            Message (optional)
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Tell the admin who you are and why you need access. For new leagues, include the league name, region, and any admin contact info."
              rows={4}
            />
          </label>

          <div className="row">
            <button className="btn" onClick={() => submitRequest()} disabled={busy}>
              {busy ? "Submitting..." : "Submit request"}
            </button>
          </div>

          {err ? <div className="callout callout--error">{err}</div> : null}
          {ok ? <div className="callout callout--ok">{ok}</div> : null}
        </div>
      )}

      <div className="card">
        <div className="card__header">
          <h2>My requests</h2>
          <div className="subtle">Your request history for the current account.</div>
        </div>
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
                    <td><span className={getAccessStatusBadgeClass(r.status)}>{r.status}</span></td>
                    <td>{r.updatedUtc ? new Date(r.updatedUtc).toLocaleString() : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card">
        <div className="card__header">
          <h2>What happens next</h2>
          <div className="subtle">The review cycle after you submit access.</div>
        </div>
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
