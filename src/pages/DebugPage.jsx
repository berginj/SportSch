import { useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";

const ROLE_OPTIONS = ["", "LeagueAdmin", "Coach", "Viewer"];
const PRACTICE_REQUEST_LIMIT = 3;

function normalizeText(value) {
  return (value || "").trim();
}

function slotSortKey(slot) {
  return `${normalizeText(slot?.gameDate)} ${normalizeText(slot?.startTime)}`.trim();
}

function sortSlotsBySchedule(slots) {
  return [...slots].sort((a, b) => slotSortKey(a).localeCompare(slotSortKey(b)));
}

function formatSlotLocation(slot) {
  return normalizeText(slot?.displayName) || normalizeText(slot?.fieldName) || normalizeText(slot?.fieldKey);
}

function buildCoachSetupLink(leagueId, teamId) {
  const cleanLeagueId = normalizeText(leagueId);
  const cleanTeamId = normalizeText(teamId);
  if (!cleanLeagueId || !cleanTeamId) return "";
  const params = new URLSearchParams();
  params.set("leagueId", cleanLeagueId);
  params.set("teamId", cleanTeamId);
  const origin = typeof window !== "undefined" ? window.location.origin : "";
  const prefix = origin ? `${origin}/` : "/";
  return `${prefix}?${params.toString()}#coach-setup`;
}

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
  const [previewContextLoading, setPreviewContextLoading] = useState(false);
  const [previewContextErr, setPreviewContextErr] = useState("");
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewErr, setPreviewErr] = useState("");
  const [previewOk, setPreviewOk] = useState("");
  const [previewDivision, setPreviewDivision] = useState("");
  const [previewTeamId, setPreviewTeamId] = useState("");
  const [previewDivisions, setPreviewDivisions] = useState([]);
  const [previewTeams, setPreviewTeams] = useState([]);
  const [previewRequests, setPreviewRequests] = useState([]);
  const [previewSlots, setPreviewSlots] = useState([]);
  const [previewRefreshedAt, setPreviewRefreshedAt] = useState("");
  const previewLoadSeqRef = useRef(0);

  const usersById = useMemo(() => {
    const map = new Map();
    for (const u of users) {
      if (u?.userId) map.set(u.userId, u);
    }
    return map;
  }, [users]);

  const coachMemberships = useMemo(() => {
    return memberships
      .filter((m) => normalizeText(m?.role) === "Coach")
      .filter((m) => normalizeText(m?.team?.division) && normalizeText(m?.team?.teamId))
      .sort((a, b) => {
        const aKey = `${normalizeText(a?.team?.division)}|${normalizeText(a?.team?.teamId)}|${normalizeText(a?.email) || normalizeText(a?.userId)}`;
        const bKey = `${normalizeText(b?.team?.division)}|${normalizeText(b?.team?.teamId)}|${normalizeText(b?.email) || normalizeText(b?.userId)}`;
        return aKey.localeCompare(bKey);
      });
  }, [memberships]);

  const previewDivisionOptions = useMemo(() => {
    const fromDivisions = previewDivisions
      .map((d) => normalizeText(d?.code))
      .filter(Boolean);
    const fromTeams = previewTeams
      .map((t) => normalizeText(t?.division))
      .filter(Boolean);
    return Array.from(new Set([...fromDivisions, ...fromTeams])).sort((a, b) => a.localeCompare(b));
  }, [previewDivisions, previewTeams]);

  const previewTeamOptions = useMemo(() => {
    const division = normalizeText(previewDivision);
    if (!division) return [];
    return previewTeams
      .filter((t) => normalizeText(t?.division) === division)
      .sort((a, b) => {
        const aLabel = normalizeText(a?.name) || normalizeText(a?.teamId);
        const bLabel = normalizeText(b?.name) || normalizeText(b?.teamId);
        return aLabel.localeCompare(bLabel);
      });
  }, [previewTeams, previewDivision]);

  const selectedPreviewTeam = useMemo(() => {
    const teamId = normalizeText(previewTeamId);
    if (!teamId) return null;
    return previewTeamOptions.find((t) => normalizeText(t?.teamId) === teamId) || null;
  }, [previewTeamId, previewTeamOptions]);

  const coachesForPreviewTeam = useMemo(() => {
    const division = normalizeText(previewDivision);
    const teamId = normalizeText(previewTeamId);
    if (!division || !teamId) return [];
    return coachMemberships.filter(
      (m) =>
        normalizeText(m?.team?.division) === division &&
        normalizeText(m?.team?.teamId) === teamId
    );
  }, [coachMemberships, previewDivision, previewTeamId]);

  const activePracticeRequests = useMemo(() => {
    return previewRequests.filter((r) => {
      const status = normalizeText(r?.status);
      return status === "Pending" || status === "Approved";
    });
  }, [previewRequests]);

  const activeRequestSlotIds = useMemo(() => {
    return new Set(activePracticeRequests.map((r) => normalizeText(r?.slotId)).filter(Boolean));
  }, [activePracticeRequests]);

  const previewStatusCounts = useMemo(() => {
    const counts = { pending: 0, approved: 0, rejected: 0 };
    for (const request of previewRequests) {
      const status = normalizeText(request?.status).toLowerCase();
      if (status === "pending") counts.pending += 1;
      else if (status === "approved") counts.approved += 1;
      else if (status === "rejected") counts.rejected += 1;
    }
    return counts;
  }, [previewRequests]);

  const coachSetupLink = useMemo(() => {
    return buildCoachSetupLink(leagueId, previewTeamId);
  }, [leagueId, previewTeamId]);

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

  async function loadPracticePreviewData(nextDivision = previewDivision, nextTeamId = previewTeamId) {
    const requestSeq = ++previewLoadSeqRef.current;
    const division = normalizeText(nextDivision);
    const teamId = normalizeText(nextTeamId);
    if (!leagueId) {
      setPreviewErr("Select a league to load coach practice preview.");
      setPreviewRequests([]);
      setPreviewSlots([]);
      return;
    }
    if (!division || !teamId) {
      setPreviewErr("Select both division and team.");
      setPreviewRequests([]);
      setPreviewSlots([]);
      return;
    }

    setPreviewLoading(true);
    setPreviewErr("");
    setPreviewOk("");
    try {
      const [requestsRaw, slotsRaw] = await Promise.all([
        apiFetch(`/api/practice-requests?teamId=${encodeURIComponent(teamId)}`),
        apiFetch(`/api/slots?division=${encodeURIComponent(division)}&status=Open`)
      ]);

      const requests = Array.isArray(requestsRaw)
        ? [...requestsRaw].sort((a, b) => String(b?.requestedUtc || "").localeCompare(String(a?.requestedUtc || "")))
        : [];
      const allOpenSlots = Array.isArray(slotsRaw) ? slotsRaw : [];
      const availabilitySlots = allOpenSlots.filter(
        (slot) => slot?.isAvailability === true && normalizeText(slot?.status) === "Open"
      );

      if (requestSeq !== previewLoadSeqRef.current) return;
      setPreviewRequests(requests);
      setPreviewSlots(sortSlotsBySchedule(availabilitySlots).slice(0, 20));
      setPreviewRefreshedAt(new Date().toISOString());
    } catch (e) {
      if (requestSeq !== previewLoadSeqRef.current) return;
      setPreviewErr(e?.message || "Failed to load coach practice preview.");
      setPreviewRequests([]);
      setPreviewSlots([]);
    } finally {
      if (requestSeq === previewLoadSeqRef.current) {
        setPreviewLoading(false);
      }
    }
  }

  async function loadPracticePreviewContext() {
    if (!isGlobalAdmin) return;
    if (!leagueId) {
      setPreviewContextErr("Select a league to load coach practice preview.");
      setPreviewDivisions([]);
      setPreviewTeams([]);
      setPreviewDivision("");
      setPreviewTeamId("");
      setPreviewRequests([]);
      setPreviewSlots([]);
      return;
    }

    setPreviewContextLoading(true);
    setPreviewContextErr("");
    try {
      const [divisionsRaw, teamsRaw] = await Promise.all([
        apiFetch("/api/divisions"),
        apiFetch("/api/teams")
      ]);

      const divisions = Array.isArray(divisionsRaw) ? divisionsRaw : [];
      const teams = Array.isArray(teamsRaw)
        ? teamsRaw.filter((t) => normalizeText(t?.division) && normalizeText(t?.teamId))
        : [];

      teams.sort((a, b) => {
        const aKey = `${normalizeText(a?.division)}|${normalizeText(a?.name) || normalizeText(a?.teamId)}`;
        const bKey = `${normalizeText(b?.division)}|${normalizeText(b?.name) || normalizeText(b?.teamId)}`;
        return aKey.localeCompare(bKey);
      });

      setPreviewDivisions(divisions);
      setPreviewTeams(teams);

      let nextDivision = normalizeText(previewDivision);
      if (!nextDivision || !teams.some((t) => normalizeText(t?.division) === nextDivision)) {
        nextDivision = normalizeText(teams[0]?.division) || normalizeText(divisions[0]?.code);
      }

      let nextTeamId = normalizeText(previewTeamId);
      if (
        !nextTeamId ||
        !teams.some(
          (t) =>
            normalizeText(t?.division) === nextDivision &&
            normalizeText(t?.teamId) === nextTeamId
        )
      ) {
        nextTeamId =
          normalizeText(teams.find((t) => normalizeText(t?.division) === nextDivision)?.teamId);
      }

      setPreviewDivision(nextDivision);
      setPreviewTeamId(nextTeamId);

      if (nextDivision && nextTeamId) {
        await loadPracticePreviewData(nextDivision, nextTeamId);
      } else {
        setPreviewRequests([]);
        setPreviewSlots([]);
      }
    } catch (e) {
      setPreviewContextErr(e?.message || "Failed to load preview context.");
      setPreviewDivisions([]);
      setPreviewTeams([]);
      setPreviewRequests([]);
      setPreviewSlots([]);
    } finally {
      setPreviewContextLoading(false);
    }
  }

  function onPreviewDivisionChange(value) {
    const nextDivision = normalizeText(value);
    setPreviewDivision(nextDivision);
    setPreviewErr("");
    setPreviewOk("");

    const teamsInDivision = previewTeams.filter(
      (t) => normalizeText(t?.division) === nextDivision
    );
    const nextTeamId = normalizeText(teamsInDivision[0]?.teamId);
    setPreviewTeamId(nextTeamId);

    if (nextDivision && nextTeamId) {
      loadPracticePreviewData(nextDivision, nextTeamId);
      return;
    }

    setPreviewRequests([]);
    setPreviewSlots([]);
    if (nextDivision) {
      setPreviewErr("No teams found in this division.");
    }
  }

  function onPreviewTeamChange(value) {
    const nextTeamId = normalizeText(value);
    setPreviewTeamId(nextTeamId);
    setPreviewErr("");
    setPreviewOk("");
    if (normalizeText(previewDivision) && nextTeamId) {
      loadPracticePreviewData(previewDivision, nextTeamId);
      return;
    }
    setPreviewRequests([]);
    setPreviewSlots([]);
  }

  async function copyCoachSetupPreviewLink() {
    if (!coachSetupLink || typeof navigator === "undefined" || !navigator.clipboard) {
      setPreviewErr("Unable to copy link in this browser.");
      return;
    }

    setPreviewErr("");
    setPreviewOk("");
    try {
      await navigator.clipboard.writeText(coachSetupLink);
      setPreviewOk("Coach onboarding link copied.");
    } catch {
      setPreviewErr("Failed to copy coach onboarding link.");
    }
  }

  useEffect(() => {
    loadMemberships();
    loadGlobalAdmins();
    if (isGlobalAdmin) {
      loadPracticePreviewContext();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId, isGlobalAdmin]);

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

      {isGlobalAdmin ? (
        <div className="card">
          <h2>Debug: coach practice request preview</h2>
          <p className="muted">
            Preview exactly what a coach sees in onboarding practice requests using <code>/api/practice-requests</code> and open
            availability slots from <code>/api/slots</code>.
          </p>

          <div className="row gap-3 row--wrap mb-2">
            <label>
              Division
              <select
                value={previewDivision}
                onChange={(e) => onPreviewDivisionChange(e.target.value)}
                disabled={previewContextLoading}
              >
                <option value="">Select division</option>
                {previewDivisionOptions.map((code) => (
                  <option key={code} value={code}>
                    {code}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Team
              <select
                value={previewTeamId}
                onChange={(e) => onPreviewTeamChange(e.target.value)}
                disabled={previewContextLoading || !normalizeText(previewDivision)}
              >
                <option value="">Select team</option>
                {previewTeamOptions.map((team) => (
                  <option key={`${team.division}-${team.teamId}`} value={team.teamId}>
                    {normalizeText(team?.name) || team.teamId} ({team.teamId})
                  </option>
                ))}
              </select>
            </label>
            <button
              className="btn"
              onClick={() => loadPracticePreviewData()}
              disabled={previewLoading || !normalizeText(previewDivision) || !normalizeText(previewTeamId)}
            >
              {previewLoading ? "Loading..." : "Refresh preview"}
            </button>
            <button
              className="btn btn--ghost"
              onClick={copyCoachSetupPreviewLink}
              disabled={!coachSetupLink}
            >
              Copy coach link
            </button>
            {coachSetupLink ? (
              <a className="btn btn--ghost" href={coachSetupLink} target="_blank" rel="noreferrer">
                Open link
              </a>
            ) : null}
          </div>

          {coachSetupLink ? (
            <div className="mb-2">
              <label>
                Coach onboarding link
                <input value={coachSetupLink} readOnly onClick={(e) => e.target.select()} />
              </label>
            </div>
          ) : null}

          {previewContextErr && <div className="error">{previewContextErr}</div>}
          {previewErr && <div className="error">{previewErr}</div>}
          {previewOk && <div className="ok">{previewOk}</div>}

          {selectedPreviewTeam ? (
            <div className="mb-3">
              <div className="font-semibold">
                Team: {normalizeText(selectedPreviewTeam?.name) || selectedPreviewTeam.teamId} ({selectedPreviewTeam.teamId})
              </div>
              <div className="muted">
                Division: {selectedPreviewTeam.division}
                {previewRefreshedAt ? ` | Last refreshed: ${new Date(previewRefreshedAt).toLocaleString()}` : ""}
              </div>
            </div>
          ) : null}

          {normalizeText(previewDivision) && normalizeText(previewTeamId) && !previewLoading ? (
            <div className="grid grid-cols-1 md:grid-cols-4 gap-3 mb-4">
              <div className="p-3 bg-yellow-50 rounded border border-yellow-200">
                <div className="font-semibold text-yellow-900">{previewStatusCounts.pending}</div>
                <div className="text-sm text-yellow-700">Pending requests</div>
              </div>
              <div className="p-3 bg-green-50 rounded border border-green-200">
                <div className="font-semibold text-green-900">{previewStatusCounts.approved}</div>
                <div className="text-sm text-green-700">Approved requests</div>
              </div>
              <div className="p-3 bg-red-50 rounded border border-red-200">
                <div className="font-semibold text-red-900">{previewStatusCounts.rejected}</div>
                <div className="text-sm text-red-700">Rejected requests</div>
              </div>
              <div className="p-3 bg-blue-50 rounded border border-blue-200">
                <div className="font-semibold text-blue-900">{previewSlots.length}</div>
                <div className="text-sm text-blue-700">Open practice slots</div>
              </div>
            </div>
          ) : null}

          {normalizeText(previewDivision) && normalizeText(previewTeamId) ? (
            <>
              {coachesForPreviewTeam.length === 0 ? (
                <div className="callout callout--error mb-3">
                  No coach membership is assigned to this team in <code>GameSwapMemberships</code>. The coach link will not show
                  team-specific onboarding data until assignment is set.
                </div>
              ) : (
                <div className="tableWrap mb-3">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Assigned coaches</th>
                        <th>User ID</th>
                      </tr>
                    </thead>
                    <tbody>
                      {coachesForPreviewTeam.map((coach) => (
                        <tr key={`${coach.userId}-${coach.team?.teamId}`}>
                          <td>{coach.email || "(no email)"}</td>
                          <td>{coach.userId}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <div className={activePracticeRequests.length >= PRACTICE_REQUEST_LIMIT ? "callout callout--error mb-3" : "callout callout--ok mb-3"}>
                Active request count (Pending + Approved): <b>{activePracticeRequests.length}</b> / {PRACTICE_REQUEST_LIMIT}
              </div>

              <h3>Practice requests for this team</h3>
              {previewRequests.length === 0 ? (
                <div className="muted mb-3">No practice requests found.</div>
              ) : (
                <div className="tableWrap mb-3">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Status</th>
                        <th>Date</th>
                        <th>Time</th>
                        <th>Location</th>
                        <th>Requested</th>
                      </tr>
                    </thead>
                    <tbody>
                      {previewRequests.map((request) => (
                        <tr key={request.requestId}>
                          <td>{request.status || ""}</td>
                          <td>{request.slot?.gameDate || ""}</td>
                          <td>{request.slot?.startTime && request.slot?.endTime ? `${request.slot.startTime} - ${request.slot.endTime}` : ""}</td>
                          <td>{formatSlotLocation(request.slot)}</td>
                          <td>{request.requestedUtc ? new Date(request.requestedUtc).toLocaleString() : ""}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <h3>Open availability slots visible to coaches (top 20)</h3>
              {previewSlots.length === 0 ? (
                <div className="muted">No open availability slots found for this division.</div>
              ) : (
                <div className="tableWrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Date</th>
                        <th>Time</th>
                        <th>Location</th>
                        <th>Requestable?</th>
                      </tr>
                    </thead>
                    <tbody>
                      {previewSlots.map((slot) => {
                        const slotId = normalizeText(slot?.slotId);
                        const alreadyRequested = activeRequestSlotIds.has(slotId);
                        return (
                          <tr key={slotId || `${slot.gameDate}-${slot.startTime}`}>
                            <td>{slot.gameDate || ""}</td>
                            <td>{slot.startTime && slot.endTime ? `${slot.startTime} - ${slot.endTime}` : ""}</td>
                            <td>{formatSlotLocation(slot)}</td>
                            <td>{alreadyRequested ? "Already requested" : "Yes"}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          ) : (
            <div className="muted">Select division and team to preview coach practice requests.</div>
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
