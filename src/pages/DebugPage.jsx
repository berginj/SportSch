import { useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

export default function DebugPage({ leagueId }) {
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [memberships, setMemberships] = useState([]);

  async function load() {
    if (!leagueId) {
      setLoading(false);
      setMemberships([]);
      setErr("Select a league to load memberships.");
      return;
    }
    setLoading(true);
    setErr("");
    try {
      const data = await apiFetch("/api/memberships");
      setMemberships(Array.isArray(data) ? data : []);
    } catch (e) {
      setErr(e?.message || "Failed to load memberships");
      setMemberships([]);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  return (
    <div className="card">
      <h2>Debug: memberships table</h2>
      <p className="muted">
        Data comes from <code>GameSwapMemberships</code> for the active league via <code>/api/memberships</code>.
      </p>

      <div className="row gap-3 row--wrap mb-2">
        <button className="btn" onClick={load} disabled={loading}>
          Refresh
        </button>
      </div>

      {err && <div className="error">{err}</div>}
      {loading ? (
        <div className="muted">Loading...</div>
      ) : memberships.length === 0 ? (
        <div className="muted">No memberships returned for this league.</div>
      ) : (
        <div className="tableWrap">
          <table className="table">
            <thead>
              <tr>
                <th>User</th>
                <th>Role</th>
                <th>Division</th>
                <th>Team</th>
              </tr>
            </thead>
            <tbody>
              {memberships.map((m) => (
                <tr key={`${m.userId}-${m.role}-${m.team?.teamId || ""}`}>
                  <td>
                    <div className="font-semibold">{m.email || m.userId}</div>
                    <div className="muted text-xs">{m.userId}</div>
                  </td>
                  <td>{m.role || ""}</td>
                  <td>{m.team?.division || ""}</td>
                  <td>{m.team?.teamId || ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <details className="mt-4">
        <summary>Raw JSON</summary>
        <pre className="codeblock">{JSON.stringify(memberships, null, 2)}</pre>
      </details>
    </div>
  );
}
