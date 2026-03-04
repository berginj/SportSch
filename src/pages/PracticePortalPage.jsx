import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";
import { ConfirmDialog } from "../components/Dialogs";
import { useConfirmDialog } from "../lib/useDialogs";

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

function normalizeRole(role) {
  return (role || "").trim();
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

function formatSlotTime(slot) {
  const start = (slot?.startTime || "").trim();
  const end = (slot?.endTime || "").trim();
  if (!start || !end) return "";
  return `${start} - ${end}`;
}

function formatSlotLocation(slot) {
  return slot?.displayName || `${slot?.parkName || ""} ${slot?.fieldName || ""}`.trim() || slot?.fieldKey || "";
}

function practicePatternKey(slot) {
  const weekday = weekdayKeyFromDate(slot?.gameDate);
  const fieldKey = (slot?.fieldKey || "").trim();
  const start = (slot?.startTime || "").trim();
  const end = (slot?.endTime || "").trim();
  if (!weekday || !fieldKey || !start || !end) return "";
  return `${weekday}|${fieldKey}|${start}|${end}`;
}

function weekdayLabelFromDate(isoDate) {
  const key = weekdayKeyFromDate(isoDate);
  return WEEKDAY_FILTER_OPTIONS.find((opt) => opt.key === key)?.label || "";
}

function isPracticeCapableAvailability(slot) {
  if (!slot?.isAvailability) return false;
  if ((slot?.status || "").trim() !== "Open") return false;
  const allocationType = String(slot?.allocationSlotType || slot?.slotType || "").trim().toLowerCase();
  if (!allocationType) return true;
  return allocationType === "practice" || allocationType === "both";
}

export default function PracticePortalPage({ me, leagueId }) {
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const memberships = useMemo(
    () => (Array.isArray(me?.memberships) ? me.memberships : []),
    [me]
  );
  const role = useMemo(() => {
    const inLeague = memberships.filter((m) => (m?.leagueId || "").trim() === (leagueId || "").trim());
    const roles = inLeague.map((m) => normalizeRole(m?.role));
    if (roles.includes("LeagueAdmin")) return "LeagueAdmin";
    if (roles.includes("Coach")) return "Coach";
    return roles.includes("Viewer") ? "Viewer" : "";
  }, [memberships, leagueId]);

  const coachTeam = useMemo(() => {
    const inLeague = memberships.filter((m) => (m?.leagueId || "").trim() === (leagueId || "").trim());
    const coach = inLeague.find((m) => normalizeRole(m?.role) === "Coach");
    const division = (coach?.team?.division || coach?.division || "").trim();
    const teamId = (coach?.team?.teamId || coach?.teamId || "").trim();
    return { division, teamId };
  }, [memberships, leagueId]);

  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");
  const [divisionTeams, setDivisionTeams] = useState([]);
  const [slots, setSlots] = useState([]);
  const [practiceRequests, setPracticeRequests] = useState([]);
  const [portalSettings, setPortalSettings] = useState(null);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [notice, setNotice] = useState("");
  const [toast, setToast] = useState(null);
  const [openToShareField, setOpenToShareField] = useState(false);
  const [shareWithTeamId, setShareWithTeamId] = useState("");
  const [availableDayFilter, setAvailableDayFilter] = useState("0");
  const [requestingSlot, setRequestingSlot] = useState("");
  const [portalMode, setPortalMode] = useState("recurring");
  const [oneOffDayFilter, setOneOffDayFilter] = useState("");
  const [oneOffFieldSearch, setOneOffFieldSearch] = useState("");
  const [oneOffDateFrom, setOneOffDateFrom] = useState("");
  const [oneOffDateTo, setOneOffDateTo] = useState("");
  const initializedRef = useRef(false);
  const loadedDivisionRef = useRef("");
  const { confirmState, requestConfirm, handleConfirm, handleCancel } = useConfirmDialog();

  const canSelectPractice = role === "Coach" || role === "LeagueAdmin" || isGlobalAdmin;
  const canPickDivision = isGlobalAdmin || role === "LeagueAdmin" || !coachTeam.division;

  const applyFiltersFromUrl = useCallback(() => {
    if (typeof window === "undefined") return { division: "" };
    const params = new URLSearchParams(window.location.search);
    return { division: (params.get("division") || "").trim() };
  }, []);

  async function loadAll(selectedDivision) {
    setErr("");
    setNotice("");
    setLoading(true);
    try {
      const [divs] = await Promise.all([apiFetch("/api/divisions")]);
      const divList = Array.isArray(divs) ? divs : [];
      setDivisions(divList);

      if (coachTeam.division && selectedDivision && selectedDivision !== coachTeam.division) {
        setNotice(`Your account is assigned to ${coachTeam.division}. Showing that division.`);
      }

      const preferred = coachTeam.division || selectedDivision || divList?.[0]?.code || "";
      setDivision(preferred);

      if (preferred) {
        const params = new URLSearchParams({ division: preferred, status: "Open,Confirmed" });
        const requestParams = new URLSearchParams();
        const portalParams = new URLSearchParams({ division: preferred });
        if (coachTeam.teamId) requestParams.set("teamId", coachTeam.teamId);
        const [s, teams, requests, settings] = await Promise.all([
          apiFetch(`/api/slots?${params.toString()}`),
          apiFetch(`/api/teams?division=${encodeURIComponent(preferred)}`).catch(() => []),
          coachTeam.teamId
            ? apiFetch(`/api/practice-requests?${requestParams.toString()}`).catch(() => [])
            : Promise.resolve([]),
          apiFetch(`/api/practice-portal/settings?${portalParams.toString()}`).catch(() => null),
        ]);
        setSlots(Array.isArray(s) ? s : []);
        setDivisionTeams(Array.isArray(teams) ? teams : []);
        setPracticeRequests(Array.isArray(requests) ? requests : []);
        setPortalSettings(settings && typeof settings === "object" ? settings : null);
        loadedDivisionRef.current = preferred;
      } else {
        setSlots([]);
        setDivisionTeams([]);
        setPracticeRequests([]);
        setPortalSettings(null);
        loadedDivisionRef.current = "";
      }
    } catch (e) {
      setErr(e?.message || String(e));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    const preferred = applyFiltersFromUrl();
    loadAll(preferred.division).finally(() => {
      initializedRef.current = true;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId, coachTeam.division, coachTeam.teamId]);

  useEffect(() => {
    if (!initializedRef.current) return;
    if (!division || division === loadedDivisionRef.current) return;
    const reload = async () => {
      setErr("");
      setLoading(true);
      try {
        const params = new URLSearchParams({ division, status: "Open,Confirmed" });
        const requestParams = new URLSearchParams();
        const portalParams = new URLSearchParams({ division });
        if (coachTeam.teamId) requestParams.set("teamId", coachTeam.teamId);
        const [s, teams, requests, settings] = await Promise.all([
          apiFetch(`/api/slots?${params.toString()}`),
          apiFetch(`/api/teams?division=${encodeURIComponent(division)}`).catch(() => []),
          coachTeam.teamId
            ? apiFetch(`/api/practice-requests?${requestParams.toString()}`).catch(() => [])
            : Promise.resolve([]),
          apiFetch(`/api/practice-portal/settings?${portalParams.toString()}`).catch(() => null),
        ]);
        setSlots(Array.isArray(s) ? s : []);
        setDivisionTeams(Array.isArray(teams) ? teams : []);
        setPracticeRequests(Array.isArray(requests) ? requests : []);
        setPortalSettings(settings && typeof settings === "object" ? settings : null);
        loadedDivisionRef.current = division;
      } catch (e) {
        setErr(e?.message || String(e));
      } finally {
        setLoading(false);
      }
    };
    reload();
  }, [division, coachTeam.teamId]);

  useEffect(() => {
    if (!initializedRef.current || typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (division) params.set("division", division);
    else params.delete("division");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [division]);

  const practiceSelections = useMemo(() => {
    if (!coachTeam.teamId) return [];
    return (slots || [])
      .filter((s) => (s?.gameType || "").trim().toLowerCase() === "practice")
      .filter((s) => (s?.status || "") === "Confirmed")
      .filter((s) => {
        const confirmed = (s?.confirmedTeamId || "").trim();
        const offering = (s?.offeringTeamId || "").trim();
        return confirmed === coachTeam.teamId || offering === coachTeam.teamId;
      });
  }, [slots, coachTeam.teamId]);

  const practiceByWeek = useMemo(() => {
    const map = new Map();
    for (const s of practiceSelections) {
      const key = weekKeyFromDate(s.gameDate);
      if (!key) continue;
      if (!map.has(key)) map.set(key, s);
    }
    return map;
  }, [practiceSelections]);

  const activePracticeRequests = useMemo(() => {
    const teamId = (coachTeam.teamId || "").trim();
    return (Array.isArray(practiceRequests) ? practiceRequests : [])
      .filter((r) => ["Pending", "Approved"].includes((r?.status || "").trim()))
      .filter((r) => !teamId || (r?.teamId || "").trim() === teamId)
      .filter((r) => !division || (r?.division || "").trim() === division)
      .sort((a, b) => {
        const pa = Number.isFinite(Number(a?.priority)) ? Number(a.priority) : 99;
        const pb = Number.isFinite(Number(b?.priority)) ? Number(b.priority) : 99;
        if (pa !== pb) return pa - pb;
        const ad = `${a?.slot?.gameDate || ""} ${a?.slot?.startTime || ""}`.trim();
        const bd = `${b?.slot?.gameDate || ""} ${b?.slot?.startTime || ""}`.trim();
        return ad.localeCompare(bd);
      });
  }, [practiceRequests, coachTeam.teamId, division]);

  const activeRequestPatternByKey = useMemo(() => {
    const map = new Map();
    for (const req of activePracticeRequests) {
      const slot = req?.slot || null;
      const key = practicePatternKey(slot);
      if (key && !map.has(key)) map.set(key, req);
    }
    return map;
  }, [activePracticeRequests]);

  const nextPracticeRequestPriority = useMemo(() => {
    const used = new Set(
      activePracticeRequests
        .map((r) => Number(r?.priority))
        .filter((p) => Number.isFinite(p) && p >= 1 && p <= 3)
    );
    for (const priority of [1, 2, 3]) {
      if (!used.has(priority)) return priority;
    }
    return 0;
  }, [activePracticeRequests]);

  const shareableTeams = useMemo(() => {
    return (Array.isArray(divisionTeams) ? divisionTeams : [])
      .filter((t) => (t?.teamId || "").trim())
      .filter((t) => (t?.teamId || "").trim() !== (coachTeam.teamId || "").trim())
      .sort((a, b) => {
        const aLabel = (a?.name || a?.teamId || "").trim();
        const bLabel = (b?.name || b?.teamId || "").trim();
        return aLabel.localeCompare(bLabel);
      });
  }, [divisionTeams, coachTeam.teamId]);

  useEffect(() => {
    if (!openToShareField && shareWithTeamId) {
      setShareWithTeamId("");
      return;
    }
    if (!openToShareField || !shareWithTeamId) return;
    if (!shareableTeams.some((t) => (t?.teamId || "").trim() === shareWithTeamId)) {
      setShareWithTeamId("");
    }
  }, [openToShareField, shareWithTeamId, shareableTeams]);

  const availableSlots = useMemo(() => {
    return (slots || [])
      .filter((s) => s?.isAvailability)
      .filter((s) => (s?.status || "") === "Open")
      .sort((a, b) => {
        const ad = `${a.gameDate || ""} ${a.startTime || ""}`.trim();
        const bd = `${b.gameDate || ""} ${b.startTime || ""}`.trim();
        return ad.localeCompare(bd);
      });
  }, [slots]);

  const practiceCapableOpenSlots = useMemo(() => {
    return availableSlots.filter((s) => isPracticeCapableAvailability(s));
  }, [availableSlots]);

  const filteredAvailableSlots = useMemo(() => {
    const dayKey = String(availableDayFilter || "").trim();
    if (!dayKey) return practiceCapableOpenSlots;
    return practiceCapableOpenSlots.filter((slot) => weekdayKeyFromDate(slot?.gameDate) === dayKey);
  }, [practiceCapableOpenSlots, availableDayFilter]);

  const oneOffAvailabilityStatus = useMemo(() => {
    const oneOffEnabled = !!portalSettings?.oneOffRequestsEnabled;
    const divisionStatus = portalSettings?.divisionStatus || null;
    const coverageReady = !!divisionStatus?.allTeamsHaveRecurringPractice;
    return {
      oneOffEnabled,
      divisionStatus,
      canBook: oneOffEnabled && coverageReady,
    };
  }, [portalSettings]);

  const oneOffSearchResults = useMemo(() => {
    const fieldSearch = String(oneOffFieldSearch || "").trim().toLowerCase();
    const fromDate = String(oneOffDateFrom || "").trim();
    const toDate = String(oneOffDateTo || "").trim();
    const dayKey = String(oneOffDayFilter || "").trim();
    return practiceCapableOpenSlots
      .filter((slot) => {
        const gameDate = String(slot?.gameDate || "").trim();
        if (!gameDate) return false;
        if (fromDate && gameDate < fromDate) return false;
        if (toDate && gameDate > toDate) return false;
        if (dayKey && weekdayKeyFromDate(gameDate) !== dayKey) return false;
        if (!fieldSearch) return true;
        const haystack = [
          slot?.displayName,
          slot?.fieldKey,
          slot?.parkName,
          slot?.fieldName,
        ]
          .map((v) => String(v || "").toLowerCase())
          .join(" ");
        return haystack.includes(fieldSearch);
      })
      .sort((a, b) => {
        const ad = `${a?.gameDate || ""} ${a?.startTime || ""}`.trim();
        const bd = `${b?.gameDate || ""} ${b?.startTime || ""}`.trim();
        const cmp = ad.localeCompare(bd);
        if (cmp !== 0) return cmp;
        return formatSlotLocation(a).localeCompare(formatSlotLocation(b));
      });
  }, [practiceCapableOpenSlots, oneOffFieldSearch, oneOffDateFrom, oneOffDateTo, oneOffDayFilter]);

  const selectedPracticePatternKeys = useMemo(() => {
    const keys = new Set();
    for (const slot of practiceSelections) {
      const key = practicePatternKey(slot);
      if (key) keys.add(key);
    }
    return keys;
  }, [practiceSelections]);

  const availablePatternChoices = useMemo(() => {
    const byPattern = new Map();
    const selectedWeeks = new Set(Array.from(practiceByWeek.keys()));

    for (const slot of filteredAvailableSlots) {
      const key = practicePatternKey(slot);
      if (!key) continue;
      if (!byPattern.has(key)) {
        byPattern.set(key, {
          key,
          slots: [],
          claimableSlots: [],
          blockedSlots: [],
        });
      }
      const group = byPattern.get(key);
      group.slots.push(slot);
      const weekKey = weekKeyFromDate(slot?.gameDate);
      if (weekKey && selectedWeeks.has(weekKey)) group.blockedSlots.push(slot);
      else group.claimableSlots.push(slot);
    }

    const sortByDateTime = (a, b) => {
      const aKey = `${a?.gameDate || ""} ${a?.startTime || ""}`.trim();
      const bKey = `${b?.gameDate || ""} ${b?.startTime || ""}`.trim();
      return aKey.localeCompare(bKey);
    };

    return Array.from(byPattern.values())
      .map((group) => {
        const slotsSorted = [...group.slots].sort(sortByDateTime);
        const claimableSorted = [...group.claimableSlots].sort(sortByDateTime);
        const representativeSlot = claimableSorted[0] || slotsSorted[0] || null;
        const firstDate = slotsSorted[0]?.gameDate || "";
        const lastDate = slotsSorted[slotsSorted.length - 1]?.gameDate || "";
        return {
          key: group.key,
          representativeSlot,
          slots: slotsSorted,
          claimableSlots: claimableSorted,
          openWeeks: slotsSorted.length,
          claimableWeeks: claimableSorted.length,
          blockedWeeks: group.blockedSlots.length,
          firstDate,
          lastDate,
          weekdayLabel: representativeSlot ? weekdayLabelFromDate(representativeSlot.gameDate) : "",
          existingRequest: activeRequestPatternByKey.get(group.key) || null,
          matchesRequestedPattern: activeRequestPatternByKey.has(group.key),
          matchesSelectedPattern: selectedPracticePatternKeys.has(group.key),
        };
      })
      .filter((choice) => choice.representativeSlot)
      .sort((a, b) => {
        if (a.matchesRequestedPattern !== b.matchesRequestedPattern) {
          return a.matchesRequestedPattern ? -1 : 1;
        }
        if (a.matchesSelectedPattern !== b.matchesSelectedPattern) {
          return a.matchesSelectedPattern ? -1 : 1;
        }
        if (a.claimableWeeks !== b.claimableWeeks) return b.claimableWeeks - a.claimableWeeks;
        const aDate = `${a.representativeSlot?.gameDate || ""} ${a.representativeSlot?.startTime || ""}`.trim();
        const bDate = `${b.representativeSlot?.gameDate || ""} ${b.representativeSlot?.startTime || ""}`.trim();
        const dateCmp = aDate.localeCompare(bDate);
        if (dateCmp !== 0) return dateCmp;
        return formatSlotLocation(a.representativeSlot).localeCompare(formatSlotLocation(b.representativeSlot));
      });
  }, [filteredAvailableSlots, practiceByWeek, selectedPracticePatternKeys, activeRequestPatternByKey]);

  const portalSummary = useMemo(() => ({
    selectedPractices: practiceSelections.length,
    activeRequests: activePracticeRequests.length,
    recurringChoices: availablePatternChoices.length,
    oneOffChoices: oneOffSearchResults.length,
  }), [practiceSelections.length, activePracticeRequests.length, availablePatternChoices.length, oneOffSearchResults.length]);

  async function requestPracticePattern(choice) {
    const slot = choice?.representativeSlot;
    if (!slot?.slotId || !division) return;
    if (!coachTeam.teamId) {
      setErr("Your coach profile needs a team assignment before requesting a practice slot.");
      return;
    }
    if (choice?.existingRequest) {
      setErr("You already requested this recurring field/day/time pattern.");
      return;
    }
    if (!nextPracticeRequestPriority) {
      setErr("You already have 3 active recurring practice requests. Wait for commissioner review before adding another.");
      return;
    }
    if (openToShareField && !shareWithTeamId) {
      setErr('Select a team to propose sharing with, or uncheck "Open to sharing a field".');
      return;
    }

    const proposedShareTeam = shareableTeams.find((t) => (t?.teamId || "").trim() === shareWithTeamId);
    const shareMsg = openToShareField
      ? ` Open to share with: ${proposedShareTeam?.name || shareWithTeamId}.`
      : "";
    const seriesCount = Number(choice?.claimableWeeks || 0);
    const recurringMsg =
      seriesCount > 1
        ? ` This requests the recurring weekly slot pattern for about ${seriesCount} available weeks (same field and time), starting ${slot.gameDate}.`
        : " This requests the recurring field/day/time pattern represented by this slot.";

    const ok = await requestConfirm({
      title: "Request recurring practice pattern",
      message: `Request priority #${nextPracticeRequestPriority} for ${weekdayLabelFromDate(slot.gameDate)} ${formatSlotTime(slot)} at ${formatSlotLocation(slot)}?${recurringMsg} Commissioner approval locks the recurring pattern.${shareMsg}`,
      confirmLabel: "Request",
    });
    if (!ok) return;

    setErr("");
    setRequestingSlot(String(slot.slotId || ""));
    try {
      const payload = {
        division,
        teamId: coachTeam.teamId,
        slotId: slot.slotId,
        priority: nextPracticeRequestPriority,
        reason: "Recurring practice preference from practice portal",
        openToShareField,
        shareWithTeamId: openToShareField ? shareWithTeamId : "",
      };
      await apiFetch("/api/practice-requests", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      await loadAll(division);
      setToast({
        tone: "success",
        message: openToShareField
          ? `Priority #${nextPracticeRequestPriority} requested. Sharing preference sent to commissioner.`
          : `Priority #${nextPracticeRequestPriority} requested. Awaiting commissioner approval.`,
        duration: 3200,
      });
    } catch (e) {
      setErr(e?.message || String(e));
    } finally {
      setRequestingSlot("");
    }
  }

  async function bookOneOffPractice(slot) {
    if (!slot?.slotId || !division) return;
    if (!coachTeam.teamId) {
      setErr("Your coach profile needs a team assignment before booking a one-off practice.");
      return;
    }
    if (openToShareField && !shareWithTeamId) {
      setErr('Select a team to propose sharing with, or uncheck "Open to sharing a field".');
      return;
    }

    if (!oneOffAvailabilityStatus.canBook) {
      setErr("One-off practice booking is not currently available.");
      return;
    }

    const proposedShareTeam = shareableTeams.find((t) => (t?.teamId || "").trim() === shareWithTeamId);
    const shareMsg = openToShareField
      ? ` Open to share with: ${proposedShareTeam?.name || shareWithTeamId}.`
      : "";

    const ok = await requestConfirm({
      title: "Book one-off practice",
      message: `Book one-off practice on ${slot.gameDate} ${formatSlotTime(slot)} at ${formatSlotLocation(slot)}?${shareMsg}`,
      confirmLabel: "Book",
    });
    if (!ok) return;

    setErr("");
    setRequestingSlot(String(slot.slotId || ""));
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(division)}/${encodeURIComponent(slot.slotId)}/practice`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          openToShareField,
          shareWithTeamId: openToShareField ? shareWithTeamId : "",
          oneOffBooking: true,
        }),
      });

      await loadAll(division);
      setToast({
        tone: "success",
        message: openToShareField
          ? "One-off practice booked. Sharing preference saved."
          : "One-off practice booked.",
        duration: 3000,
      });
    } catch (e) {
      setErr(e?.message || String(e));
    } finally {
      setRequestingSlot("");
    }
  }

  if (loading) {
    return (
      <div className="page">
        <StatusCard title="Loading" message="Loading practice slots..." />
      </div>
    );
  }

  if (!canSelectPractice) {
    return (
      <div className="page">
        <div className="card">
          <h2>Practice selection</h2>
          <p className="muted">You do not have access to the practice selection portal.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="card">
        <div className="card__header">
          <h2>Practice selection portal</h2>
          <div className="subtle">Recurring requests first, one-off bookings second.</div>
        </div>
        <p className="muted">
          Coaches should submit up to 3 prioritized recurring practice requests. Commissioners approve one to lock the same field/day/time pattern across matching regular-season weeks.
        </p>
        <div className="formGrid">
          <label>
            Division
            <select value={division} onChange={(e) => setDivision(e.target.value)} disabled={!canPickDivision}>
              <option value="">Select a division</option>
              {divisions.map((d) => (
                <option key={d.code} value={d.code}>
                  {d.name} ({d.code})
                </option>
              ))}
            </select>
          </label>
          <label>
            Team
            <input value={coachTeam.teamId || "Unassigned"} readOnly />
          </label>
        </div>
        {err ? <div className="callout callout--error">{err}</div> : null}
        {notice ? <div className="callout callout--ok">{notice}</div> : null}
        <div className="layoutStatRow">
          <div className="layoutStat">
            <div className="layoutStat__value">{portalSummary.selectedPractices}</div>
            <div className="layoutStat__label">Selected practices</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{portalSummary.activeRequests}</div>
            <div className="layoutStat__label">Active requests</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{portalSummary.recurringChoices}</div>
            <div className="layoutStat__label">Recurring choices</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{portalSummary.oneOffChoices}</div>
            <div className="layoutStat__label">One-off openings</div>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="row row--wrap gap-2 items-center">
          <button
            type="button"
            className={`btn btn--sm ${portalMode === "recurring" ? "btn--primary" : ""}`}
            onClick={() => setPortalMode("recurring")}
          >
            Recurring Selection
          </button>
          <button
            type="button"
            className={`btn btn--sm ${portalMode === "oneoff" ? "btn--primary" : ""}`}
            onClick={() => setPortalMode("oneoff")}
          >
            One-off Practice Search
          </button>
        </div>
        <div className="muted mt-2">
          Recurring selection is the current setup workflow. One-off practice search will be enabled by the commissioner after recurring slots are finalized for teams.
        </div>
      </div>

      {portalMode === "oneoff" ? (
        <div className="card">
          <div className="card__header">
            <h3>One-off practice search</h3>
            <div className="subtle">Self-booked single-date practices after recurring coverage is complete.</div>
          </div>
          <div className={`callout mb-3 ${oneOffAvailabilityStatus.canBook ? "callout--ok" : ""}`}>
            <div>
              Commissioner one-off booking toggle: <b>{oneOffAvailabilityStatus.oneOffEnabled ? "Enabled" : "Disabled"}</b>
            </div>
            <div className="mt-1">
              Division recurring coverage:{" "}
              <b>
                {oneOffAvailabilityStatus.divisionStatus
                  ? `${oneOffAvailabilityStatus.divisionStatus.teamsWithApprovedRecurringPractice}/${oneOffAvailabilityStatus.divisionStatus.teamCount}`
                  : "-/-"}
              </b>
              {oneOffAvailabilityStatus.divisionStatus?.allTeamsHaveRecurringPractice
                ? " (all teams covered)"
                : " (waiting on recurring approvals)"}
            </div>
            {!oneOffAvailabilityStatus.oneOffEnabled ? (
              <div className="muted mt-2">
                Commissioner must enable one-off practice self-booking before coaches can use this tab.
              </div>
            ) : null}
            {oneOffAvailabilityStatus.oneOffEnabled && !oneOffAvailabilityStatus.divisionStatus?.allTeamsHaveRecurringPractice ? (
              <div className="muted mt-2">
                One-off bookings unlock after all teams in this division have an approved recurring practice request.
              </div>
            ) : null}
            {Array.isArray(oneOffAvailabilityStatus.divisionStatus?.missingTeams) &&
            oneOffAvailabilityStatus.divisionStatus.missingTeams.length > 0 ? (
              <div className="muted mt-2">
                Missing recurring approval for:{" "}
                {oneOffAvailabilityStatus.divisionStatus.missingTeams
                  .slice(0, 8)
                  .map((t) => t?.name || t?.teamId)
                  .filter(Boolean)
                  .join(", ")}
                {oneOffAvailabilityStatus.divisionStatus.missingTeams.length > 8 ? " ..." : ""}
              </div>
            ) : null}
          </div>

          <div className="callout mb-3 controlBand">
            <div className="row row--wrap gap-3">
              <label className="row row--wrap gap-2 items-center">
                <input
                  type="checkbox"
                  checked={openToShareField}
                  onChange={(e) => setOpenToShareField(e.target.checked)}
                  disabled={!coachTeam.teamId}
                />
                <span>Open to sharing a field</span>
              </label>
              <label className="min-w-[260px]">
                Propose sharing with team
                <select
                  value={shareWithTeamId}
                  onChange={(e) => setShareWithTeamId(e.target.value)}
                  disabled={!openToShareField || !coachTeam.teamId || shareableTeams.length === 0}
                >
                  <option value="">
                    {!coachTeam.teamId
                      ? "Coach team assignment required"
                      : !openToShareField
                        ? "Enable sharing first"
                        : shareableTeams.length
                          ? "Select a team"
                          : "No other teams in division"}
                  </option>
                  {shareableTeams.map((t) => (
                    <option key={t.teamId} value={t.teamId}>
                      {t.name ? `${t.name} (${t.teamId})` : t.teamId}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            <div className="row row--wrap gap-3 mt-2">
              <label className="min-w-[220px]">
                Filter by day
                <select value={oneOffDayFilter} onChange={(e) => setOneOffDayFilter(e.target.value)}>
                  {WEEKDAY_FILTER_OPTIONS.map((opt) => (
                    <option key={`oneoff-${opt.key || "all"}`} value={opt.key}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </label>
              <label className="min-w-[180px]">
                Date from
                <input type="date" value={oneOffDateFrom} onChange={(e) => setOneOffDateFrom(e.target.value)} />
              </label>
              <label className="min-w-[180px]">
                Date to
                <input type="date" value={oneOffDateTo} onChange={(e) => setOneOffDateTo(e.target.value)} />
              </label>
              <label className="flex-1 min-w-[220px]">
                Search fields
                <input
                  value={oneOffFieldSearch}
                  onChange={(e) => setOneOffFieldSearch(e.target.value)}
                  placeholder="Search by field / park"
                />
              </label>
            </div>
            <div className="muted mt-2">
              Search open practice-capable availability and book a single date (one-off) without changing your recurring practice selection.
            </div>
          </div>

          {!oneOffAvailabilityStatus.canBook ? (
            <div className="muted">One-off booking is currently locked.</div>
          ) : oneOffSearchResults.length ? (
            <div className="tableWrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Day</th>
                    <th>Date</th>
                    <th>Time</th>
                    <th>Location</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {oneOffSearchResults.map((slot) => (
                    <tr key={slot.slotId}>
                      <td>{weekdayLabelFromDate(slot.gameDate)}</td>
                      <td>{slot.gameDate}</td>
                      <td>{formatSlotTime(slot)}</td>
                      <td>{formatSlotLocation(slot)}</td>
                      <td className="tableActions">
                        <button
                          type="button"
                          className="btn btn--primary"
                          disabled={!!requestingSlot}
                          onClick={() => bookOneOffPractice(slot)}
                          title={requestingSlot ? "Processing one-off booking..." : "Book this one-off practice"}
                        >
                          {requestingSlot
                            ? (requestingSlot === slot.slotId ? "Booking..." : "Working...")
                            : "Book one-off"}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="muted">
              No open practice-capable slots match your search filters.
            </div>
          )}
        </div>
      ) : (
        <>
      <div className="card">
        <div className="card__header">
          <h3>Your selected practices</h3>
          <div className="subtle">Confirmed recurring practices already assigned to your team.</div>
        </div>
        {practiceSelections.length ? (
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
                {practiceSelections.map((s) => (
                  <tr key={s.slotId}>
                    <td>{weekKeyFromDate(s.gameDate)}</td>
                    <td>{s.gameDate}</td>
                    <td>{formatSlotTime(s)}</td>
                    <td>{formatSlotLocation(s)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="muted">No practice slots selected yet.</div>
        )}
      </div>

      <div className="card">
        <div className="card__header">
          <h3>Your recurring requests (priority 1-3)</h3>
          <div className="subtle">Pending and approved requests currently active for this team.</div>
        </div>
        {activePracticeRequests.length ? (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Priority</th>
                  <th>Status</th>
                  <th>Day</th>
                  <th>Time</th>
                  <th>Location</th>
                  <th>Requested</th>
                  <th>Sharing</th>
                </tr>
              </thead>
              <tbody>
                {activePracticeRequests.map((request) => (
                  <tr key={request.requestId}>
                    <td>{request.priority || "-"}</td>
                    <td><span className={`statusBadge ${String(request.status || "").trim() === "Approved" ? "status-confirmed" : "status-open"}`}>{request.status}</span></td>
                    <td>{weekdayLabelFromDate(request?.slot?.gameDate) || "-"}</td>
                    <td>{request?.slot ? formatSlotTime(request.slot) : "-"}</td>
                    <td>{request?.slot ? formatSlotLocation(request.slot) : (request.slot?.fieldKey || "-")}</td>
                    <td>{request?.slot?.gameDate || "-"}</td>
                    <td>
                      {request.openToShareField
                        ? `Open to share${request.shareWithTeamId ? ` (${request.shareWithTeamId})` : ""}`
                        : "-"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="muted">
            No recurring requests yet. Choose a day (defaults to Sunday) and request up to 3 prioritized options.
          </div>
        )}
      </div>

      <div className="card">
        <div className="card__header">
          <h3>Available practice choices</h3>
          <div className="subtle">Recurring field-day-time patterns sorted by how usable they are.</div>
        </div>
        <div className="callout mb-3 controlBand">
          <div className="row row--wrap gap-3">
            <label className="row row--wrap gap-2 items-center">
              <input
                type="checkbox"
                checked={openToShareField}
                onChange={(e) => setOpenToShareField(e.target.checked)}
                disabled={!coachTeam.teamId}
              />
              <span>Open to sharing a field</span>
            </label>
            <label className="min-w-[260px]">
              Propose sharing with team
              <select
                value={shareWithTeamId}
                onChange={(e) => setShareWithTeamId(e.target.value)}
                disabled={!openToShareField || !coachTeam.teamId || shareableTeams.length === 0}
              >
                <option value="">
                  {!coachTeam.teamId
                    ? "Coach team assignment required"
                    : !openToShareField
                      ? "Enable sharing first"
                      : shareableTeams.length
                        ? "Select a team"
                        : "No other teams in division"}
                </option>
                {shareableTeams.map((t) => (
                  <option key={t.teamId} value={t.teamId}>
                    {t.name ? `${t.name} (${t.teamId})` : t.teamId}
                  </option>
                ))}
              </select>
            </label>
            <label className="min-w-[220px]">
              Filter by day
              <select
                value={availableDayFilter}
                onChange={(e) => setAvailableDayFilter(e.target.value)}
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
            This preference is attached to your recurring practice request(s) and can help commissioners coordinate shared fields.
          </div>
          <div className="muted">
            Filter to a day (defaults to Sunday) and request one recurring field/time pattern. Commissioner approval will lock matching regular-season weeks.
          </div>
          <div className="muted">
            Choices matching your requested or approved patterns appear first.
          </div>
        </div>
        {availablePatternChoices.length ? (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Day</th>
                  <th>Time</th>
                  <th>Location</th>
                  <th>Open Weeks</th>
                  <th>Claimable</th>
                  <th>Season Span</th>
                  <th>Status</th>
                  <th>First Claim</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {availablePatternChoices.map((choice) => {
                  const s = choice.representativeSlot;
                  const hasExistingRequest = !!choice.existingRequest;
                  const disabled = choice.claimableWeeks <= 0 || hasExistingRequest || !nextPracticeRequestPriority;
                  const statusLabel = hasExistingRequest
                    ? `Requested (P${choice.existingRequest?.priority || "?"}, ${choice.existingRequest?.status || "Pending"})`
                    : choice.matchesSelectedPattern
                    ? "Matches your current pattern"
                    : (choice.blockedWeeks > 0 ? `${choice.blockedWeeks} week(s) blocked by existing selections` : "");
                  return (
                    <tr key={choice.key}>
                      <td>{choice.weekdayLabel || weekdayLabelFromDate(s?.gameDate)}</td>
                      <td>{formatSlotTime(s)}</td>
                      <td>{formatSlotLocation(s)}</td>
                      <td>{choice.openWeeks}</td>
                      <td>{choice.claimableWeeks}</td>
                      <td>{choice.firstDate && choice.lastDate ? `${choice.firstDate} - ${choice.lastDate}` : (choice.firstDate || "")}</td>
                      <td>{statusLabel ? <span className="softballBadge softballBadge--neutral">{statusLabel}</span> : "-"}</td>
                      <td>{s?.gameDate || ""}</td>
                      <td className="tableActions">
                        <button
                          className="btn btn--primary"
                          type="button"
                          disabled={disabled || !!requestingSlot}
                          onClick={() => requestPracticePattern(choice)}
                          title={
                            hasExistingRequest
                              ? "You already requested this recurring pattern"
                              : !nextPracticeRequestPriority
                                ? "You already have 3 active recurring requests"
                                : disabled
                              ? "No claimable weeks remain in this pattern"
                              : requestingSlot
                                ? "Submitting recurring request..."
                                : `Request this pattern as priority #${nextPracticeRequestPriority}`
                          }
                        >
                          {requestingSlot
                            ? (requestingSlot === s?.slotId ? "Requesting..." : "Working...")
                            : hasExistingRequest
                              ? "Requested"
                              : (nextPracticeRequestPriority ? `Request P${nextPracticeRequestPriority}` : "Max 3")}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="muted">
            {availableDayFilter
              ? "No recurring practice choices match the selected day."
              : "No open practice choices available for this division."}
          </div>
        )}
      </div>
        </>
      )}

      {toast ? <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} /> : null}
      <ConfirmDialog state={confirmState} onConfirm={handleConfirm} onCancel={handleCancel} />
    </div>
  );
}
