import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import Toast from "../components/Toast";
import { PromptDialog } from "../components/Dialogs";
import { usePromptDialog } from "../lib/useDialogs";
import { LEAGUE_HEADER_NAME } from "../lib/constants";
import { trackEvent } from "../lib/telemetry";
import AccessRequestsSection from "./admin/AccessRequestsSection";
import CoachAssignmentsSection from "./admin/CoachAssignmentsSection";
import CsvImportSection from "./admin/CsvImportSection";
import GlobalAdminSection from "./admin/GlobalAdminSection";

const ROLE_OPTIONS = [
  "LeagueAdmin",
  "Coach",
  "Viewer",
];

function csvEscape(value) {
  const raw = String(value ?? "");
  if (!/[",\n]/.test(raw)) return raw;
  return `"${raw.replace(/"/g, '""')}"`;
}

function buildTeamsTemplateCsv(divisions) {
  const header = ["division", "teamId", "name", "coachName", "coachEmail", "coachPhone"];
  const rows = (divisions || [])
    .map((d) => {
      if (!d) return "";
      if (typeof d === "string") return d;
      if (d.isActive === false) return "";
      return d.code || d.division || "";
    })
    .filter(Boolean)
    .map((code) => [code, "", "", "", "", ""]);

  return [header, ...rows].map((row) => row.map(csvEscape).join(",")).join("\n");
}

function downloadCsv(csv, filename) {
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.setAttribute("download", filename);
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export default function AdminPage({ me, leagueId, setLeagueId }) {
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [items, setItems] = useState([]);
  const [memLoading, setMemLoading] = useState(false);
  const [memberships, setMemberships] = useState([]);
  const [divisions, setDivisions] = useState([]);
  const [teams, setTeams] = useState([]);
  const [coachDraft, setCoachDraft] = useState({});
  const [slotsFile, setSlotsFile] = useState(null);
  const [teamsFile, setTeamsFile] = useState(null);
  const [slotsBusy, setSlotsBusy] = useState(false);
  const [teamsBusy, setTeamsBusy] = useState(false);
  const [slotsErr, setSlotsErr] = useState("");
  const [teamsErr, setTeamsErr] = useState("");
  const [slotsOk, setSlotsOk] = useState("");
  const [teamsOk, setTeamsOk] = useState("");
  const [slotsErrors, setSlotsErrors] = useState([]);
  const [slotsWarnings, setSlotsWarnings] = useState([]);
  const [teamsErrors, setTeamsErrors] = useState([]);
  const [globalErr, setGlobalErr] = useState("");
  const [globalOk, setGlobalOk] = useState("");
  const [globalLoading, setGlobalLoading] = useState(false);
  const [globalLeagues, setGlobalLeagues] = useState([]);
  const [userSearch, setUserSearch] = useState("");
  const [usersLoading, setUsersLoading] = useState(false);
  const [users, setUsers] = useState([]);
  const [userDraft, setUserDraft] = useState({ userId: "", email: "", homeLeagueId: "", role: "" });
  const [memberSearch, setMemberSearch] = useState("");
  const [memberLeague, setMemberLeague] = useState("");
  const [memberRole, setMemberRole] = useState("");
  const [membersLoadingAll, setMembersLoadingAll] = useState(false);
  const [membersAll, setMembersAll] = useState([]);
  const [newLeague, setNewLeague] = useState({ leagueId: "", name: "" });
  const [seasonLeagueId, setSeasonLeagueId] = useState("");
  const [seasonDraft, setSeasonDraft] = useState({
    springStart: "",
    springEnd: "",
    fallStart: "",
    fallEnd: "",
    gameLengthMinutes: 0,
  });
  const [blackoutsDraft, setBlackoutsDraft] = useState([]);
  const [toast, setToast] = useState(null);
  const [accessStatus, setAccessStatus] = useState("Pending");
  const [accessScope, setAccessScope] = useState("league");
  const [accessLeagueFilter, setAccessLeagueFilter] = useState("");
  const { promptState, promptValue, setPromptValue, requestPrompt, handleConfirm, handleCancel } = usePromptDialog();

  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const hasMemberships = Array.isArray(me?.memberships) && me.memberships.length > 0;
  const accessAll = accessScope === "all";

  async function load() {
    setLoading(true);
    setErr("");
    try {
      const qs = new URLSearchParams();
      qs.set("status", accessStatus);
      if (accessAll) qs.set("all", "true");
      const data = await apiFetch(`/api/accessrequests?${qs.toString()}`);
      setItems(Array.isArray(data) ? data : []);
    } catch (e) {
      setErr(e?.message || "Failed to load pending requests");
      setItems([]);
    } finally {
      setLoading(false);
    }
  }

  async function loadMembershipsAndTeams() {
    setMemLoading(true);
    try {
      const [m, d, t] = await Promise.all([
        apiFetch(`/api/memberships`),
        apiFetch(`/api/divisions`),
        apiFetch(`/api/teams`),
      ]);
      const mems = Array.isArray(m) ? m : [];
      setMemberships(mems);
      setDivisions(Array.isArray(d) ? d : []);
      setTeams(Array.isArray(t) ? t : []);

      // initialize draft assignments from current server state
      const draft = {};
      for (const mm of mems) {
        if ((mm.role || "").toLowerCase() !== "coach") continue;
        draft[mm.userId] = {
          division: mm.team?.division || "",
          teamId: mm.team?.teamId || "",
        };
      }
      setCoachDraft(draft);
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to load memberships/teams" });
      setMemberships([]);
      setDivisions([]);
      setTeams([]);
    } finally {
      setMemLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId, accessStatus, accessAll]);

  useEffect(() => {
    loadMembershipsAndTeams();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const status = (params.get("accessStatus") || "Pending").trim();
    const allowed = new Set(["Pending", "Approved", "Denied"]);
    setAccessStatus(allowed.has(status) ? status : "Pending");
  }, []);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (accessStatus) params.set("accessStatus", accessStatus);
    else params.delete("accessStatus");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [accessStatus]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const scopeParam = params.get("accessScope");
    const scope = (scopeParam || "").trim();
    if (!scopeParam && isGlobalAdmin) {
      setAccessScope("all");
      return;
    }
    setAccessScope(isGlobalAdmin && scope === "all" ? "all" : "league");
  }, [isGlobalAdmin]);

  useEffect(() => {
    if (!isGlobalAdmin) return;
    if (!hasMemberships || !leagueId) {
      setAccessScope("all");
    }
  }, [isGlobalAdmin, hasMemberships, leagueId]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (isGlobalAdmin && accessAll) params.set("accessScope", "all");
    else params.delete("accessScope");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [accessAll, isGlobalAdmin]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const league = (params.get("accessLeague") || "").trim();
    setAccessLeagueFilter(league);
  }, []);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (isGlobalAdmin && accessAll && accessLeagueFilter) params.set("accessLeague", accessLeagueFilter);
    else params.delete("accessLeague");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [accessAll, accessLeagueFilter, isGlobalAdmin]);

  useEffect(() => {
    if (!isGlobalAdmin) return;
    loadGlobalLeagues();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGlobalAdmin]);

  useEffect(() => {
    if (!isGlobalAdmin) return;
    loadUsers();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGlobalAdmin]);

  useEffect(() => {
    if (!isGlobalAdmin) return;
    loadAllMemberships();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGlobalAdmin]);

  async function loadGlobalLeagues() {
    setGlobalErr("");
    setGlobalLoading(true);
    try {
      const list = await apiFetch("/api/global/leagues");
      setGlobalLeagues(Array.isArray(list) ? list : []);
      if (!seasonLeagueId && Array.isArray(list) && list.length) {
        setSeasonLeagueId(list[0].leagueId);
      }
    } catch (e) {
      setGlobalErr(e?.message || "Failed to load leagues");
      setGlobalLeagues([]);
    } finally {
      setGlobalLoading(false);
    }
  }

  async function loadUsers() {
    setUsersLoading(true);
    try {
      const qs = new URLSearchParams();
      if (userSearch.trim()) qs.set("search", userSearch.trim());
      const data = await apiFetch(`/api/users${qs.toString() ? `?${qs.toString()}` : ""}`);
      setUsers(Array.isArray(data) ? data : []);
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to load users" });
      setUsers([]);
    } finally {
      setUsersLoading(false);
    }
  }

  async function saveUser() {
    const userId = (userDraft.userId || "").trim();
    const homeLeagueId = (userDraft.homeLeagueId || "").trim();
    const role = (userDraft.role || "").trim();
    if (!userId) return setToast({ tone: "error", message: "User ID is required." });
    if (role && !homeLeagueId) return setToast({ tone: "error", message: "Home league is required when setting a role." });

    try {
      await apiFetch("/api/users", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId,
          email: (userDraft.email || "").trim(),
          homeLeagueId,
          role: role || undefined,
        }),
      });
      setToast({ tone: "success", message: `Saved user ${userId}.` });
      trackEvent("ui_admin_user_save", { userId });
      setUserDraft({ userId: "", email: "", homeLeagueId: "", role: "" });
      await loadUsers();
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to save user" });
    }
  }

  async function loadAllMemberships() {
    setMembersLoadingAll(true);
    try {
      const qs = new URLSearchParams();
      qs.set("all", "true");
      if (memberSearch.trim()) qs.set("search", memberSearch.trim());
      if (memberLeague.trim()) qs.set("leagueId", memberLeague.trim());
      if (memberRole.trim()) qs.set("role", memberRole.trim());
      const data = await apiFetch(`/api/memberships?${qs.toString()}`);
      setMembersAll(Array.isArray(data) ? data : []);
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to load memberships" });
      setMembersAll([]);
    } finally {
      setMembersLoadingAll(false);
    }
  }

  async function createLeague() {
    setGlobalErr("");
    setGlobalOk("");
    const leagueId = (newLeague.leagueId || "").trim();
    const name = (newLeague.name || "").trim();
    const timezone = "America/New_York";
    if (!leagueId || !name) return setGlobalErr("leagueId and name are required.");

    setGlobalLoading(true);
    try {
      await apiFetch("/api/global/leagues", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ leagueId, name, timezone }),
      });
      setGlobalOk(`Created league ${leagueId}.`);
      setToast({ tone: "success", message: `Created league ${leagueId}.` });
      trackEvent("ui_admin_create_league", { leagueId });
      setNewLeague({ leagueId: "", name: "" });
      await loadGlobalLeagues();
    } catch (e) {
      setGlobalErr(e?.message || "Create league failed");
    } finally {
      setGlobalLoading(false);
    }
  }

  async function deleteLeague(league) {
    const id = (league?.leagueId || "").trim();
    if (!id) return;
    const ok = window.confirm(`Delete league ${id}? This deletes all league data and cannot be undone.`);
    if (!ok) return;
    setGlobalLoading(true);
    setGlobalErr("");
    setGlobalOk("");
    try {
      await apiFetch(`/api/global/leagues/${encodeURIComponent(id)}`, { method: "DELETE" });
      setGlobalOk(`Deleted league ${id}.`);
      setToast({ tone: "success", message: `Deleted league ${id}.` });
      trackEvent("ui_admin_delete_league", { leagueId: id });
      if (seasonLeagueId === id) setSeasonLeagueId("");
      await loadGlobalLeagues();
    } catch (e) {
      setGlobalErr(e?.message || "Delete league failed");
    } finally {
      setGlobalLoading(false);
    }
  }

  function applySeasonFromLeague(league) {
    const season = league?.season || {};
    setSeasonDraft({
      springStart: season.springStart || "",
      springEnd: season.springEnd || "",
      fallStart: season.fallStart || "",
      fallEnd: season.fallEnd || "",
      gameLengthMinutes: season.gameLengthMinutes || 0,
    });
    setBlackoutsDraft(Array.isArray(season.blackouts) ? season.blackouts.map((b) => ({
      startDate: b.startDate || "",
      endDate: b.endDate || "",
      label: b.label || "",
    })) : []);
  }

  useEffect(() => {
    if (!seasonLeagueId || !globalLeagues.length) return;
    const league = globalLeagues.find((l) => l.leagueId === seasonLeagueId);
    if (league) applySeasonFromLeague(league);
  }, [seasonLeagueId, globalLeagues]);

  async function saveSeasonConfig() {
    if (!seasonLeagueId) return;
    setGlobalErr("");
    setGlobalOk("");
    const payload = {
      season: {
        springStart: (seasonDraft.springStart || "").trim(),
        springEnd: (seasonDraft.springEnd || "").trim(),
        fallStart: (seasonDraft.fallStart || "").trim(),
        fallEnd: (seasonDraft.fallEnd || "").trim(),
        gameLengthMinutes: Number(seasonDraft.gameLengthMinutes) || 0,
        blackouts: blackoutsDraft.map((b) => ({
          startDate: (b.startDate || "").trim(),
          endDate: (b.endDate || "").trim(),
          label: (b.label || "").trim(),
        })),
      },
    };

    setGlobalLoading(true);
    try {
      await apiFetch(`/api/global/leagues/${encodeURIComponent(seasonLeagueId)}/season`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setGlobalOk(`Updated season settings for ${seasonLeagueId}.`);
      setToast({ tone: "success", message: `Updated season settings for ${seasonLeagueId}.` });
      trackEvent("ui_admin_season_save", { leagueId: seasonLeagueId });
      await loadGlobalLeagues();
    } catch (e) {
      setGlobalErr(e?.message || "Update season settings failed");
    } finally {
      setGlobalLoading(false);
    }
  }

  async function approve(req, roleOverride) {
    const userId = req?.userId || "";
    const role = (roleOverride || req?.requestedRole || "Viewer").trim();
    const targetLeagueId = (req?.leagueId || leagueId || "").trim();
    if (!userId) return;
    try {
      await apiFetch(`/api/accessrequests/${encodeURIComponent(userId)}/approve`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          ...(targetLeagueId ? { [LEAGUE_HEADER_NAME]: targetLeagueId } : {}),
        },
        body: JSON.stringify({ role }),
      });
      await load();
      setToast({ tone: "success", message: "Access request approved." });
      trackEvent("ui_admin_access_request_approve", {
        leagueId: targetLeagueId,
        userId,
        role,
      });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Approve failed" });
    }
  }

  async function deny(req) {
    const userId = req?.userId || "";
    const targetLeagueId = (req?.leagueId || leagueId || "").trim();
    if (!userId) return;
    const reason = await requestPrompt({
      title: "Deny access request",
      message: "Optional reason for denial.",
      placeholder: "Reason (optional)",
      confirmLabel: "Deny",
    });
    if (reason === null) return;
    try {
      await apiFetch(`/api/accessrequests/${encodeURIComponent(userId)}/deny`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          ...(targetLeagueId ? { [LEAGUE_HEADER_NAME]: targetLeagueId } : {}),
        },
        body: JSON.stringify({ reason }),
      });
      await load();
      setToast({ tone: "success", message: "Access request denied." });
      trackEvent("ui_admin_access_request_deny", {
        leagueId: targetLeagueId,
        userId,
      });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Deny failed" });
    }
  }

  const filteredItems = useMemo(() => {
    if (!accessAll || !accessLeagueFilter) return items;
    return (items || []).filter((r) => (r.leagueId || "") === accessLeagueFilter);
  }, [items, accessAll, accessLeagueFilter]);

  const sorted = useMemo(() => {
    return [...filteredItems].sort((a, b) => (b.updatedUtc || "").localeCompare(a.updatedUtc || ""));
  }, [filteredItems]);

  const accessLeagueOptions = useMemo(() => {
    return [...(globalLeagues || [])].sort((a, b) => {
      const nameA = (a.name || a.leagueId || "").toLowerCase();
      const nameB = (b.name || b.leagueId || "").toLowerCase();
      return nameA.localeCompare(nameB);
    });
  }, [globalLeagues]);


  const coaches = useMemo(() => {
    return (memberships || []).filter((m) => (m.role || "").toLowerCase() === "coach");
  }, [memberships]);

  const teamsByDivision = useMemo(() => {
    const map = new Map();
    for (const t of teams || []) {
      const div = (t.division || "").trim();
      if (!div) continue;
      if (!map.has(div)) map.set(div, []);
      map.get(div).push(t);
    }
    for (const [k, v] of map.entries()) {
      v.sort((a, b) => (a.name || a.teamId || "").localeCompare(b.name || b.teamId || ""));
      map.set(k, v);
    }
    return map;
  }, [teams]);

  function setDraftForCoach(userId, patch) {
    setCoachDraft((prev) => {
      const cur = prev[userId] || { division: "", teamId: "" };
      return { ...prev, [userId]: { ...cur, ...patch } };
    });
  }

  async function saveCoachAssignment(userId) {
    const draft = coachDraft[userId] || { division: "", teamId: "" };
    const division = (draft.division || "").trim();
    const teamId = (draft.teamId || "").trim();
    const body = division && teamId ? { team: { division, teamId } } : { team: null };

    try {
      await apiFetch(`/api/memberships/${encodeURIComponent(userId)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      await loadMembershipsAndTeams();
      setToast({ tone: "success", message: "Coach assignment updated." });
      trackEvent("ui_admin_membership_assign", {
        leagueId,
        userId,
        division,
        teamId,
      });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to update coach assignment" });
    }
  }

  async function clearCoachAssignment(userId) {
    setDraftForCoach(userId, { division: "", teamId: "" });
    await saveCoachAssignment(userId);
  }

  async function importSlotsCsv() {
    setSlotsErr("");
    setSlotsOk("");
    setSlotsErrors([]);
    setSlotsWarnings([]);
    if (!slotsFile) return setSlotsErr("Choose a CSV file to upload.");

    setSlotsBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", slotsFile);
      const res = await apiFetch("/api/import/slots", { method: "POST", body: fd });
      setSlotsOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      if (Array.isArray(res?.errors) && res.errors.length) setSlotsErrors(res.errors);
      if (Array.isArray(res?.warnings) && res.warnings.length) setSlotsWarnings(res.warnings);
      trackEvent("ui_admin_import_slots_success", {
        leagueId,
        upserted: res?.upserted ?? 0,
        rejected: res?.rejected ?? 0,
        skipped: res?.skipped ?? 0,
      });
    } catch (e) {
      setSlotsErr(e?.message || "Import failed");
    } finally {
      setSlotsBusy(false);
    }
  }

  async function importTeamsCsv() {
    setTeamsErr("");
    setTeamsOk("");
    setTeamsErrors([]);
    if (!teamsFile) return setTeamsErr("Choose a CSV file to upload.");

    setTeamsBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", teamsFile);
      const res = await apiFetch("/api/import/teams", { method: "POST", body: fd });
      setTeamsOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      if (Array.isArray(res?.errors) && res.errors.length) setTeamsErrors(res.errors);
      await loadMembershipsAndTeams();
      trackEvent("ui_admin_import_teams_success", {
        leagueId,
        upserted: res?.upserted ?? 0,
        rejected: res?.rejected ?? 0,
        skipped: res?.skipped ?? 0,
      });
    } catch (e) {
      setTeamsErr(e?.message || "Import failed");
    } finally {
      setTeamsBusy(false);
    }
  }

  function downloadTeamsTemplate() {
    const csv = buildTeamsTemplateCsv(divisions);
    const safeLeague = (leagueId || "league").replace(/[^a-z0-9_-]+/gi, "_");
    downloadCsv(csv, `teams_template_${safeLeague}.csv`);
    trackEvent("ui_admin_teams_template_download", { leagueId });
  }

  return (
    <div className="page">
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />
      <PromptDialog
        open={!!promptState}
        title={promptState?.title}
        message={promptState?.message}
        placeholder={promptState?.placeholder}
        confirmLabel={promptState?.confirmLabel}
        cancelLabel={promptState?.cancelLabel}
        value={promptValue}
        onChange={setPromptValue}
        onConfirm={handleConfirm}
        onCancel={handleCancel}
      />

      <AccessRequestsSection
        leagueId={leagueId}
        setLeagueId={setLeagueId}
        me={me}
        isGlobalAdmin={isGlobalAdmin}
        accessStatus={accessStatus}
        setAccessStatus={setAccessStatus}
        accessScope={accessScope}
        setAccessScope={setAccessScope}
        accessLeagueFilter={accessLeagueFilter}
        setAccessLeagueFilter={setAccessLeagueFilter}
        accessLeagueOptions={accessLeagueOptions}
        loading={loading}
        err={err}
        sorted={sorted}
        load={load}
        loadMembershipsAndTeams={loadMembershipsAndTeams}
        memLoading={memLoading}
        approve={approve}
        deny={deny}
      />

      {isGlobalAdmin && (
        <GlobalAdminSection
          globalErr={globalErr}
          globalOk={globalOk}
          newLeague={newLeague}
          setNewLeague={setNewLeague}
          createLeague={createLeague}
          globalLoading={globalLoading}
          loadGlobalLeagues={loadGlobalLeagues}
          globalLeagues={globalLeagues}
          deleteLeague={deleteLeague}
          seasonLeagueId={seasonLeagueId}
          setSeasonLeagueId={setSeasonLeagueId}
          seasonDraft={seasonDraft}
          setSeasonDraft={setSeasonDraft}
          blackoutsDraft={blackoutsDraft}
          setBlackoutsDraft={setBlackoutsDraft}
          saveSeasonConfig={saveSeasonConfig}
          applySeasonFromLeague={applySeasonFromLeague}
          userSearch={userSearch}
          setUserSearch={setUserSearch}
          loadUsers={loadUsers}
          usersLoading={usersLoading}
          userDraft={userDraft}
          setUserDraft={setUserDraft}
          saveUser={saveUser}
          users={users}
          memberSearch={memberSearch}
          setMemberSearch={setMemberSearch}
          memberLeague={memberLeague}
          setMemberLeague={setMemberLeague}
          memberRole={memberRole}
          setMemberRole={setMemberRole}
          loadAllMemberships={loadAllMemberships}
          membersLoadingAll={membersLoadingAll}
          membersAll={membersAll}
        />
      )}

      <CoachAssignmentsSection
        memLoading={memLoading}
        coaches={coaches}
        divisions={divisions}
        teamsByDivision={teamsByDivision}
        coachDraft={coachDraft}
        setDraftForCoach={setDraftForCoach}
        saveCoachAssignment={saveCoachAssignment}
        clearCoachAssignment={clearCoachAssignment}
      />

      <CsvImportSection
        leagueId={leagueId}
        slotsFile={slotsFile}
        setSlotsFile={setSlotsFile}
        slotsBusy={slotsBusy}
        slotsErr={slotsErr}
        slotsOk={slotsOk}
        slotsErrors={slotsErrors}
        slotsWarnings={slotsWarnings}
        importSlotsCsv={importSlotsCsv}
        teamsFile={teamsFile}
        setTeamsFile={setTeamsFile}
        teamsBusy={teamsBusy}
        teamsErr={teamsErr}
        teamsOk={teamsOk}
        teamsErrors={teamsErrors}
        importTeamsCsv={importTeamsCsv}
        downloadTeamsTemplate={downloadTeamsTemplate}
      />
    </div>
  );
}
