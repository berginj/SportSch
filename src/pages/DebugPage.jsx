import { useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import { readPagedItems } from "../lib/pagedResults";

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
  return `${normalizeText(slot?.gameDate)} ${normalizeText(slot?.startTime)}`.trim();
}

function sortSlotsBySchedule(slots) {
  return [...slots].sort((a, b) => slotSortKey(a).localeCompare(slotSortKey(b)));
}

function weekKeyFromDate(isoDate) {
  const parts = (isoDate || "").split("-");
  if (parts.length !== 3) return "";
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return "";
  const date = new Date(Date.UTC(year, month - 1, day));
  const dayNum = date.getUTCDay() || 7;
  date.setUTCDate(date.getUTCDate() + 4 - dayNum);
  const yearStart = new Date(Date.UTC(date.getUTCFullYear(), 0, 1));
  const weekNo = Math.ceil((((date - yearStart) / 86400000) + 1) / 7);
  return `${date.getUTCFullYear()}-W${String(weekNo).padStart(2, "0")}`;
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
  return normalizeText(slot?.displayName) || normalizeText(slot?.fieldName) || normalizeText(slot?.fieldKey);
}

function practicePatternKey(slot) {
  const weekday = weekdayKeyFromDate(normalizeText(slot?.gameDate));
  const fieldKey = normalizeText(slot?.fieldKey);
  const start = normalizeText(slot?.startTime);
  const end = normalizeText(slot?.endTime);
  if (!weekday || !fieldKey || !start || !end) return "";
  return `${weekday}|${fieldKey}|${start}|${end}`;
}

