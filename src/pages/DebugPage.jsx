import { useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";

const ROLE_OPTIONS = ["", "LeagueAdmin", "Coach", "Viewer"];
const PRACTICE_REQUEST_LIMIT = 3;
const WEEKDAY_FILTER_OPTIONS = [
  { key: "", label: "All days" },
  { key: "1", label: "Monday" },
  { key: "2", label: "Tuesday" },
  { key: "3", label: "Wednesday" },
  { key: "4", label: "Thursday" },
  { key: "5", label: "Friday" },
  { key: "6", label: "Saturday" },
  { key: "0", label: "Sunday" },
];

function normalizeText(value) {
  return (value || "").trim();
}

function slotSortKey(slot) {
  return `${normalizeText(slot?.date || slot?.gameDate)} ${normalizeText(slot?.startTime)}`.trim();
}

function sortSlotsBySchedule(slots) {
  return [...slots].sort((a, b) => slotSortKey(a).localeCompare(slotSortKey(b)));
}

function weekdayKeyFromDate(isoDate) {
  const parts = (isoDate || "").split("-");
  if (parts.length !== 3) return "";
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return "";
  const date = new Date(Date.UTC(year, month - 1, day));
  return String(date.getUTCDay());
}

function weekdayLabelFromDate(isoDate) {
  const key = weekdayKeyFromDate(isoDate);
  return WEEKDAY_FILTER_OPTIONS.find((opt) => opt.key === key)?.label || "";
}

function formatSlotTime(slot) {
  const start = normalizeText(slot?.startTime);
  const end = normalizeText(slot?.endTime);
  if (!start || !end) return "";
  return `${start} - ${end}`;
}

function formatSlotLocation(slot) {
  return normalizeText(slot?.displayName) || normalizeText(slot?.fieldName) || normalizeText(slot?.fieldId) || normalizeText(slot?.fieldKey);
}

function requestSortKey(request) {
  return `${normalizeText(request?.date || request?.slot?.gameDate)} ${normalizeText(request?.startTime || request?.slot?.startTime)}`.trim();
}

function isActivePracticeRequest(request) {
  const status = normalizeText(request?.status).toLowerCase();
  return status === "pending" || status === "approved";
}

function buildDebugPracticePreview(adminView, division, teamId) {
  const requests = (Array.isArray(adminView?.requests) ? adminView.requests : [])
    .filter(
      (request) =>
        normalizeText(request?.division) === division &&
        normalizeText(request?.teamId) === teamId
    )
    .sort((a, b) => requestSortKey(b).localeCompare(requestSortKey(a)));

  const activeSlotIds = new Set(
    requests
      .filter(isActivePracticeRequest)
      .map((request) => `${normalizeText(request?.division)}|${normalizeText(request?.slotId)}`)
      .filter(Boolean)
  );

  const slots = sortSlotsBySchedule(
    (Array.isArray(adminView?.slots) ? adminView.slots : [])
      .filter((slot) => normalizeText(slot?.division) === division)
      .filter((slot) => {
        const state = normalizeText(slot?.normalizationState).toLowerCase();
        return state === "ready" || state === "normalized";
      })
      .filter((slot) => normalizeText(slot?.bookingPolicy).toLowerCase() !== "not_requestable")
      .filter((slot) => {
        const key = `${normalizeText(slot?.division)}|${normalizeText(slot?.slotId)}`;
        return Number(slot?.remainingCapacity || 0) > 0 || activeSlotIds.has(key);
      })
  );

  return { requests, slots };
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

function parseJsonString(value, fallback = null) {
  if (value == null) return fallback;
  if (typeof value === "object") return value;
  const text = String(value || "").trim();
  if (!text) return fallback;
  try {
    return JSON.parse(text);
  } catch {
    return fallback;
  }
}

function csvCell(value) {
  const text = String(value ?? "");
  if (!/[",\n]/.test(text)) return text;
  return `"${text.replace(/"/g, '""')}"`;
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
  const [previewPortalDayFilter, setPreviewPortalDayFilter] = useState("");
  const [previewRefreshedAt, setPreviewRefreshedAt] = useState("");
  const [repairAuditLoading, setRepairAuditLoading] = useState(false);
  const [repairAuditErr, setRepairAuditErr] = useState("");
  const [repairAuditDivision, setRepairAuditDivision] = useState("");
  const [repairAuditLimit, setRepairAuditLimit] = useState("100");
  const [repairAuditIncludeWizardRuns, setRepairAuditIncludeWizardRuns] = useState(false);
  const [repairAuditRows, setRepairAuditRows] = useState([]);
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
    return previewRequests.filter(isActivePracticeRequest);
  }, [previewRequests]);

  const activeRequestSlotIds = useMemo(() => {
    return new Set(
      activePracticeRequests
        .map((request) => `${normalizeText(request?.division)}|${normalizeText(request?.slotId)}`)
        .filter((value) => value !== "|")
    );
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

  const debugSummary = useMemo(() => ({
    memberships: memberships.length,
    globalAdmins: globalAdmins.length,
    users: users.length,
    membershipRows: membershipsAll.length,
    previewRequests: previewRequests.length,
    repairRows: repairAuditRows.length,
  }), [memberships.length, globalAdmins.length, users.length, membershipsAll.length, previewRequests.length, repairAuditRows.length]);

  const coachSetupLink = useMemo(() => {
    return buildCoachSetupLink(leagueId, previewTeamId);
  }, [leagueId, previewTeamId]);

  const previewVisibleSlots = useMemo(() => {
    const dayKey = normalizeText(previewPortalDayFilter);
    if (!dayKey) return previewSlots;
    return previewSlots.filter(
      (slot) => weekdayKeyFromDate(normalizeText(slot?.date || slot?.gameDate)) === dayKey
    );
  }, [previewSlots, previewPortalDayFilter]);

  const repairAuditRowsNormalized = useMemo(() => {
    return (repairAuditRows || []).map((row) => {
      const recordType = normalizeText(row?.RecordType);
      const fixesRuleIds = parseJsonString(row?.FixesRuleIds, []);
      const changes = parseJsonString(row?.Changes, []);
      const beforeRuleHealth = parseJsonString(row?.RuleHealthBefore, null);
      const afterRuleHealth = parseJsonString(row?.RuleHealthAfter, null);
      const beforeAfterSummary = parseJsonString(row?.BeforeAfterSummary, {});
      const createdUtc = normalizeText(row?.CreatedUtc) || normalizeText(row?.Timestamp);
      const hardBefore = Number(row?.HardViolationsBefore ?? beforeRuleHealth?.hardViolationCount ?? 0);
      const hardAfter = Number(row?.HardViolationsAfter ?? afterRuleHealth?.hardViolationCount ?? 0);
      const softBefore = Number(row?.SoftScoreBefore ?? beforeRuleHealth?.softScore ?? 0);
      const softAfter = Number(row?.SoftScoreAfter ?? afterRuleHealth?.softScore ?? 0);
      const proposalTitle = normalizeText(row?.ProposalTitle);
      const proposalId = normalizeText(row?.ProposalId);
      const deltaHard = Number.isFinite(hardBefore) && Number.isFinite(hardAfter) ? (hardAfter - hardBefore) : null;
      const deltaSoft = Number.isFinite(softBefore) && Number.isFinite(softAfter)
        ? Number((softAfter - softBefore).toFixed(2))
        : null;
      return {
        raw: row,
        recordType,
        createdUtc,
        division: normalizeText(row?.Division),
        runId: normalizeText(row?.RunId || row?.RowKey),
        requestId: normalizeText(row?.RequestId),
        proposalId,
        proposalTitle,
        proposalRationale: normalizeText(row?.ProposalRationale),
        fixesRuleIds: Array.isArray(fixesRuleIds) ? fixesRuleIds : [],
        changes: Array.isArray(changes) ? changes : [],
        beforeRuleHealth,
        afterRuleHealth,
        beforeAfterSummary: beforeAfterSummary && typeof beforeAfterSummary === "object" ? beforeAfterSummary : {},
        hardBefore,
        hardAfter,
        softBefore,
        softAfter,
        deltaHard,
        deltaSoft,
        applyBlockedBefore: row?.ApplyBlockedBefore === true || String(row?.ApplyBlockedBefore).toLowerCase() === "true",
        applyBlockedAfter: row?.ApplyBlockedAfter === true || String(row?.ApplyBlockedAfter).toLowerCase() === "true",
      };
    });
  }, [repairAuditRows]);

  const repairAuditExportCsv = useMemo(() => {
    const rows = [
      [
        "CreatedUtc",
        "Division",
        "RecordType",
        "ProposalTitle",
        "FixesRuleIds",
        "Changes",
        "HardBefore",
        "HardAfter",
        "HardDelta",
        "SoftScoreBefore",
        "SoftScoreAfter",
        "SoftDelta",
        "ApplyBlockedBefore",
        "ApplyBlockedAfter",
        "RequestId",
        "RunId",
      ].join(","),
    ];

    for (const row of repairAuditRowsNormalized) {
      rows.push([
        csvCell(row.createdUtc),
        csvCell(row.division),
        csvCell(row.recordType),
        csvCell(row.proposalTitle),
        csvCell((row.fixesRuleIds || []).join("|")),
        csvCell((row.changes || []).map((c) => c?.changeType).filter(Boolean).join("|")),
        csvCell(row.hardBefore),
        csvCell(row.hardAfter),
        csvCell(row.deltaHard),
        csvCell(row.softBefore),
        csvCell(row.softAfter),
        csvCell(row.deltaSoft),
        csvCell(row.applyBlockedBefore),
        csvCell(row.applyBlockedAfter),
        csvCell(row.requestId),
        csvCell(row.runId),
      ].join(","));
    }

    return rows.join("\n");
  }, [repairAuditRowsNormalized]);

  const repairAuditExportSummaryText = useMemo(() => {
    return repairAuditRowsNormalized.map((row) => {
      const title = row.proposalTitle || row.proposalId || row.recordType || row.runId;
      const hardPart = Number.isFinite(row.hardBefore) && Number.isFinite(row.hardAfter)
        ? `hard ${row.hardBefore} -> ${row.hardAfter}`
        : "hard ?";
      const softPart = Number.isFinite(row.softBefore) && Number.isFinite(row.softAfter)
        ? `soft ${row.softBefore} -> ${row.softAfter}`
        : "soft ?";
      return `${row.createdUtc || "-"} | ${row.division || "-"} | ${title} | ${hardPart} | ${softPart}`;
    }).join("\n");
  }, [repairAuditRowsNormalized]);

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
      void loadAllMemberships(userId, { leagueId: "", role: "" });
    }
    setMemberDraft({
      userId,
      email: (user?.email || "").trim(),
      leagueId: "",
      role: "",
    });
  }

  async function loadAllMemberships(userIdOverride = memberSearch, filterOverrides = null) {
    if (!isGlobalAdmin) return;
    const userId = (typeof userIdOverride === "string" ? userIdOverride : memberSearch || "").trim();
    const leagueFilter = typeof filterOverrides?.leagueId === "string"
      ? filterOverrides.leagueId.trim()
      : memberLeague.trim();
    const roleFilter = typeof filterOverrides?.role === "string"
      ? filterOverrides.role.trim()
      : memberRole.trim();
    if (!userId) {
      setMemberErr("Enter an exact user ID or choose Memberships from the user list.");
      setMembershipsAll([]);
      return;
    }
    setMembersLoadingAll(true);
    setMemberErr("");
    try {
      const qs = new URLSearchParams();
      qs.set("all", "true");
      qs.set("userId", userId);
      if (leagueFilter) qs.set("leagueId", leagueFilter);
      if (roleFilter) qs.set("role", roleFilter);
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
      await loadAllMemberships(userId);
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
      await loadAllMemberships(userId);
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
      const adminView = await apiFetch("/api/field-inventory/practice/admin");
      const preview = buildDebugPracticePreview(adminView, division, teamId);

      if (requestSeq !== previewLoadSeqRef.current) return;
      setPreviewRequests(preview.requests);
      setPreviewSlots(preview.slots);
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

  async function loadScheduleRepairAudits() {
    if (!isGlobalAdmin) return;
    if (!leagueId) {
      setRepairAuditErr("Select a league to load schedule repair audit history.");
      setRepairAuditRows([]);
      return;
    }

    setRepairAuditLoading(true);
    setRepairAuditErr("");
    try {
      const qs = new URLSearchParams();
      if (normalizeText(repairAuditDivision)) qs.set("division", normalizeText(repairAuditDivision));
      const parsedLimit = Number(repairAuditLimit);
      if (Number.isFinite(parsedLimit) && parsedLimit > 0) qs.set("limit", String(Math.trunc(parsedLimit)));
      if (repairAuditIncludeWizardRuns) qs.set("includeWizardRuns", "1");

      const data = await apiFetch(`/api/admin/debug/league/${encodeURIComponent(leagueId)}/schedule-repair-audits?${qs.toString()}`);
      const rows = Array.isArray(data?.rows) ? data.rows : (Array.isArray(data) ? data : []);
      setRepairAuditRows(rows);
    } catch (e) {
      setRepairAuditErr(e?.message || "Failed to load schedule repair audits.");
      setRepairAuditRows([]);
    } finally {
      setRepairAuditLoading(false);
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGlobalAdmin]);

  return (
    <div className="page">
      <div className="card">
        <div className="card__header">
          <div className="h2">Debug workspace</div>
          <div className="subtle">Operational diagnostics for memberships, coach setup previews, and scheduler repair audit history.</div>
        </div>
        <div className="layoutStatRow">
          <div className="layoutStat">
            <div className="layoutStat__value">{debugSummary.memberships}</div>
            <div className="layoutStat__label">League memberships</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{debugSummary.globalAdmins}</div>
            <div className="layoutStat__label">Global admins</div>
          </div>
          {isGlobalAdmin ? (
            <>
              <div className="layoutStat">
                <div className="layoutStat__value">{debugSummary.users}</div>
                <div className="layoutStat__label">Loaded users</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{debugSummary.membershipRows}</div>
                <div className="layoutStat__label">Membership rows</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{debugSummary.previewRequests}</div>
                <div className="layoutStat__label">Preview requests</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{debugSummary.repairRows}</div>
                <div className="layoutStat__label">Repair audit rows</div>
              </div>
            </>
          ) : null}
        </div>
      </div>
      {isGlobalAdmin ? (
        <div className="card">
          <div className="card__header">
            <div className="h2">Debug: user management</div>
            <div className="subtle">Search users, update home league defaults, and assign league roles.</div>
          </div>

          <div className="controlBand mb-2">
            <div className="row gap-3 row--wrap">
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
          </div>

          {userErr && <div className="callout callout--error">{userErr}</div>}
          {userOk && <div className="callout callout--ok">{userOk}</div>}

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
          <div className="card__header">
            <div className="h2">Debug: league memberships</div>
            <div className="subtle">Assign users to leagues and inspect one exact user partition at a time from <code>GameSwapMemberships</code>.</div>
          </div>

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
              User ID
              <input
                value={memberSearch}
                onChange={(e) => setMemberSearch(e.target.value)}
                placeholder="Exact user ID"
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
            <button
              className="btn"
              onClick={() => loadAllMemberships()}
              disabled={membersLoadingAll || !memberSearch.trim()}
            >
              {membersLoadingAll ? "Loading..." : "Load memberships"}
            </button>
          </div>

          {memberErr && <div className="callout callout--error">{memberErr}</div>}
          {memberOk && <div className="callout callout--ok">{memberOk}</div>}

          {membersLoadingAll ? (
            <div className="muted">Loading...</div>
          ) : !memberSearch.trim() ? (
            <div className="muted">Choose a user above or enter an exact user ID to inspect memberships.</div>
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
          <div className="card__header">
            <div className="h2">Debug: normalized coach practice preview</div>
            <div className="subtle">
              Preview the normalized coach practice experience by filtering the admin inventory-backed practice payload for a selected team and division.
            </div>
          </div>

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

          {previewContextErr && <div className="callout callout--error">{previewContextErr}</div>}
          {previewErr && <div className="callout callout--error">{previewErr}</div>}
          {previewOk && <div className="callout callout--ok">{previewOk}</div>}

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
            <div className="layoutStatRow mb-4">
              <div className="layoutStat">
                <div className="layoutStat__value">{previewStatusCounts.pending}</div>
                <div className="layoutStat__label">Pending requests</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{previewStatusCounts.approved}</div>
                <div className="layoutStat__label">Approved requests</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{previewStatusCounts.rejected}</div>
                <div className="layoutStat__label">Rejected requests</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{previewSlots.length}</div>
                <div className="layoutStat__label">Visible practice blocks</div>
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

              <div className="callout mb-3">
                <div className="font-semibold">Read-only normalized preview</div>
                <div className="muted mt-1">
                  This debug view now mirrors the inventory-backed coach workflow. It does not use legacy direct-claim overrides.
                </div>
              </div>

              <div className="card__header">
                <div className="h2">Practice requests for this team</div>
              </div>
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
                        <th>Policy</th>
                        <th>Notes</th>
                      </tr>
                    </thead>
                    <tbody>
                      {previewRequests.map((request) => (
                        <tr key={request.requestId}>
                          <td>{request.status || ""}</td>
                          <td>{request.date || ""}</td>
                          <td>{formatSlotTime(request)}</td>
                          <td>
                            <div>{request.fieldName || "-"}</div>
                            {request.isMove && request.moveFromDate ? (
                              <div className="muted text-xs">
                                Move from {request.moveFromDate} {request.moveFromStartTime || "-"}-{request.moveFromEndTime || "-"} {request.moveFromFieldName || ""}
                              </div>
                            ) : null}
                          </td>
                          <td>{request.bookingPolicyLabel || "-"}</td>
                          <td>{request.notes || request.reviewReason || "-"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <div className="card__header">
                <div className="h2">Requestable normalized practice blocks</div>
                <div className="subtle">
                  These are the blocks the selected coach would see after division scoping, requestability, normalization, and capacity checks.
                </div>
              </div>

              <div className="row gap-3 row--wrap mb-3">
                <label className="min-w-[220px]">
                  Filter by day
                  <select
                    value={previewPortalDayFilter}
                    onChange={(e) => setPreviewPortalDayFilter(normalizeText(e.target.value))}
                  >
                    {WEEKDAY_FILTER_OPTIONS.map((opt) => (
                      <option key={opt.key || "all"} value={opt.key}>
                        {opt.label}
                      </option>
                    ))}
                  </select>
                </label>
              </div>

              {previewVisibleSlots.length === 0 ? (
                <div className="muted">
                  {previewPortalDayFilter
                    ? "No normalized practice blocks match the selected day."
                    : "No normalized practice blocks are currently visible for this division/team."}
                </div>
              ) : (
                <div className="tableWrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Date</th>
                        <th>Day</th>
                        <th>Time</th>
                        <th>Location</th>
                        <th>Policy</th>
                        <th>Capacity</th>
                        <th>State</th>
                      </tr>
                    </thead>
                    <tbody>
                      {previewVisibleSlots.map((slot) => {
                        const slotId = normalizeText(slot?.slotId);
                        const slotKey = `${normalizeText(slot?.division)}|${slotId}`;
                        const alreadyRequested = activeRequestSlotIds.has(slotKey);
                        return (
                          <tr key={slotId || `${slot?.date}-${slot?.startTime}`}>
                            <td>{slot.date || ""}</td>
                            <td>{slot.dayOfWeek || weekdayLabelFromDate(normalizeText(slot?.date))}</td>
                            <td>{formatSlotTime(slot)}</td>
                            <td>{formatSlotLocation(slot)}</td>
                            <td>{slot.bookingPolicyLabel || "-"}</td>
                            <td>
                              {Number(slot.remainingCapacity || 0)}/{Number(slot.capacity || 0)}
                              {alreadyRequested ? <div className="muted text-xs">Already requested by this team</div> : null}
                            </td>
                            <td>{slot.normalizationState || "-"}</td>
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

      {isGlobalAdmin ? (
        <div className="card">
          <div className="card__header">
            <div className="h2">Debug: wizard preview repair audit history</div>
            <div className="subtle">
              Review <code>GameSwapScheduleRuns</code> records with <code>RecordType = WizardPreviewRepair</code> to inspect fixes and rule-health deltas.
            </div>
          </div>

          <div className="row gap-3 row--wrap mb-2">
            <label>
              Division (optional)
              <input
                value={repairAuditDivision}
                onChange={(e) => setRepairAuditDivision(e.target.value)}
                placeholder="e.g. 12U"
              />
            </label>
            <label>
              Limit
              <input
                value={repairAuditLimit}
                onChange={(e) => setRepairAuditLimit(e.target.value)}
                inputMode="numeric"
                className="w-[110px]"
              />
            </label>
            <label className="inlineCheck inlineCheck--compact pt-7">
              <input
                type="checkbox"
                checked={repairAuditIncludeWizardRuns}
                onChange={(e) => setRepairAuditIncludeWizardRuns(e.target.checked)}
              />
              <span>Include normal wizard runs</span>
            </label>
            <button className="btn" onClick={loadScheduleRepairAudits} disabled={repairAuditLoading}>
              {repairAuditLoading ? "Loading..." : "Load repair audits"}
            </button>
          </div>

          {repairAuditErr && <div className="callout callout--error">{repairAuditErr}</div>}

          {repairAuditRowsNormalized.length === 0 ? (
            <div className="muted">
              {repairAuditLoading ? "Loading..." : "No schedule repair audit entries loaded yet."}
            </div>
          ) : (
            <>
              <div className="callout mb-3">
                Loaded <b>{repairAuditRowsNormalized.length}</b> row(s).
                {" "}Preview repairs: <b>{repairAuditRowsNormalized.filter((r) => r.recordType === "WizardPreviewRepair").length}</b>.
                {" "}Avg hard delta: <b>{
                  (() => {
                    const deltas = repairAuditRowsNormalized
                      .map((r) => r.deltaHard)
                      .filter((n) => Number.isFinite(n));
                    if (!deltas.length) return "-";
                    const avg = deltas.reduce((sum, n) => sum + n, 0) / deltas.length;
                    return avg.toFixed(2);
                  })()
                }</b>
              </div>

              <div className="mb-3">
                <div className="font-semibold">Audit export (summary copy/paste)</div>
                <div className="muted mt-1">
                  One line per audit row showing proposal and rule-health before/after.
                </div>
                <textarea
                  readOnly
                  value={repairAuditExportSummaryText || ""}
                  rows={Math.min(14, Math.max(4, (repairAuditExportSummaryText || "").split("\n").filter(Boolean).length + 1))}
                  className="textareaMono mt-2"
                />
              </div>

              <div className="mb-3">
                <div className="font-semibold">Audit export CSV</div>
                <div className="muted mt-1">
                  Copy into Sheets/Excel for sorting and review.
                </div>
                <textarea
                  readOnly
                  value={repairAuditExportCsv || ""}
                  rows={8}
                  className="textareaMono mt-2"
                />
              </div>

              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Created</th>
                      <th>Division</th>
                      <th>Type</th>
                      <th>Proposal</th>
                      <th>Rules</th>
                      <th>Changes</th>
                      <th>Hard</th>
                      <th>Soft</th>
                      <th>Request</th>
                    </tr>
                  </thead>
                  <tbody>
                    {repairAuditRowsNormalized.map((row) => (
                      <tr key={row.runId || row.requestId || `${row.createdUtc}-${row.proposalId}`}>
                        <td>{row.createdUtc ? new Date(row.createdUtc).toLocaleString() : ""}</td>
                        <td>{row.division || ""}</td>
                        <td>{row.recordType || ""}</td>
                        <td>
                          <div className="font-semibold">{row.proposalTitle || row.proposalId || "(no proposal title)"}</div>
                          {row.proposalRationale ? <div className="muted text-xs">{row.proposalRationale}</div> : null}
                          {row.beforeAfterSummary?.priorityImpactSummary ? (
                            <div className="muted text-xs">Priority impact: {String(row.beforeAfterSummary.priorityImpactSummary)}</div>
                          ) : null}
                        </td>
                        <td>{(row.fixesRuleIds || []).join(", ") || "-"}</td>
                        <td>{Array.isArray(row.changes) ? row.changes.length : 0}</td>
                        <td>
                          {Number.isFinite(row.hardBefore) && Number.isFinite(row.hardAfter)
                            ? `${row.hardBefore} -> ${row.hardAfter}${Number.isFinite(row.deltaHard) ? ` (${row.deltaHard >= 0 ? "+" : ""}${row.deltaHard})` : ""}`
                            : "-"}
                        </td>
                        <td>
                          {Number.isFinite(row.softBefore) && Number.isFinite(row.softAfter)
                            ? `${Number(row.softBefore).toFixed(1)} -> ${Number(row.softAfter).toFixed(1)}${Number.isFinite(row.deltaSoft) ? ` (${row.deltaSoft >= 0 ? "+" : ""}${row.deltaSoft})` : ""}`
                            : "-"}
                        </td>
                        <td>
                          <div>{row.requestId || ""}</div>
                          <div className="muted text-xs">{row.runId || ""}</div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <details className="mt-4">
                <summary>Raw audit JSON</summary>
                <pre className="codeblock">{JSON.stringify(repairAuditRows, null, 2)}</pre>
              </details>
            </>
          )}
        </div>
      ) : null}

      <div className="card">
        <div className="card__header">
          <div className="h2">Debug: global admins</div>
          <div className="subtle">Data comes from <code>GameSwapGlobalAdmins</code> via <code>/api/globaladmins</code>.</div>
        </div>

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

        {globalErr && <div className="callout callout--error">{globalErr}</div>}
        {globalOk && <div className="callout callout--ok">{globalOk}</div>}
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
        <div className="card__header">
          <div className="h2">Debug: memberships table</div>
          <div className="subtle">Data comes from <code>GameSwapMemberships</code> for the active league via <code>/api/memberships</code>.</div>
        </div>

        <div className="row gap-3 row--wrap mb-2">
          <button className="btn" onClick={loadMemberships} disabled={loading}>
            Refresh
          </button>
        </div>

        {err && <div className="callout callout--error">{err}</div>}
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
