import { useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

const MEMBERSHIP_ROLES = ["LeagueAdmin", "Coach", "Viewer"];

export default function DebugPage({ leagueId }) {
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [memberships, setMemberships] = useState([]);
  const [globalAdmins, setGlobalAdmins] = useState([]);
  const [globalErr, setGlobalErr] = useState("");
  const [globalOk, setGlobalOk] = useState("");
  const [globalBusy, setGlobalBusy] = useState(false);
  const [globalDraft, setGlobalDraft] = useState({ userId: "", email: "" });

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

  async function loadGlobalAdmins() {
    setGlobalErr("");
    try {
      const data = await apiFetch("/api/admin/globaladmins");
      setGlobalAdmins(Array.isArray(data) ? data : []);
    } catch (e) {
      setGlobalErr(e?.message || "Failed to load global admins.");
      setGlobalAdmins([]);
    }
  }

  async function addGlobalAdmin() {
    setGlobalErr("");
    setGlobalOk("");
    const userId = (globalDraft.userId || "").trim();
    const email = (globalDraft.email || "").trim();
    if (!userId) {
      setGlobalErr("User ID is required.");
      return;
    }
    setGlobalBusy(true);
    try {
      await apiFetch("/api/admin/globaladmins", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ userId, email }),
      });
      setGlobalOk(`Granted global admin to ${userId}.`);
      setGlobalDraft({ userId: "", email: "" });
      await loadGlobalAdmins();
    } catch (e) {
      setGlobalErr(e?.message || "Failed to grant global admin.");
    } finally {
      setGlobalBusy(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    loadGlobalAdmins();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="page">
      <div className="card">
        <h2>Debug: global admins</h2>
        <p className="muted">
          Data comes from <code>GameSwapGlobalAdmins</code> via <code>/api/admin/globaladmins</code>.
        </p>

        <div className="formGrid">
          <label>
            User ID
            <input
              value={globalDraft.userId}
              onChange={(e) => setGlobalDraft((prev) => ({ ...prev, userId: e.target.value }))}
              placeholder="aad|..."
            />
          </label>
          <label>
            Email (optional)
            <input
              value={globalDraft.email}
              onChange={(e) => setGlobalDraft((prev) => ({ ...prev, email: e.target.value }))}
              placeholder="name@domain.com"
            />
          </label>
        </div>

        <div className="row gap-3 row--wrap mb-2">
          <button className="btn" onClick={addGlobalAdmin} disabled={globalBusy}>
            {globalBusy ? "Saving..." : "Grant global admin"}
          </button>
          <button className="btn btn--ghost" onClick={loadGlobalAdmins} disabled={globalBusy}>
            Refresh list
          </button>
        </div>

        {globalErr && <div className="error">{globalErr}</div>}
        {globalOk && <div className="ok">{globalOk}</div>}
        {globalAdmins.length === 0 ? (
          <div className="muted">No global admins returned.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>User</th>
                  <th>Created</th>
                </tr>
              </thead>
              <tbody>
                {globalAdmins.map((admin) => (
                  <tr key={admin.userId}>
                    <td>
                      <div className="font-semibold">{admin.email || admin.userId}</div>
                      <div className="muted text-xs">{admin.userId}</div>
                    </td>
                    <td>{admin.createdUtc ? new Date(admin.createdUtc).toLocaleString() : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

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
    </div>
  );
}
