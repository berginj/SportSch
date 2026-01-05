import { useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

const MEMBERSHIP_ROLES = ["LeagueAdmin", "Coach", "Viewer"];

export default function DebugPage({ leagueId, me }) {
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [memberships, setMemberships] = useState([]);
  const [globalAdmins, setGlobalAdmins] = useState([]);
  const [globalErr, setGlobalErr] = useState("");
  const [globalOk, setGlobalOk] = useState("");
  const [globalBusy, setGlobalBusy] = useState(false);
  const [globalDraft, setGlobalDraft] = useState({ userId: "", email: "" });
  const [globalLeagues, setGlobalLeagues] = useState([]);
  const [debugData, setDebugData] = useState(null);
  const [debugErr, setDebugErr] = useState("");
  const [debugLoading, setDebugLoading] = useState(false);
  const [wipeConfirm, setWipeConfirm] = useState("");
  const [wipeBusy, setWipeBusy] = useState(false);
  const [wipeOk, setWipeOk] = useState("");
  const [wipeErr, setWipeErr] = useState("");
  const [deleteLeagueId, setDeleteLeagueId] = useState("");
  const [deleteConfirm, setDeleteConfirm] = useState("");
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [deleteErr, setDeleteErr] = useState("");
  const [deleteOk, setDeleteOk] = useState("");
  const [membershipDraft, setMembershipDraft] = useState({
    userId: "",
    email: "",
    leagueId: "",
    role: "Viewer",
    division: "",
    teamId: "",
  });
  const [membershipBusy, setMembershipBusy] = useState(false);
  const [membershipErr, setMembershipErr] = useState("");
  const [membershipOk, setMembershipOk] = useState("");

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
    if (!isGlobalAdmin) {
      setGlobalErr("Global admin access required.");
      setGlobalAdmins([]);
      return;
    }
    setGlobalErr("");
    try {
      const data = await apiFetch("/api/admin/globaladmins");
      setGlobalAdmins(Array.isArray(data) ? data : []);
    } catch (e) {
      setGlobalErr(e?.message || "Failed to load global admins.");
      setGlobalAdmins([]);
    }
  }

  async function loadGlobalLeagues() {
    if (!isGlobalAdmin) {
      setGlobalLeagues([]);
      return;
    }
    try {
      const data = await apiFetch("/api/global/leagues");
      const list = Array.isArray(data) ? data : [];
      setGlobalLeagues(list);
      if (!deleteLeagueId && list.length > 0) setDeleteLeagueId(list[0].leagueId);
      if (!membershipDraft.leagueId && list.length > 0) {
        setMembershipDraft((prev) => ({ ...prev, leagueId: list[0].leagueId }));
      }
    } catch {
      setGlobalLeagues([]);
    }
  }

  async function loadDebugData() {
    if (!leagueId) {
      setDebugErr("Select a league to load raw table data.");
      setDebugData(null);
      return;
    }
    setDebugLoading(true);
    setDebugErr("");
    try {
      const data = await apiFetch(`/api/admin/debug/league/${encodeURIComponent(leagueId)}`);
      setDebugData(data || null);
    } catch (e) {
      setDebugErr(e?.message || "Failed to load debug table data.");
      setDebugData(null);
    } finally {
      setDebugLoading(false);
    }
  }

  async function wipeLeagueData() {
    if (!isGlobalAdmin) {
      setWipeErr("Global admin access required.");
      return;
    }
    if (!leagueId) {
      setWipeErr("Select a league to wipe.");
      return;
    }
    if (wipeConfirm.trim().toUpperCase() !== "WIPE") {
      setWipeErr("Type WIPE to confirm.");
      return;
    }
    setWipeBusy(true);
    setWipeErr("");
    setWipeOk("");
    try {
      await apiFetch("/api/admin/wipe", {
        method: "POST",
        headers: { "Content-Type": "application/json", "x-league-id": leagueId },
        body: JSON.stringify({ confirm: "WIPE" }),
      });
      setWipeOk("League tables wiped (global admins preserved).");
      setWipeConfirm("");
      await load();
      await loadDebugData();
    } catch (e) {
      setWipeErr(e?.message || "Failed to wipe league tables.");
    } finally {
      setWipeBusy(false);
    }
  }

  async function deleteLeague() {
    if (!isGlobalAdmin) {
      setDeleteErr("Global admin access required.");
      return;
    }
    if (!deleteLeagueId) {
      setDeleteErr("Choose a league to delete.");
      return;
    }
    if (deleteConfirm.trim().toUpperCase() !== "DELETE") {
      setDeleteErr("Type DELETE to confirm.");
      return;
    }
    setDeleteBusy(true);
    setDeleteErr("");
    setDeleteOk("");
    try {
      await apiFetch(`/api/global/leagues/${encodeURIComponent(deleteLeagueId)}`, {
        method: "DELETE",
      });
      setDeleteOk(`Deleted league ${deleteLeagueId}.`);
      setDeleteConfirm("");
      await loadGlobalLeagues();
      if (leagueId === deleteLeagueId) {
        setDebugData(null);
      }
    } catch (e) {
      setDeleteErr(e?.message || "Failed to delete league.");
    } finally {
      setDeleteBusy(false);
    }
  }

  async function addMembership() {
    if (!isGlobalAdmin) {
      setMembershipErr("Global admin access required.");
      return;
    }
    setMembershipErr("");
    setMembershipOk("");
    const userId = membershipDraft.userId.trim();
    const targetLeagueId = membershipDraft.leagueId.trim();
    if (!userId || !targetLeagueId) {
      setMembershipErr("User ID and league are required.");
      return;
    }
    setMembershipBusy(true);
    try {
      const body = {
        userId,
        email: membershipDraft.email.trim(),
        leagueId: targetLeagueId,
        role: membershipDraft.role,
        team: membershipDraft.division.trim() || membershipDraft.teamId.trim()
          ? { division: membershipDraft.division.trim(), teamId: membershipDraft.teamId.trim() }
          : null,
      };
      await apiFetch("/api/admin/memberships", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      setMembershipOk(`Granted ${membershipDraft.role} in ${targetLeagueId}.`);
      setMembershipDraft((prev) => ({
        ...prev,
        userId: "",
        email: "",
        division: "",
        teamId: "",
      }));
      await load();
      await loadDebugData();
    } catch (e) {
      setMembershipErr(e?.message || "Failed to add membership.");
    } finally {
      setMembershipBusy(false);
    }
  }

  async function addGlobalAdmin() {
    if (!isGlobalAdmin) {
      setGlobalErr("Global admin access required.");
      return;
    }
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
    if (isGlobalAdmin) loadGlobalAdmins();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGlobalAdmin]);

  useEffect(() => {
    if (isGlobalAdmin) loadGlobalLeagues();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGlobalAdmin]);

  return (
    <div className="page">
      <div className="card">
        <h2>Debug: global admins</h2>
        <p className="muted">
          Data comes from <code>GameSwapGlobalAdmins</code> via <code>/api/admin/globaladmins</code>.
        </p>

        {isGlobalAdmin ? (
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
        ) : (
          <div className="muted">Global admin access is required to view this table.</div>
        )}

        {isGlobalAdmin ? (
          <div className="row gap-3 row--wrap mb-2">
            <button className="btn" onClick={addGlobalAdmin} disabled={globalBusy}>
              {globalBusy ? "Saving..." : "Grant global admin"}
            </button>
            <button className="btn btn--ghost" onClick={loadGlobalAdmins} disabled={globalBusy}>
              Refresh list
            </button>
          </div>
        ) : null}

        {globalErr && <div className="error">{globalErr}</div>}
        {globalOk && <div className="ok">{globalOk}</div>}
        {isGlobalAdmin && globalAdmins.length === 0 ? (
          <div className="muted">No global admins returned.</div>
        ) : isGlobalAdmin ? (
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
        ) : null}
      </div>

      <div className="card">
        <h2>Debug: league tools</h2>
        <p className="muted">
          Manage league data and memberships. Wipe and delete actions do not touch the global admins table.
        </p>

        <div className="formGrid">
          <label>
            League to delete
            <select value={deleteLeagueId} onChange={(e) => setDeleteLeagueId(e.target.value)}>
              {globalLeagues.map((league) => (
                <option key={league.leagueId} value={league.leagueId}>
                  {league.name ? `${league.name} (${league.leagueId})` : league.leagueId}
                </option>
              ))}
            </select>
          </label>
          <label>
            Confirm delete
            <input
              value={deleteConfirm}
              onChange={(e) => setDeleteConfirm(e.target.value)}
              placeholder="Type DELETE"
            />
          </label>
        </div>

        <div className="row gap-3 row--wrap mb-2">
          <button className="btn btn--danger" onClick={deleteLeague} disabled={deleteBusy}>
            {deleteBusy ? "Deleting..." : "Delete league"}
          </button>
        </div>

        {deleteErr && <div className="error">{deleteErr}</div>}
        {deleteOk && <div className="ok">{deleteOk}</div>}

        <div className="formGrid mt-4">
          <label>
            Confirm wipe
            <input
              value={wipeConfirm}
              onChange={(e) => setWipeConfirm(e.target.value)}
              placeholder="Type WIPE"
            />
          </label>
        </div>

        <div className="row gap-3 row--wrap mb-2">
          <button className="btn btn--danger" onClick={wipeLeagueData} disabled={wipeBusy}>
            {wipeBusy ? "Wiping..." : "Wipe league tables"}
          </button>
        </div>

        {wipeErr && <div className="error">{wipeErr}</div>}
        {wipeOk && <div className="ok">{wipeOk}</div>}
      </div>

      <div className="card">
        <h2>Debug: add membership</h2>
        <p className="muted">
          Assign users to multiple leagues by creating memberships directly.
        </p>

        <div className="formGrid">
          <label>
            User ID
            <input
              value={membershipDraft.userId}
              onChange={(e) => setMembershipDraft((prev) => ({ ...prev, userId: e.target.value }))}
              placeholder="aad|..."
            />
          </label>
          <label>
            Email (optional)
            <input
              value={membershipDraft.email}
              onChange={(e) => setMembershipDraft((prev) => ({ ...prev, email: e.target.value }))}
              placeholder="name@domain.com"
            />
          </label>
          <label>
            League
            <select
              value={membershipDraft.leagueId}
              onChange={(e) => setMembershipDraft((prev) => ({ ...prev, leagueId: e.target.value }))}
            >
              <option value="">Select league</option>
              {globalLeagues.map((league) => (
                <option key={league.leagueId} value={league.leagueId}>
                  {league.name ? `${league.name} (${league.leagueId})` : league.leagueId}
                </option>
              ))}
            </select>
          </label>
          <label>
            Role
            <select
              value={membershipDraft.role}
              onChange={(e) => setMembershipDraft((prev) => ({ ...prev, role: e.target.value }))}
            >
              {MEMBERSHIP_ROLES.map((role) => (
                <option key={role} value={role}>
                  {role}
                </option>
              ))}
            </select>
          </label>
          <label>
            Division (coach only)
            <input
              value={membershipDraft.division}
              onChange={(e) => setMembershipDraft((prev) => ({ ...prev, division: e.target.value }))}
            />
          </label>
          <label>
            Team ID (coach only)
            <input
              value={membershipDraft.teamId}
              onChange={(e) => setMembershipDraft((prev) => ({ ...prev, teamId: e.target.value }))}
            />
          </label>
        </div>

        <div className="row gap-3 row--wrap mb-2">
          <button className="btn" onClick={addMembership} disabled={membershipBusy}>
            {membershipBusy ? "Saving..." : "Grant membership"}
          </button>
        </div>

        {membershipErr && <div className="error">{membershipErr}</div>}
        {membershipOk && <div className="ok">{membershipOk}</div>}
      </div>

      <div className="card">
        <h2>Debug: raw league tables</h2>
        <p className="muted">
          Raw table data for the selected league (global admin access required).
        </p>

        <div className="row gap-3 row--wrap mb-2">
          <button className="btn" onClick={loadDebugData} disabled={debugLoading}>
            {debugLoading ? "Loading..." : "Load raw data"}
          </button>
        </div>

        {debugErr && <div className="error">{debugErr}</div>}
        {debugData ? (
          <details>
            <summary>Raw JSON</summary>
            <pre className="codeblock">{JSON.stringify(debugData, null, 2)}</pre>
          </details>
        ) : (
          <div className="muted">No raw data loaded yet.</div>
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