function isPracticeCapableAvailabilitySlot(slot) {
  if (slot?.isAvailability !== true) return false;
  if (normalizeText(slot?.status) !== "Open") return false;
  const allocationType = normalizeText(slot?.allocationSlotType || slot?.slotType).toLowerCase();
  if (!allocationType) return true;
  return allocationType === "practice" || allocationType === "both";
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
  const [previewPortalSlotsAll, setPreviewPortalSlotsAll] = useState([]);
  const [previewPortalOpenToShareField, setPreviewPortalOpenToShareField] = useState(false);
  const [previewPortalShareWithTeamId, setPreviewPortalShareWithTeamId] = useState("");
  const [previewPortalDayFilter, setPreviewPortalDayFilter] = useState("0");
  const [previewPortalClaimingSlotId, setPreviewPortalClaimingSlotId] = useState("");
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

  const previewPortalShareableTeams = useMemo(() => {
    const teamId = normalizeText(previewTeamId);
    return previewTeamOptions
      .filter((t) => normalizeText(t?.teamId) && normalizeText(t?.teamId) !== teamId)
      .map((t) => ({
        teamId: normalizeText(t?.teamId),
        name: normalizeText(t?.name),
      }));
  }, [previewTeamId, previewTeamOptions]);

  const previewPortalSelections = useMemo(() => {
    const teamId = normalizeText(previewTeamId);
    if (!teamId) return [];
    return sortSlotsBySchedule(
      (previewPortalSlotsAll || [])
        .filter((s) => normalizeText(s?.status) === "Confirmed")
        .filter((s) => normalizeText(s?.gameType).toLowerCase() === "practice")
        .filter((s) => {
          const confirmed = normalizeText(s?.confirmedTeamId);
          const offering = normalizeText(s?.offeringTeamId);
          return confirmed === teamId || offering === teamId;
        })
    );
  }, [previewPortalSlotsAll, previewTeamId]);

  const previewPortalSelectionsByWeek = useMemo(() => {
    const map = new Map();
    for (const slot of previewPortalSelections) {
      const key = weekKeyFromDate(normalizeText(slot?.gameDate));
      if (!key) continue;
      if (!map.has(key)) map.set(key, slot);
    }
    return map;
  }, [previewPortalSelections]);

  const previewPortalAvailableSlotsAll = useMemo(() => {
    return sortSlotsBySchedule(
      (previewPortalSlotsAll || []).filter(isPracticeCapableAvailabilitySlot)
    );
  }, [previewPortalSlotsAll]);

  const previewPortalAvailableSlots = useMemo(() => {
    return previewPortalAvailableSlotsAll.slice(0, 20);
  }, [previewPortalAvailableSlotsAll]);

  const previewPortalAvailableSlotsFiltered = useMemo(() => {
    const dayKey = normalizeText(previewPortalDayFilter);
    if (!dayKey) return previewPortalAvailableSlots;
    return previewPortalAvailableSlots.filter(
      (slot) => weekdayKeyFromDate(normalizeText(slot?.gameDate)) === dayKey
    );
  }, [previewPortalAvailableSlots, previewPortalDayFilter]);

  const previewPortalAvailableSlotsAllFiltered = useMemo(() => {
    const dayKey = normalizeText(previewPortalDayFilter);
    if (!dayKey) return previewPortalAvailableSlotsAll;
    return previewPortalAvailableSlotsAll.filter(
      (slot) => weekdayKeyFromDate(normalizeText(slot?.gameDate)) === dayKey
    );
  }, [previewPortalAvailableSlotsAll, previewPortalDayFilter]);

  const previewPortalSeasonWeekOrdinalByKey = useMemo(() => {
    const weekKeys = Array.from(new Set(
      previewPortalAvailableSlotsAll
        .map((slot) => weekKeyFromDate(normalizeText(slot?.gameDate)))
        .filter(Boolean)
    )).sort((a, b) => a.localeCompare(b));
    const map = new Map();
    weekKeys.forEach((key, index) => map.set(key, index + 1));
    return map;
  }, [previewPortalAvailableSlotsAll]);

  const previewPortalActiveRequestPatternByKey = useMemo(() => {
    const map = new Map();
    for (const request of activePracticeRequests) {
      const key = practicePatternKey(request?.slot);
      if (key && !map.has(key)) map.set(key, request);
    }
    return map;
  }, [activePracticeRequests]);

  const previewPortalRecurringChoices = useMemo(() => {
    const groups = new Map();
    for (const slot of previewPortalAvailableSlotsAllFiltered) {
      const key = practicePatternKey(slot);
      if (!key) continue;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(slot);
    }

    const rows = [];
    for (const [key, slots] of groups.entries()) {
      const slotsSorted = sortSlotsBySchedule(slots);
      const representativeSlot = slotsSorted[0] || null;
      if (!representativeSlot) continue;
      const weekOrdinals = slotsSorted
        .map((slot) => previewPortalSeasonWeekOrdinalByKey.get(weekKeyFromDate(normalizeText(slot?.gameDate))))
        .filter((n) => Number.isFinite(n));
      const minWeek = weekOrdinals.length ? Math.min(...weekOrdinals) : null;
      const maxWeek = weekOrdinals.length ? Math.max(...weekOrdinals) : null;
      rows.push({
        key,
        representativeSlot,
        weeksCount: slotsSorted.length,
        weekRangeLabel: minWeek && maxWeek ? (minWeek === maxWeek ? `W${minWeek}` : `W${minWeek}-W${maxWeek}`) : "-",
        firstDate: normalizeText(slotsSorted[0]?.gameDate),
        lastDate: normalizeText(slotsSorted[slotsSorted.length - 1]?.gameDate),
        existingRequest: previewPortalActiveRequestPatternByKey.get(key) || null,
      });
    }

    return rows.sort((a, b) => {
      const aRequested = !!a.existingRequest;
      const bRequested = !!b.existingRequest;
      if (aRequested !== bRequested) return aRequested ? -1 : 1;
      const aPriority = Number(a?.existingRequest?.priority || 99);
      const bPriority = Number(b?.existingRequest?.priority || 99);
      if (aPriority !== bPriority) return aPriority - bPriority;
      if (a.weeksCount !== b.weeksCount) return b.weeksCount - a.weeksCount;
      return slotSortKey(a.representativeSlot).localeCompare(slotSortKey(b.representativeSlot));
    });
  }, [previewPortalAvailableSlotsAllFiltered, previewPortalSeasonWeekOrdinalByKey, previewPortalActiveRequestPatternByKey]);

  const previewPortalGoogleFormOptionsText = useMemo(() => {
    const groups = new Map();
    for (const slot of previewPortalAvailableSlotsAll) {
      const key = practicePatternKey(slot);
      if (!key) continue;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(slot);
    }

    const lines = [];
    for (const slots of groups.values()) {
      const representativeSlot = sortSlotsBySchedule(slots)[0] || null;
      if (!representativeSlot) continue;
      const gameDate = normalizeText(representativeSlot?.gameDate);
      const startTime = normalizeText(representativeSlot?.startTime);
      const endTime = normalizeText(representativeSlot?.endTime);
      if (!gameDate || !startTime || !endTime) continue;
      const dayLabel = weekdayLabelFromDate(gameDate) || "";
      const location = formatSlotLocation(representativeSlot);
      lines.push({
        sortKey: slotSortKey(representativeSlot),
        text: `${dayLabel} ${gameDate} | ${startTime}-${endTime} | ${location}`,
      });
    }

    return lines
      .sort((a, b) => a.sortKey.localeCompare(b.sortKey))
      .map((row) => row.text)
      .join("\n");
  }, [previewPortalAvailableSlotsAll]);

  const previewPortalGoogleFormPatternOptionsText = useMemo(() => {
    const lines = previewPortalRecurringChoices.map((choice) => {
      const slot = choice?.representativeSlot || null;
      const dayLabel = weekdayLabelFromDate(normalizeText(slot?.gameDate)) || "Day";
      const location = formatSlotLocation(slot);
      const startTime = normalizeText(slot?.startTime);
      const endTime = normalizeText(slot?.endTime);
      const timeLabel = startTime && endTime ? `${startTime}-${endTime}` : (startTime || endTime || "-");
      return `${dayLabel} | ${location} | ${timeLabel}`;
    });
    return lines.join("\n");
  }, [previewPortalRecurringChoices]);

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

  useEffect(() => {
    if (!previewPortalOpenToShareField && previewPortalShareWithTeamId) {
      setPreviewPortalShareWithTeamId("");
      return;
    }
    if (!previewPortalOpenToShareField || !previewPortalShareWithTeamId) return;
    if (!previewPortalShareableTeams.some((t) => t.teamId === previewPortalShareWithTeamId)) {
      setPreviewPortalShareWithTeamId("");
    }
  }, [previewPortalOpenToShareField, previewPortalShareWithTeamId, previewPortalShareableTeams]);

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
      setPreviewPortalSlotsAll([]);
      return;
    }
    if (!division || !teamId) {
      setPreviewErr("Select both division and team.");
      setPreviewRequests([]);
      setPreviewSlots([]);
      setPreviewPortalSlotsAll([]);
      return;
    }

    setPreviewLoading(true);
    setPreviewErr("");
    setPreviewOk("");
    try {
      const [requestsRaw, slotsRaw] = await Promise.all([
        apiFetch(`/api/practice-requests?teamId=${encodeURIComponent(teamId)}`),
        apiFetch(`/api/slots?division=${encodeURIComponent(division)}&status=Open,Confirmed`)
      ]);

      const requests = Array.isArray(requestsRaw)
        ? [...requestsRaw].sort((a, b) => String(b?.requestedUtc || "").localeCompare(String(a?.requestedUtc || "")))
        : [];
      const allOpenSlots = readPagedItems(slotsRaw);
      const availabilitySlots = allOpenSlots.filter(
        (slot) => slot?.isAvailability === true && normalizeText(slot?.status) === "Open"
      );

      if (requestSeq !== previewLoadSeqRef.current) return;
      setPreviewRequests(requests);
      setPreviewSlots(sortSlotsBySchedule(availabilitySlots).slice(0, 20));
      setPreviewPortalSlotsAll(allOpenSlots);
      setPreviewRefreshedAt(new Date().toISOString());
    } catch (e) {
      if (requestSeq !== previewLoadSeqRef.current) return;
      setPreviewErr(e?.message || "Failed to load coach practice preview.");
      setPreviewRequests([]);
      setPreviewSlots([]);
      setPreviewPortalSlotsAll([]);
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
      setPreviewPortalSlotsAll([]);
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
        setPreviewPortalSlotsAll([]);
      }
    } catch (e) {
      setPreviewContextErr(e?.message || "Failed to load preview context.");
      setPreviewDivisions([]);
      setPreviewTeams([]);
      setPreviewRequests([]);
      setPreviewSlots([]);
      setPreviewPortalSlotsAll([]);
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
    setPreviewPortalSlotsAll([]);
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
    setPreviewPortalSlotsAll([]);
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

  async function claimPracticePortalPreviewOverride(slot) {
    const division = normalizeText(previewDivision);
    const teamId = normalizeText(previewTeamId);
    const slotId = normalizeText(slot?.slotId);
    if (!division || !teamId || !slotId) {
      setPreviewErr("Select division/team and a valid slot before using override.");
      return;
    }
    if (previewPortalOpenToShareField && !normalizeText(previewPortalShareWithTeamId)) {
      setPreviewErr('Select a team to propose sharing with, or uncheck "Open to sharing a field".');
      return;
    }

    const selectedDate = normalizeText(slot?.gameDate);
    const selectedWeekday = weekdayKeyFromDate(selectedDate);
    const selectedFieldKey = normalizeText(slot?.fieldKey);
    const selectedStart = normalizeText(slot?.startTime);
    const selectedEnd = normalizeText(slot?.endTime);
    const existingPracticeWeeks = new Set(Array.from(previewPortalSelectionsByWeek.keys()));

    const recurringCandidates = sortSlotsBySchedule(
      (previewPortalSlotsAll || [])
        .filter((s) => s?.isAvailability === true)
        .filter((s) => normalizeText(s?.status) === "Open")
        .filter((s) => normalizeText(s?.fieldKey) === selectedFieldKey)
        .filter((s) => normalizeText(s?.startTime) === selectedStart)
        .filter((s) => normalizeText(s?.endTime) === selectedEnd)
        .filter((s) => {
          const gameDate = normalizeText(s?.gameDate);
          if (!gameDate) return false;
          if (selectedDate && gameDate < selectedDate) return false;
          if (selectedWeekday && weekdayKeyFromDate(gameDate) !== selectedWeekday) return false;
          const weekKey = weekKeyFromDate(gameDate);
          if (!weekKey) return false;
          if (existingPracticeWeeks.has(weekKey)) return false;
          return true;
        })
    );

    const payload = {
      teamId,
      openToShareField: previewPortalOpenToShareField,
      shareWithTeamId: previewPortalOpenToShareField ? normalizeText(previewPortalShareWithTeamId) : "",
    };

    setPreviewErr("");
    setPreviewOk("");
    setPreviewPortalClaimingSlotId(slotId);
    try {
      const targets = recurringCandidates.length ? recurringCandidates : [slot];
      let successCount = 0;
      const failures = [];

      for (const candidate of targets) {
        try {
          await apiFetch(`/api/slots/${encodeURIComponent(division)}/${encodeURIComponent(candidate.slotId)}/practice`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
          });
          successCount += 1;
        } catch (e) {
          failures.push({
            slot: candidate,
            message: e?.message || String(e),
          });
        }
      }

      await loadPracticePreviewData(division, teamId);

      if (successCount > 0) {
        const base = successCount === 1
          ? `Override claimed 1 practice slot for ${teamId}.`
          : `Override claimed recurring practice pattern for ${teamId} across ${successCount} weeks.`;
        const sharing = previewPortalOpenToShareField ? " Sharing preference saved." : "";
        const partial = failures.length ? ` ${failures.length} week(s) failed.` : "";
        setPreviewOk(`${base}${sharing}${partial}`.trim());
      } else {
        setPreviewErr(failures[0]?.message || "No matching practice slots could be claimed.");
      }

      if (failures.length) {
        const sample = failures
          .slice(0, 3)
          .map((f) => `${normalizeText(f.slot?.gameDate) || "?"}: ${f.message}`)
          .join(" | ");
        setPreviewErr(`Some weeks were not claimed. ${sample}${failures.length > 3 ? " ..." : ""}`);
      }
    } catch (e) {
      setPreviewErr(e?.message || "Failed to apply practice override.");
    } finally {
      setPreviewPortalClaimingSlotId("");
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
            <div className="subtle">Assign users to leagues and adjust roles from <code>GameSwapMemberships</code>.</div>
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

          {memberErr && <div className="callout callout--error">{memberErr}</div>}
          {memberOk && <div className="callout callout--ok">{memberOk}</div>}

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
          <div className="card__header">
            <div className="h2">Debug: coach practice request preview</div>
            <div className="subtle">
              Preview what a coach sees in onboarding practice requests using <code>/api/practice-requests</code> and open slots from <code>/api/slots</code>.
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
                <div className="layoutStat__label">Open practice slots</div>
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

              <div className="card__header">
                <div className="h2">Open availability slots visible to coaches (top 20)</div>
              </div>
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

              <div className="card__header mt-4">
                <div className="h2">Practice selection portal preview (admin override)</div>
                <div className="subtle">
                  Mirrors the coach practice selection portal layout for this team. Use Select here to apply an admin override for the selected team.
                </div>
              </div>

              <div className="card mt-2">
                <div className="card__header">
                  <div className="h2">Practice selection portal</div>
                  <div className="subtle">
                    Selecting a slot claims the same field/day/time pattern for matching open weeks in the regular-season availability set.
                  </div>
                </div>
                <div className="formGrid">
                  <label>
                    Division
                    <input value={normalizeText(previewDivision)} readOnly />
                  </label>
                  <label>
                    Team
                    <input value={normalizeText(previewTeamId) || "Unassigned"} readOnly />
                  </label>
                </div>
              </div>

              <div className="card">
                <div className="card__header">
                  <div className="h2">Your selected practices (preview)</div>
                </div>
                {previewPortalSelections.length ? (
                  <div className="tableWrap">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Week</th>
                          <th>Date</th>
                          <th>Time</th>
                          <th>Location</th>
                        </tr>
                      </thead>
                      <tbody>
                        {previewPortalSelections.map((slot) => (
                          <tr key={normalizeText(slot?.slotId) || `${slot?.gameDate}-${slot?.startTime}`}>
                            <td>{weekKeyFromDate(normalizeText(slot?.gameDate))}</td>
                            <td>{normalizeText(slot?.gameDate)}</td>
                            <td>{formatSlotTime(slot)}</td>
                            <td>{formatSlotLocation(slot)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  <div className="muted">No confirmed practice selections for this team.</div>
                )}
              </div>

              <div className="card">
                <div className="card__header">
                  <div className="h2">Available practice slots (preview)</div>
                </div>
                <div className="callout mb-3">
                  <div className="row row--wrap gap-3">
                    <label className="inlineCheck inlineCheck--compact">
                      <input
                        type="checkbox"
                        checked={previewPortalOpenToShareField}
                        onChange={(e) => setPreviewPortalOpenToShareField(e.target.checked)}
                      />
                      <span>Open to sharing a field</span>
                    </label>
                    <label className="min-w-[260px]">
                      Propose sharing with team
                      <select
                        value={previewPortalShareWithTeamId}
                        onChange={(e) => setPreviewPortalShareWithTeamId(normalizeText(e.target.value))}
                        disabled={!previewPortalOpenToShareField || previewPortalShareableTeams.length === 0}
                      >
                        <option value="">
                          {!previewPortalOpenToShareField
                            ? "Enable sharing first"
                            : (previewPortalShareableTeams.length ? "Select a team" : "No other teams in division")}
                        </option>
                        {previewPortalShareableTeams.map((team) => (
                          <option key={team.teamId} value={team.teamId}>
                            {team.name ? `${team.name} (${team.teamId})` : team.teamId}
                          </option>
                        ))}
                      </select>
                    </label>
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
                  <div className="muted mt-2">
                    Admin override: filter by weekday, then Select to claim the recurring field/day/time pattern for the selected team.
                  </div>
                </div>

                <div className="mb-3">
                  <div className="font-semibold">Coach onboarding recurring choices (debug preview)</div>
                  <div className="muted mt-1">
                    Mirrors the coach setup recurring choices: day + field + time (the recurring season pattern is reserved together on approval).
                  </div>
                </div>

                {previewPortalRecurringChoices.length === 0 ? (
                  <div className="muted mb-3">
                    {previewPortalDayFilter
                      ? "No recurring practice choices match the selected day."
                      : "No recurring practice choices available for this division."}
                  </div>
                ) : (
                  <div className="tableWrap mb-3">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Field</th>
                          <th>Day</th>
                          <th>Time</th>
                          <th>Season Span</th>
                          <th>Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {previewPortalRecurringChoices.map((choice) => {
                          const slot = choice.representativeSlot;
                          const request = choice.existingRequest;
                          return (
                            <tr key={choice.key}>
                              <td>{formatSlotLocation(slot)}</td>
                              <td>{weekdayLabelFromDate(normalizeText(slot?.gameDate))}</td>
                              <td>{formatSlotTime(slot)}</td>
                              <td>{choice.firstDate && choice.lastDate ? `${choice.firstDate} - ${choice.lastDate}` : (choice.firstDate || "-")}</td>
                              <td>{request ? `Requested (P${request.priority || "?"}, ${normalizeText(request.status) || "Pending"})` : "-"}</td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                )}

                <div className="mb-3">
                  <div className="font-semibold">Google Form option text (copy/paste)</div>
                  <div className="muted mt-1">
                    One line per recurring availability pattern using a single representative week date (all days shown).
                  </div>
                  <textarea
                    readOnly
                    value={previewPortalGoogleFormOptionsText || ""}
                    rows={Math.min(16, Math.max(4, (previewPortalGoogleFormOptionsText || "").split("\n").filter(Boolean).length + 1))}
                    className="textareaMono mt-2"
                  />
                </div>

                <div className="mb-3">
                  <div className="font-semibold">Recurring option summary (Day | Field | Time)</div>
                  <div className="muted mt-1">
                    One line per recurring pattern for quick review{previewPortalDayFilter ? " (filtered by selected day)" : ""}.
                  </div>
                  <textarea
                    readOnly
                    value={previewPortalGoogleFormPatternOptionsText || ""}
                    rows={Math.min(16, Math.max(4, (previewPortalGoogleFormPatternOptionsText || "").split("\n").filter(Boolean).length + 1))}
                    className="textareaMono mt-2"
                  />
                </div>

                {previewPortalAvailableSlotsFiltered.length === 0 ? (
                  <div className="muted">
                    {previewPortalDayFilter
                      ? "No open practice slots match the selected day."
                      : "No open practice slots available for this division."}
                  </div>
                ) : (
                  <div className="tableWrap">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Week</th>
                          <th>Date</th>
                          <th>Time</th>
                          <th>Location</th>
                          <th />
                        </tr>
                      </thead>
                      <tbody>
                        {previewPortalAvailableSlotsFiltered.map((slot) => {
                          const weekKey = weekKeyFromDate(normalizeText(slot?.gameDate));
                          const disabled = !!(weekKey && previewPortalSelectionsByWeek.has(weekKey));
                          return (
                            <tr key={normalizeText(slot?.slotId) || `${slot?.gameDate}-${slot?.startTime}`}>
                              <td>{weekKey}</td>
                              <td>{normalizeText(slot?.gameDate)}</td>
                              <td>{formatSlotTime(slot)}</td>
                              <td>{formatSlotLocation(slot)}</td>
                              <td>
                                <button
                                  className="btn btn--primary"
                                  type="button"
                                  disabled={disabled || !!previewPortalClaimingSlotId || previewLoading}
                                  onClick={() => claimPracticePortalPreviewOverride(slot)}
                                  title={
                                    disabled
                                      ? "No claimable weeks remain in this pattern for the selected team"
                                      : previewPortalClaimingSlotId
                                        ? "Processing admin override..."
                                        : "Admin override: claim this recurring pattern for the selected team"
                                  }
                                >
                                  {previewPortalClaimingSlotId
                                    ? (previewPortalClaimingSlotId === normalizeText(slot?.slotId) ? "Selecting..." : "Working...")
                                    : "Select"}
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
