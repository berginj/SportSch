import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";

const ROLE_OPTIONS = ["", "LeagueAdmin", "Coach", "Viewer"];

export default function DebugPage({ leagueId, me }) {
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [memberships, setMemberships] = useState([]);
  const [globalAdmins, setGlobalAdmins] = useState([]);
  const [globalErr, setGlobalErr] = useState("");
  const [globalOk, setGlobalOk] = useState("");
  const [globalBusy, setGlobalBusy] = useState(false);
  const [globalLoading, setGlobalLoading] = useState(true);
  const [globalDraft, setGlobalDraft] = useState({ userId: "", email: "" });

  const [usersLoading, setUsersLoading] = useState(false);
  const [users, setUsers] = useState([]);
  const [userSearch, setUserSearch] = useState("");
  const [userEdits, setUserEdits] = useState({});
  const [userErr, setUserErr] = useState("");
  const [userOk, setUserOk] = useState("");

  const [membersLoadingAll, setMembersLoadingAll] = useState(false);
  const [membershipsAll, setMembershipsAll] = useState([]);
  const [memberSearch, setMemberSearch] = useState("");
  const [memberLeague, setMemberLeague] = useState("");
  const [memberRole, setMemberRole] = useState("");
  const [memberEdits, setMemberEdits] = useState({});
  const [memberDraft, setMemberDraft] = useState({ userId: "", email: "", leagueId: "", role: "" });
  const [memberErr, setMemberErr] = useState("");
  const [memberOk, setMemberOk] = useState("");

  const usersById = useMemo(() => {
    const map = new Map();
    for (const u of users) {
      if (u?.userId) map.set(u.userId, u);
    }
    return map;
  }, [users]);

  async function loadMemberships() {
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
    setGlobalLoading(true);
    setGlobalErr("");
    try {
      const data = await apiFetch("/api/globaladmins");
      setGlobalAdmins(Array.isArray(data) ? data : []);
    } catch (e) {
      setGlobalErr(e?.message || "Failed to load global admins.");
      setGlobalAdmins([]);
    } finally {
      setGlobalLoading(false);
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
      await apiFetch("/api/globaladmins", {
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

  async function loadUsers() {
    if (!isGlobalAdmin) return;
    setUsersLoading(true);
    setUserErr("");
    try {
      const qs = new URLSearchParams();
      if (userSearch.trim()) qs.set("search", userSearch.trim());
      const data = await apiFetch(`/api/users${qs.toString() ? `?${qs.toString()}` : ""}`);
      setUsers(Array.isArray(data) ? data : []);
    } catch (e) {
      setUserErr(e?.message || "Failed to load users.");
      setUsers([]);
    } finally {
      setUsersLoading(false);
    }
  }

  function updateUserEdit(userId, patch) {
    setUserEdits((prev) => ({
      ...prev,
      [userId]: { ...prev[userId], ...patch },
    }));
  }

  function getUserEdit(user) {
    const draft = userEdits[user.userId] || {};
    return {
      homeLeagueId: draft.homeLeagueId ?? user.homeLeagueId ?? "",
      role: draft.role ?? user.homeLeagueRole ?? "",
    };
  }

  async function saveUser(user) {
    const userId = (user.userId || "").trim();
    if (!userId) return;
    const draft = getUserEdit(user);
    const homeLeagueId = (draft.homeLeagueId || "").trim();
    const role = (draft.role || "").trim();
    if (role && !homeLeagueId) {
      setUserErr("Home league is required when setting a role.");
      return;
    }
    setUserErr("");
    setUserOk("");
    try {
      await apiFetch("/api/users", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId,
          email: (user.email || "").trim(),
          homeLeagueId,
          role: role || undefined,
        }),
      });
      setUserOk(`Saved ${userId}.`);
      setUserEdits((prev) => {
        const next = { ...prev };
        delete next[userId];
        return next;
      });
      await loadUsers();
    } catch (e) {
      setUserErr(e?.message || "Failed to save user.");
    }
  }

  function focusMembershipsForUser(user) {
    const userId = (user?.userId || "").trim();
    if (userId) {
      setMemberSearch(userId);
      setMemberLeague("");
      setMemberRole("");
    }
    setMemberDraft({
      userId,
      email: (user?.email || "").trim(),
      leagueId: "",
      role: "",
    });
  }

  async function loadAllMemberships() {
    if (!isGlobalAdmin) return;
    setMembersLoadingAll(true);
    setMemberErr("");
    try {
      const qs = new URLSearchParams();
      qs.set("all", "true");
      if (memberSearch.trim()) qs.set("search", memberSearch.trim());
      if (memberLeague.trim()) qs.set("leagueId", memberLeague.trim());
      if (memberRole.trim()) qs.set("role", memberRole.trim());
      const data = await apiFetch(`/api/memberships?${qs.toString()}`);
      setMembershipsAll(Array.isArray(data) ? data : []);
    } catch (e) {
      setMemberErr(e?.message || "Failed to load memberships.");
      setMembershipsAll([]);
    } finally {
      setMembersLoadingAll(false);
    }
  }

  function updateMemberEdit(key, patch) {
    setMemberEdits((prev) => ({
      ...prev,
      [key]: { ...prev[key], ...patch },
    }));
  }

  function getMemberEdit(m) {
    const key = `${m.userId}|${m.leagueId}`;
    const draft = memberEdits[key] || {};
    return {
      role: draft.role ?? m.role ?? "",
    };
  }

  async function saveMembership(m) {
    const userId = (m.userId || "").trim();
    const leagueId = (m.leagueId || "").trim();
    if (!userId || !leagueId) return;
    const draft = getMemberEdit(m);
    const role = (draft.role || "").trim();
    if (!role) {
      setMemberErr("Role is required.");
      return;
    }
    const user = usersById.get(userId);
    const email = (m.email || user?.email || "").trim();
    setMemberErr("");
    setMemberOk("");
    try {
      await apiFetch("/api/admin/memberships", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId,
          email,
          leagueId,
          role,
        }),
      });
      setMemberOk(`Saved ${userId} in ${leagueId}.`);
      setMemberEdits((prev) => {
        const next = { ...prev };
        delete next[`${userId}|${leagueId}`];
        return next;
      });
      await loadAllMemberships();
      await loadUsers();
    } catch (e) {
      setMemberErr(e?.message || "Failed to save membership.");
    }
  }

  async function saveMembershipDraft() {
    const userId = (memberDraft.userId || "").trim();
    const email = (memberDraft.email || "").trim();
    const leagueId = (memberDraft.leagueId || "").trim();
    const role = (memberDraft.role || "").trim();
    if (!userId || !leagueId || !role) {
      setMemberErr("User ID, league, and role are required.");
      return;
    }
    setMemberErr("");
    setMemberOk("");
    try {
      await apiFetch("/api/admin/memberships", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ userId, email, leagueId, role }),
      });
      setMemberOk(`Added ${userId} to ${leagueId}.`);
      setMemberDraft({ userId: "", email: "", leagueId: "", role: "" });
      await loadAllMemberships();
      await loadUsers();
    } catch (e) {
      setMemberErr(e?.message || "Failed to save membership.");
    }
  }

  useEffect(() => {
    loadMemberships();
    loadGlobalAdmins();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    if (!isGlobalAdmin) return;
    loadUsers();
    loadAllMemberships();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGlobalAdmin]);

  return (
    <div className="stack">
      {isGlobalAdmin ? (
        <div className="card">
          <h2>Debug: user management</h2>
          <p className="muted">
            Search users, update home league defaults, and assign league roles.
          </p>

          <div className="row gap-3 row--wrap mb-2">
            <label>
              Search
              <input
                value={userSearch}
                onChange={(e) => setUserSearch(e.target.value)}
                placeholder="userId or email"
              />
            </label>
            <button className="btn" onClick={loadUsers} disabled={usersLoading}>
              {usersLoading ? "Loading..." : "Refresh users"}
            </button>
          </div>

          {userErr && <div className="error">{userErr}</div>}
          {userOk && <div className="ok">{userOk}</div>}

          {usersLoading ? (
            <div className="muted">Loading...</div>
          ) : users.length === 0 ? (
            <div className="muted">No users returned.</div>
          ) : (
            <div className="tableWrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>Home league</th>
                    <th>Home role</th>
                    <th>Updated</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {users.map((u) => {
                    const draft = getUserEdit(u);
                    return (
                      <tr key={u.userId}>
                        <td>
                          <div className="font-semibold">{u.email || u.userId}</div>
                          <div className="muted text-xs">{u.userId}</div>
                        </td>
                        <td>
                          <input
                            value={draft.homeLeagueId}
                            onChange={(e) => updateUserEdit(u.userId, { homeLeagueId: e.target.value })}
                            placeholder="League ID"
                          />
                        </td>
                        <td>
                          <select
                            value={draft.role}
                            onChange={(e) => updateUserEdit(u.userId, { role: e.target.value })}
                          >
                            {ROLE_OPTIONS.map((opt) => (
                              <option key={opt || "none"} value={opt}>
                                {opt || "None"}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>{u.updatedUtc ? new Date(u.updatedUtc).toLocaleString() : ""}</td>
                        <td className="row gap-2 row--wrap">
                          <button className="btn" onClick={() => saveUser(u)}>
                            Save
                          </button>
                          <button
                            className="btn btn--ghost"
                            onClick={() => focusMembershipsForUser(u)}
                          >
                            Memberships
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : null}

      {isGlobalAdmin ? (
        <div className="card">
          <h2>Debug: league memberships</h2>
          <p className="muted">
            Assign users to leagues and adjust roles. Data comes from <code>GameSwapMemberships</code>.
          </p>

          <div className="formGrid">
            <label>
              User ID
              <input
                value={memberDraft.userId}
                onChange={(e) => setMemberDraft((prev) => ({ ...prev, userId: e.target.value }))}
                placeholder="aad|..."
              />
            </label>
            <label>
              Email (optional)
              <input
                value={memberDraft.email}
                onChange={(e) => setMemberDraft((prev) => ({ ...prev, email: e.target.value }))}
                placeholder="name@domain.com"
              />
            </label>
            <label>
              League ID
              <input
                value={memberDraft.leagueId}
                onChange={(e) => setMemberDraft((prev) => ({ ...prev, leagueId: e.target.value }))}
                placeholder="ARL"
              />
            </label>
            <label>
              Role
              <select
                value={memberDraft.role}
                onChange={(e) => setMemberDraft((prev) => ({ ...prev, role: e.target.value }))}
              >
                {ROLE_OPTIONS.map((opt) => (
                  <option key={opt || "none"} value={opt}>
                    {opt || "Select role"}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="row gap-3 row--wrap mb-2">
            <button className="btn" onClick={saveMembershipDraft}>
              Save membership
            </button>
            <button className="btn btn--ghost" onClick={() => setMemberDraft({ userId: "", email: "", leagueId: "", role: "" })}>
              Clear
            </button>
          </div>

          <div className="row gap-3 row--wrap mb-2">
            <label>
              Search
              <input
                value={memberSearch}
                onChange={(e) => setMemberSearch(e.target.value)}
                placeholder="userId, email, league, role"
              />
            </label>
            <label>
              League filter
              <input
                value={memberLeague}
                onChange={(e) => setMemberLeague(e.target.value)}
                placeholder="League ID"
              />
            </label>
            <label>
              Role filter
              <select
                value={memberRole}
                onChange={(e) => setMemberRole(e.target.value)}
              >
                {ROLE_OPTIONS.map((opt) => (
                  <option key={opt || "any"} value={opt}>
                    {opt || "All roles"}
                  </option>
                ))}
              </select>
            </label>
            <button className="btn" onClick={loadAllMemberships} disabled={membersLoadingAll}>
              {membersLoadingAll ? "Loading..." : "Refresh memberships"}
            </button>
          </div>

          {memberErr && <div className="error">{memberErr}</div>}
          {memberOk && <div className="ok">{memberOk}</div>}

          {membersLoadingAll ? (
            <div className="muted">Loading...</div>
          ) : membershipsAll.length === 0 ? (
            <div className="muted">No memberships returned.</div>
          ) : (
            <div className="tableWrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>League</th>
                    <th>Role</th>
                    <th>Team</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {membershipsAll.map((m) => {
                    const draft = getMemberEdit(m);
                    return (
                      <tr key={`${m.userId}-${m.leagueId}-${m.role}`}>
                        <td>
                          <div className="font-semibold">{m.email || m.userId}</div>
                          <div className="muted text-xs">{m.userId}</div>
                        </td>
                        <td>{m.leagueId}</td>
                        <td>
                          <select
                            value={draft.role}
                            onChange={(e) => updateMemberEdit(`${m.userId}|${m.leagueId}`, { role: e.target.value })}
                          >
                            {ROLE_OPTIONS.filter((r) => r).map((opt) => (
                              <option key={opt} value={opt}>
                                {opt}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>{m.team?.teamId || ""}</td>
                        <td>
                          <button className="btn" onClick={() => saveMembership(m)}>
                            Save role
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : null}

      <div className="card">
        <h2>Debug: global admins</h2>
        <p className="muted">
          Data comes from <code>GameSwapGlobalAdmins</code> via <code>/api/globaladmins</code>.
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
          <button className="btn btn--ghost" onClick={loadGlobalAdmins} disabled={globalLoading}>
            Refresh list
          </button>
        </div>

        {globalErr && <div className="error">{globalErr}</div>}
        {globalOk && <div className="ok">{globalOk}</div>}
        {globalLoading ? (
          <div className="muted">Loading...</div>
        ) : globalAdmins.length === 0 ? (
          <div className="muted">No global admins returned.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>User</th>
                  <th>Email</th>
                  <th>Created</th>
                </tr>
              </thead>
              <tbody>
                {globalAdmins.map((admin) => (
                  <tr key={admin.userId}>
                    <td>
                      <div className="font-semibold">{admin.userId}</div>
                    </td>
                    <td>{admin.email || ""}</td>
                    <td>{admin.createdUtc ? new Date(admin.createdUtc).toLocaleString() : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <details className="mt-4">
          <summary>Raw JSON</summary>
          <pre className="codeblock">{JSON.stringify(globalAdmins, null, 2)}</pre>
        </details>
      </div>

      <div className="card">
        <h2>Debug: memberships table</h2>
        <p className="muted">
          Data comes from <code>GameSwapMemberships</code> for the active league via <code>/api/memberships</code>.
        </p>

        <div className="row gap-3 row--wrap mb-2">
          <button className="btn" onClick={loadMemberships} disabled={loading}>
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
