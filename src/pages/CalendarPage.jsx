import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import { readContinuationToken, readPagedItems } from "../lib/pagedResults";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";
import { SLOT_STATUS } from "../lib/constants";
import LeaguePicker from "../components/LeaguePicker";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";
import CalendarView from "../components/CalendarView";
import { ConfirmDialog, PromptDialog } from "../components/Dialogs";
import { useConfirmDialog, usePromptDialog } from "../lib/useDialogs";
import { trackEvent } from "../lib/telemetry";

function createSlotStatusFilter({ open = true, confirmed = true, cancelled = false } = {}) {
  return {
    [SLOT_STATUS.OPEN]: open,
    [SLOT_STATUS.CONFIRMED]: confirmed,
    [SLOT_STATUS.CANCELLED]: cancelled,
  };
}

const CALENDAR_QUERY_FILTER_KEYS = [
  "division",
  "dateFrom",
  "dateTo",
  "showSlots",
  "showEvents",
  "status",
  "slotType",
  "teamId",
];

function normalizeRole(role) {
  return (role || "").trim();
}

function parseBoolParam(params, key, fallback) {
  const raw = params.get(key);
  if (raw == null) return fallback;
  return raw === "1" || raw.toLowerCase() === "true";
}

function parseStatusFilter(params) {
  const raw = (params.get("status") || "").trim();
  if (!raw) {
    return createSlotStatusFilter();
  }
  const set = new Set(raw.split(",").map((s) => s.trim()).filter(Boolean));
  return createSlotStatusFilter({
    open: set.has(SLOT_STATUS.OPEN),
    confirmed: set.has(SLOT_STATUS.CONFIRMED),
    cancelled: set.has(SLOT_STATUS.CANCELLED),
  });
}

function normalizeSlotTypeFilter(raw) {
  const v = (raw || "").trim().toLowerCase();
  if (v === "offer" || v === "request") return v;
  return "all";
}

function matchesSlotType(gameType, filter) {
  const normalized = (gameType || "").trim().toLowerCase();
  if (!filter || filter === "all") return true;
  if (filter === "request") return normalized === "request";
  return normalized !== "request";
}

function isPracticeSlot(slot) {
  return (slot?.gameType || "").trim().toLowerCase() === "practice";
}

function isPlaceholderTeamId(value) {
  const normalized = (value || "").trim().toUpperCase();
  return !normalized || normalized === "AVAILABLE" || normalized === "OPEN" || normalized === "TBD";
}

function isUnscheduledOpenCapacity(slot) {
  if (!slot) return false;
  if ((slot.status || "").trim() !== SLOT_STATUS.OPEN) return false;
  if (slot.isAvailability) return true;
  if (slot.isExternalOffer) return false;

  const away = (slot.awayTeamId || "").trim();
  if (away) return false;

  const homeOrOffering = (slot.homeTeamId || slot.offeringTeamId || "").trim();
  return isPlaceholderTeamId(homeOrOffering);
}

function canAcceptSlot(slot) {
  if (!slot || (slot.status || "") !== "Open") return false;
  if (slot.isAvailability) return false;
  if ((slot.awayTeamId || "").trim() && !slot.isExternalOffer) return false;
  return true;
}

function parseMinutes(hhmm) {
  const parts = String(hhmm || "").split(":");
  if (parts.length !== 2) return null;
  const h = Number(parts[0]);
  const m = Number(parts[1]);
  if (!Number.isFinite(h) || !Number.isFinite(m)) return null;
  if (h < 0 || h > 23 || m < 0 || m > 59) return null;
  return h * 60 + m;
}

function slotMatchupLabel(slot) {
  if (isPracticeSlot(slot)) {
    const team = (slot?.confirmedTeamId || slot?.offeringTeamId || "").trim();
    return team ? `Practice: ${team}` : "Practice";
  }
  const home = (slot?.homeTeamId || slot?.offeringTeamId || "").trim();
  const away = (slot?.awayTeamId || "").trim();
  if (away) return `${home} vs ${away}`;
  if (home && slot?.isExternalOffer) return `${home} vs TBD (external)`;
  if (home) return `${home} vs TBD`;
  return "";
}

function buildServerFilters({ division, dateFrom, dateTo, showSlots, showEvents, slotStatusFilter }) {
  return {
    division: (division || "").trim(),
    dateFrom: (dateFrom || "").trim(),
    dateTo: (dateTo || "").trim(),
    showSlots: !!showSlots,
    showEvents: !!showEvents,
    slotStatusFilter: {
      [SLOT_STATUS.OPEN]: !!slotStatusFilter?.[SLOT_STATUS.OPEN],
      [SLOT_STATUS.CONFIRMED]: !!slotStatusFilter?.[SLOT_STATUS.CONFIRMED],
      [SLOT_STATUS.CANCELLED]: !!slotStatusFilter?.[SLOT_STATUS.CANCELLED],
    },
  };
}

function getServerFilterSignature(filters) {
  const activeStatuses = [SLOT_STATUS.OPEN, SLOT_STATUS.CONFIRMED, SLOT_STATUS.CANCELLED]
    .filter((status) => filters?.slotStatusFilter?.[status])
    .join(",");
  return [
    filters?.division || "",
    filters?.dateFrom || "",
    filters?.dateTo || "",
    filters?.showSlots ? "1" : "0",
    filters?.showEvents ? "1" : "0",
    activeStatuses,
  ].join("|");
}

function collectTeamIds(item) {
  const teamIds = new Set();
  [
    item?.teamId,
    item?.homeTeamId,
    item?.awayTeamId,
    item?.offeringTeamId,
    item?.confirmedTeamId,
  ]
    .map((value) => (value || "").trim())
    .filter(Boolean)
    .forEach((value) => teamIds.add(value));
  return Array.from(teamIds);
}

function matchesTeamFilter(item, teamFilter) {
  const selectedTeamId = (teamFilter || "").trim();
  if (!selectedTeamId) return true;
  return collectTeamIds(item).includes(selectedTeamId);
}

function hasExplicitCalendarFilters(params) {
  return CALENDAR_QUERY_FILTER_KEYS.some((key) => params.has(key));
}

function getRoleDefaultCalendarFilters({ defaults, role, isGlobalAdmin, myCoachTeamId }) {
  const seasonRange = defaults || getDefaultRangeFallback();
  const upcomingRange = getDefaultRangeFallback(new Date(), 30);

  if (role === "LeagueAdmin" || isGlobalAdmin) {
    return {
      division: "",
      dateFrom: seasonRange.from,
      dateTo: seasonRange.to,
      showSlots: true,
      showEvents: false,
      slotTypeFilter: "offer",
      teamFilter: "",
      slotStatusFilter: createSlotStatusFilter({ open: true, confirmed: false, cancelled: false }),
    };
  }

  if (role === "Coach") {
    return {
      division: "",
      dateFrom: seasonRange.from,
      dateTo: seasonRange.to,
      showSlots: true,
      showEvents: true,
      slotTypeFilter: "all",
      teamFilter: "",
      slotStatusFilter: createSlotStatusFilter(),
    };
  }

  return {
    division: "",
    dateFrom: upcomingRange.from,
    dateTo: upcomingRange.to,
    showSlots: true,
    showEvents: true,
    slotTypeFilter: "all",
    teamFilter: "",
    slotStatusFilter: createSlotStatusFilter({ open: false, confirmed: true, cancelled: false }),
  };
}

function readCalendarFiltersFromQuery(params, defaults, context) {
  if (!hasExplicitCalendarFilters(params)) {
    return getRoleDefaultCalendarFilters({ defaults, ...context });
  }

  return {
    division: (params.get("division") || "").trim(),
    dateFrom: (params.get("dateFrom") || "").trim() || defaults.from,
    dateTo: (params.get("dateTo") || "").trim() || defaults.to,
    showSlots: parseBoolParam(params, "showSlots", true),
    showEvents: parseBoolParam(params, "showEvents", true),
    slotTypeFilter: normalizeSlotTypeFilter(params.get("slotType")),
    teamFilter: (params.get("teamId") || "").trim(),
    slotStatusFilter: parseStatusFilter(params),
  };
}

export default function CalendarPage({ me, leagueId, setLeagueId }) {
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

  const myCoachTeamId = useMemo(() => {
    const inLeague = memberships.filter((m) => (m?.leagueId || "").trim() === (leagueId || "").trim());
    const coach = inLeague.find((m) => normalizeRole(m?.role) === "Coach");
    return (coach?.team?.teamId || "").trim();
  }, [memberships, leagueId]);

  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");

  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [showSlots, setShowSlots] = useState(true);
  const [showEvents, setShowEvents] = useState(true);
  const [slotTypeFilter, setSlotTypeFilter] = useState("all");
  const [teamFilter, setTeamFilter] = useState("");
  const [slotStatusFilter, setSlotStatusFilter] = useState(createSlotStatusFilter);

  const [events, setEvents] = useState([]);
  const [slots, setSlots] = useState([]);
  const [fields, setFields] = useState([]);
  const [teams, setTeams] = useState([]);
  const [acceptTeamBySlot, setAcceptTeamBySlot] = useState({});
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const initializedRef = useRef(false);
  const defaultsRef = useRef(getDefaultRangeFallback());
  const loadRequestIdRef = useRef(0);
  const locationSearchRef = useRef(typeof window !== "undefined" ? window.location.search : "");
  const [toast, setToast] = useState(null);
  const { confirmState, requestConfirm, handleConfirm: confirmYes, handleCancel: confirmNo } = useConfirmDialog();
  const { promptState, promptValue, setPromptValue, requestPrompt, handleConfirm, handleCancel } = usePromptDialog();
  const [exportFormat, setExportFormat] = useState("internal");
  const [exporting, setExporting] = useState(false);
  const [editingSlot, setEditingSlot] = useState(null);
  const [editGameDate, setEditGameDate] = useState("");
  const [editStartTime, setEditStartTime] = useState("");
  const [editEndTime, setEditEndTime] = useState("");
  const [editFieldKey, setEditFieldKey] = useState("");
  const [editConflicts, setEditConflicts] = useState([]);
  const [editCheckingConflicts, setEditCheckingConflicts] = useState(false);
  const [savingEdit, setSavingEdit] = useState(false);
  const [useNewCalendarView, setUseNewCalendarView] = useState(() => {
    try {
      return localStorage.getItem("calendar-use-new-view") === "true";
    } catch {
      return false;
    }
  });

  const toggleCalendarView = () => {
    setUseNewCalendarView((prev) => {
      const next = !prev;
      try {
        localStorage.setItem("calendar-use-new-view", String(next));
      } catch {
        // Ignore localStorage errors
      }
      return next;
    });
  };

  const canPickTeam = isGlobalAdmin || role === "LeagueAdmin";
  if (typeof window !== "undefined") {
    locationSearchRef.current = window.location.search;
  }
  const currentServerFilters = useMemo(
    () => buildServerFilters({ division, dateFrom, dateTo, showSlots, showEvents, slotStatusFilter }),
    [division, dateFrom, dateTo, showSlots, showEvents, slotStatusFilter]
  );
  const currentServerFilterSignature = useMemo(
    () => getServerFilterSignature(currentServerFilters),
    [currentServerFilters]
  );

  async function loadAllSlots(slotsQuery) {
    const merged = [];
    let continuationToken = "";
    let pageCount = 0;
    const maxPages = 40;
    const pageSize = 100;

    while (pageCount < maxPages) {
      const query = new URLSearchParams(slotsQuery);
      query.set("pageSize", String(pageSize));
      if (continuationToken) query.set("continuationToken", continuationToken);
      const response = await apiFetch(`/api/slots?${query.toString()}`);
      const items = readPagedItems(response);
      merged.push(...items);

      continuationToken = readContinuationToken(response);
      pageCount += 1;
      if (!continuationToken || items.length === 0) break;
    }

    return merged;
  }

  async function loadMeta() {
    const [divs, flds] = await Promise.all([apiFetch("/api/divisions"), apiFetch("/api/fields")]);
    setDivisions(Array.isArray(divs) ? divs : []);
    setFields(Array.isArray(flds) ? flds : []);

    if (canPickTeam) {
      const t = await apiFetch("/api/teams");
      setTeams(Array.isArray(t) ? t : []);
    } else {
      setTeams([]);
    }
  }

  const applyFiltersFromUrl = useCallback((defaults, search = null) => {
    if (typeof window === "undefined" && typeof search !== "string") return;
    const params = new URLSearchParams(typeof search === "string" ? search : window.location.search);
    const next = readCalendarFiltersFromQuery(params, defaults, {
      role,
      isGlobalAdmin,
      myCoachTeamId,
    });
    setDivision(next.division);
    setDateFrom(next.dateFrom);
    setDateTo(next.dateTo);
    setShowSlots(next.showSlots);
    setShowEvents(next.showEvents);
    setSlotTypeFilter(next.slotTypeFilter);
    setTeamFilter(next.teamFilter);
    setSlotStatusFilter(next.slotStatusFilter);
  }, [isGlobalAdmin, myCoachTeamId, role]);

  async function loadData(overrides = null) {
    const current = overrides ? buildServerFilters(overrides) : currentServerFilters;
    const requestId = ++loadRequestIdRef.current;
    setErr("");
    setLoading(true);
    try {
      const baseQuery = new URLSearchParams();
      if (current.division) baseQuery.set("division", current.division);
      if (current.dateFrom) baseQuery.set("dateFrom", current.dateFrom);
      if (current.dateTo) baseQuery.set("dateTo", current.dateTo);

      const activeStatuses = Object.entries(current.slotStatusFilter || {})
        .filter(([, on]) => on)
        .map(([k]) => k);
      const shouldLoadSlots = current.showSlots && activeStatuses.length > 0;

      const slotsQuery = new URLSearchParams(baseQuery);
      if (activeStatuses.length) slotsQuery.set("status", activeStatuses.join(","));
      slotsQuery.set("excludeAvailability", "1");

      const [ev, sl] = await Promise.all([
        current.showEvents ? apiFetch(`/api/events?${baseQuery.toString()}`) : Promise.resolve([]),
        shouldLoadSlots ? loadAllSlots(slotsQuery) : Promise.resolve([]),
      ]);
      if (requestId !== loadRequestIdRef.current) return;
      setEvents(Array.isArray(ev) ? ev : []);
      setSlots(Array.isArray(sl) ? sl : []);
    } catch (e) {
      if (requestId !== loadRequestIdRef.current) return;
      setErr(e?.message || String(e));
    } finally {
      if (requestId === loadRequestIdRef.current) {
        setLoading(false);
      }
    }
  }

  useEffect(() => {
    (async () => {
      initializedRef.current = false;
      const currentSearch = locationSearchRef.current;
      let defaults = getDefaultRangeFallback();
      try {
        const league = await apiFetch("/api/league");
        const seasonRange = getSeasonRange(league?.season, new Date());
        if (seasonRange) defaults = seasonRange;
      } catch {
        // ignore season config
      }
      defaultsRef.current = defaults;
      applyFiltersFromUrl(defaults, currentSearch);
      try {
        await loadMeta();
      } catch {
        // ignore
      }
      const params = new URLSearchParams(currentSearch);
      const initialFilters = readCalendarFiltersFromQuery(params, defaults, {
        role,
        isGlobalAdmin,
        myCoachTeamId,
      });
      await loadData(initialFilters);
      initializedRef.current = true;
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const onPopState = () => applyFiltersFromUrl(defaultsRef.current);
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, [applyFiltersFromUrl]);

  useEffect(() => {
    if (!initializedRef.current || typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (division) params.set("division", division);
    else params.delete("division");
    if (dateFrom) params.set("dateFrom", dateFrom);
    else params.delete("dateFrom");
    if (dateTo) params.set("dateTo", dateTo);
    else params.delete("dateTo");
    if (showSlots) params.set("showSlots", "1");
    else params.delete("showSlots");
    if (showEvents) params.set("showEvents", "1");
    else params.delete("showEvents");
    if (slotTypeFilter && slotTypeFilter !== "all") params.set("slotType", slotTypeFilter);
    else params.delete("slotType");
    if (teamFilter) params.set("teamId", teamFilter);
    else params.delete("teamId");

    const activeStatuses = [
      SLOT_STATUS.OPEN,
      SLOT_STATUS.CONFIRMED,
      SLOT_STATUS.CANCELLED,
    ].filter((s) => slotStatusFilter[s]);
    if (activeStatuses.length) params.set("status", activeStatuses.join(","));
    else params.delete("status");

    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [division, dateFrom, dateTo, showSlots, showEvents, slotStatusFilter, slotTypeFilter, teamFilter]);

  useEffect(() => {
    if (!initializedRef.current) return;
    const timer = setTimeout(() => {
      loadData(currentServerFilters);
    }, 250);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentServerFilterSignature, currentServerFilters]);
  const visibleSlots = useMemo(
    () =>
      (slots || []).filter(
        (slot) =>
          !slot.isAvailability &&
          !isUnscheduledOpenCapacity(slot) &&
          matchesSlotType(slot.gameType, slotTypeFilter) &&
          matchesTeamFilter(slot, teamFilter)
      ),
    [slots, slotTypeFilter, teamFilter]
  );

  const visibleEvents = useMemo(
    () => (events || []).filter((event) => matchesTeamFilter(event, teamFilter)),
    [events, teamFilter]
  );

  const timeline = useMemo(() => {
    const items = [];

    for (const e of visibleEvents) {
      items.push({
        kind: "event",
        id: e.eventId,
        date: e.eventDate,
        start: e.startTime || "",
        end: e.endTime || "",
        title: `${e.type ? `${e.type}: ` : ""}${e.title || "(Untitled event)"}`,
        subtitle: [
          e.status ? `Status: ${e.status}` : "",
          e.opponentTeamId ? `Opponent: ${e.opponentTeamId}` : "",
          e.location,
          e.division ? `Division: ${e.division}` : "",
          e.teamId ? `Team: ${e.teamId}` : "",
        ]
          .filter(Boolean)
          .join(" | "),
        raw: e,
      });
    }

    for (const s of visibleSlots) {
      const matchup = slotMatchupLabel(s);
      const label = `${matchup || s.offeringTeamId || ""} @ ${s.displayName || `${s.parkName || ""} ${s.fieldName || ""}`}`.trim();
      items.push({
        kind: "slot",
        id: s.slotId,
        date: s.gameDate,
        start: s.startTime || "",
        end: s.endTime || "",
        title: label ? `Slot: ${label}` : `Slot: ${s.slotId}`,
        subtitle: [
          s.division ? `Division: ${s.division}` : "",
          s.status ? `Status: ${s.status}` : "",
          s.confirmedTeamId ? `Confirmed: ${s.confirmedTeamId}` : "",
          matchup ? `Matchup: ${matchup}` : "",
        ]
          .filter(Boolean)
          .join(" | "),
        raw: s,
      });
    }

    return items
      .filter((x) => x.date)
      .sort((a, b) => {
        const ad = `${a.date}T${a.start || "00:00"}`;
        const bd = `${b.date}T${b.start || "00:00"}`;
        return ad.localeCompare(bd) || a.kind.localeCompare(b.kind) || (a.title || "").localeCompare(b.title || "");
      });
  }, [visibleEvents, visibleSlots]);

  const teamsByDivision = useMemo(() => {
    const map = new Map();
    for (const t of teams || []) {
      const div = (t.division || "").trim().toUpperCase();
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

  const teamNameMap = useMemo(() => {
    const map = new Map();
    for (const team of teams || []) {
      const teamId = (team?.teamId || "").trim();
      if (!teamId || map.has(teamId)) continue;
      map.set(teamId, (team?.name || teamId).trim());
    }
    return map;
  }, [teams]);

  const availableTeamOptions = useMemo(() => {
    const options = new Map();
    const divisionKey = (division || "").trim().toUpperCase();

    const addOption = (teamId, label) => {
      const normalizedTeamId = (teamId || "").trim();
      if (!normalizedTeamId) return;
      options.set(normalizedTeamId, label || teamNameMap.get(normalizedTeamId) || normalizedTeamId);
    };

    for (const team of teams || []) {
      const teamDivision = (team?.division || "").trim().toUpperCase();
      if (divisionKey && teamDivision && teamDivision !== divisionKey) continue;
      addOption(team?.teamId, (team?.name || team?.teamId || "").trim());
    }

    for (const item of [...(slots || []), ...(events || [])]) {
      collectTeamIds(item).forEach((teamId) => addOption(teamId));
    }

    if (myCoachTeamId) {
      addOption(myCoachTeamId);
    }

    return Array.from(options.entries())
      .map(([teamId, label]) => ({ teamId, label }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }, [division, events, myCoachTeamId, slots, teamNameMap, teams]);
  const quickViews = (() => {
    const upcomingRange = getDefaultRangeFallback(new Date(), 30);
    const seasonRange = defaultsRef.current || upcomingRange;
    const views = [];

    if (role === "Coach" && myCoachTeamId) {
      views.push({
        id: "my-team",
        label: "My Team",
        title: "Focus the current page view on your team.",
        state: {
          showSlots: true,
          showEvents: true,
          slotTypeFilter: "all",
          teamFilter: myCoachTeamId,
          slotStatusFilter: createSlotStatusFilter(),
        },
      });
    }

    if (role === "Coach") {
      views.push({
        id: "open-games",
        label: "Open Games",
        title: "Focus on open game opportunities to accept.",
        state: {
          showSlots: true,
          showEvents: false,
          slotTypeFilter: "offer",
          teamFilter: "",
          slotStatusFilter: createSlotStatusFilter({ open: true, confirmed: false, cancelled: false }),
        },
      });
    } else if (role === "LeagueAdmin" || isGlobalAdmin) {
      views.push(
        {
          id: "open-slots",
          label: "Open Slots",
          title: "Focus on open slots that still need action.",
          state: {
            showSlots: true,
            showEvents: false,
            slotTypeFilter: "offer",
            teamFilter: "",
            slotStatusFilter: createSlotStatusFilter({ open: true, confirmed: false, cancelled: false }),
          },
        },
        {
          id: "confirmed-games",
          label: "Confirmed Games",
          title: "Focus on confirmed games only.",
          state: {
            showSlots: true,
            showEvents: false,
            slotTypeFilter: "all",
            teamFilter: "",
            slotStatusFilter: createSlotStatusFilter({ open: false, confirmed: true, cancelled: false }),
          },
        }
      );
    } else {
      views.push({
        id: "upcoming",
        label: "Upcoming",
        title: "Show upcoming confirmed games and events.",
        state: {
          dateFrom: upcomingRange.from,
          dateTo: upcomingRange.to,
          showSlots: true,
          showEvents: true,
          slotTypeFilter: "all",
          teamFilter: "",
          slotStatusFilter: createSlotStatusFilter({ open: false, confirmed: true, cancelled: false }),
        },
      });
    }

    views.push(
      {
        id: "next-30",
        label: "Next 30 Days",
        title: "Set the calendar window to the next 30 days.",
        state: {
          dateFrom: upcomingRange.from,
          dateTo: upcomingRange.to,
        },
      },
      {
        id: "full-season",
        label: "Full Season",
        title: "Reset to the season window and the full calendar view.",
        state: {
          dateFrom: seasonRange.from,
          dateTo: seasonRange.to,
          showSlots: true,
          showEvents: true,
          slotTypeFilter: "all",
          teamFilter: "",
          slotStatusFilter: createSlotStatusFilter(),
        },
      }
    );

    return views;
  })();
  // --- Create events ---
  const canCreateEvents = role === "LeagueAdmin";
  const canDeleteAnyEvent = role === "LeagueAdmin";

  const [newType, setNewType] = useState("Practice");
  const [newDivision, setNewDivision] = useState("");
  const [newTeamId, setNewTeamId] = useState("");
  const [newTitle, setNewTitle] = useState("");
  const [newDate, setNewDate] = useState("");
  const [newStart, setNewStart] = useState("");
  const [newEnd, setNewEnd] = useState("");
  const [newLocation, setNewLocation] = useState("");
  const [newNotes, setNewNotes] = useState("");

  async function createEvent() {
    setErr("");
    if (!newTitle.trim()) return setErr("Title is required.");
    if (!newDate.trim()) return setErr("EventDate is required (YYYY-MM-DD).");
    if (!newStart.trim()) return setErr("StartTime is required (HH:MM).");
    if (!newEnd.trim()) return setErr("EndTime is required (HH:MM).");

    try {
      await apiFetch(`/api/events`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          type: newType.trim(),
          division: newDivision.trim(),
          teamId: newTeamId.trim(),
          title: newTitle.trim(),
          eventDate: newDate.trim(),
          startTime: newStart.trim(),
          endTime: newEnd.trim(),
          location: newLocation.trim(),
          notes: newNotes.trim(),
        }),
      });

      setNewType("Practice");
      setNewDivision("");
      setNewTeamId("");
      setNewTitle("");
      setNewDate("");
      setNewStart("");
      setNewEnd("");
      setNewLocation("");
      setNewNotes("");
      await loadData();
      trackEvent("ui_calendar_event_create", { leagueId, division: newDivision.trim() });
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  async function deleteEvent(eventId) {
    if (!eventId) return;
    const ok = await requestConfirm({
      title: "Delete event",
      message: "Delete this event from the calendar?",
      confirmLabel: "Delete",
    });
    if (!ok) return;
    setErr("");
    try {
      await apiFetch(`/api/events/${encodeURIComponent(eventId)}`, { method: "DELETE" });
      await loadData();
      setToast({ tone: "success", message: "Event deleted." });
      trackEvent("ui_calendar_event_delete", { leagueId, eventId });
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  async function requestSlot(slot, requestingTeamId) {
    if (!slot?.slotId || !slot?.division) return;
    const notes = await requestPrompt({
      title: "Add a note",
      message: "Optional notes for the offering coach.",
      placeholder: "Type a note (optional)",
      confirmLabel: "Accept",
    });
    if (notes === null) return;
    setErr("");
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(slot.division)}/${encodeURIComponent(slot.slotId)}/requests`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          notes: String(notes || "").trim(),
          requestingTeamId: requestingTeamId || undefined,
          requestingDivision: slot.division,
        }),
      });
      await loadData();
      setToast({ tone: "success", message: "Accepted. The game is now scheduled on the calendar." });
      trackEvent("ui_calendar_slot_accept", { leagueId, division: slot.division, slotId: slot.slotId });
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  async function cancelSlot(slot) {
    if (!slot?.slotId || !slot?.division) return;
    const ok = await requestConfirm({
      title: "Cancel slot",
      message: "Cancel this game/slot?",
      confirmLabel: "Cancel game",
    });
    if (!ok) return;
    setErr("");
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(slot.division)}/${encodeURIComponent(slot.slotId)}/cancel`, {
        method: "PATCH",
      });
      await loadData();
      setToast({ tone: "success", message: "Slot cancelled." });
      trackEvent("ui_calendar_slot_cancel", { leagueId, division: slot.division, slotId: slot.slotId });
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  function canCancelSlot(slot) {
    if (!slot) return false;
    if (isGlobalAdmin) return true;
    if (role === "LeagueAdmin") return true;
    if (role !== "Coach") return false;
    const my = (myCoachTeamId || "").trim();
    if (!my) return false;
    const offering = (slot.offeringTeamId || "").trim();
    const confirmed = (slot.confirmedTeamId || "").trim();
    if (slot.status === "Open") return my && offering && my === offering;
    if (slot.status === "Confirmed") return my === offering || (confirmed && my === confirmed);
    return false;
  }

  function canEditSlot(slot) {
    if (!slot) return false;
    if (slot.isAvailability) return false;
    if ((slot.status || "") === SLOT_STATUS.CANCELLED) return false;
    return isGlobalAdmin || role === "LeagueAdmin";
  }

  function openEditSlot(slot) {
    if (!slot || !canEditSlot(slot)) return;
    setErr("");
    setEditingSlot(slot);
    setEditGameDate(slot.gameDate || "");
    setEditStartTime(slot.startTime || "");
    setEditEndTime(slot.endTime || "");
    setEditFieldKey(slot.fieldKey || "");
    setEditConflicts([]);
  }

  function closeEditSlot() {
    if (savingEdit) return;
    setEditingSlot(null);
    setEditGameDate("");
    setEditStartTime("");
    setEditEndTime("");
    setEditFieldKey("");
    setEditConflicts([]);
  }

  async function checkEditConflicts(next) {
    if (!editingSlot?.division || !editingSlot?.slotId) {
      setEditConflicts([]);
      return;
    }
    if (!next?.gameDate || !next?.fieldKey) {
      setEditConflicts([]);
      return;
    }
    const nextStartMin = parseMinutes(next.startTime);
    const nextEndMin = parseMinutes(next.endTime);
    if (nextStartMin == null || nextEndMin == null || nextStartMin >= nextEndMin) {
      setEditConflicts([]);
      return;
    }

    setEditCheckingConflicts(true);
    try {
      const params = new URLSearchParams();
      params.set("division", editingSlot.division);
      params.set("dateFrom", next.gameDate);
      params.set("dateTo", next.gameDate);
      params.set("fieldKey", next.fieldKey);
      params.set("status", `${SLOT_STATUS.OPEN},${SLOT_STATUS.CONFIRMED}`);
      const result = await apiFetch(`/api/slots?${params.toString()}`);
      const candidates = readPagedItems(result);
      const overlaps = candidates.filter((candidate) => {
        if (!candidate || candidate.slotId === editingSlot.slotId) return false;
        if ((candidate.status || "") === SLOT_STATUS.CANCELLED) return false;
        const candidateStart = parseMinutes(candidate.startTime);
        const candidateEnd = parseMinutes(candidate.endTime);
        if (candidateStart == null || candidateEnd == null) return false;
        return nextStartMin < candidateEnd && nextEndMin > candidateStart;
      });
      setEditConflicts(overlaps);
    } catch {
      setEditConflicts([]);
    } finally {
      setEditCheckingConflicts(false);
    }
  }

  async function saveSlotEdit() {
    if (!editingSlot?.slotId || !editingSlot?.division) return;
    if (!editGameDate.trim()) return setErr("GameDate is required.");
    if (!editStartTime.trim() || !editEndTime.trim()) return setErr("StartTime and EndTime are required.");
    if (!editFieldKey.trim()) return setErr("Field is required.");

    const startMin = parseMinutes(editStartTime);
    const endMin = parseMinutes(editEndTime);
    if (startMin == null || endMin == null || startMin >= endMin) {
      return setErr("StartTime must be before EndTime in HH:MM format.");
    }

    if (editConflicts.length > 0) {
      return setErr("Selected field/time overlaps an existing slot. Choose another field or time.");
    }

    setErr("");
    setSavingEdit(true);
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(editingSlot.division)}/${encodeURIComponent(editingSlot.slotId)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          gameDate: editGameDate.trim(),
          startTime: editStartTime.trim(),
          endTime: editEndTime.trim(),
          fieldKey: editFieldKey.trim(),
        }),
      });
      closeEditSlot();
      await loadData();
      setToast({ tone: "success", message: "Game updated." });
      trackEvent("ui_calendar_slot_edit", {
        leagueId,
        division: editingSlot.division,
        slotId: editingSlot.slotId,
      });
    } catch (e) {
      setErr(e?.message || String(e));
      const apiConflicts = Array.isArray(e?.details?.conflicts) ? e.details.conflicts : [];
      if (apiConflicts.length) {
        setEditConflicts(apiConflicts);
      }
    } finally {
      setSavingEdit(false);
    }
  }

  function toggleSlotStatus(status) {
    setSlotStatusFilter((prev) => ({ ...prev, [status]: !prev[status] }));
  }

  function activateSlotFilter(status) {
    setShowSlots(true);
    setSlotStatusFilter(
      createSlotStatusFilter({
        open: status === SLOT_STATUS.OPEN,
        confirmed: status === SLOT_STATUS.CONFIRMED,
        cancelled: status === SLOT_STATUS.CANCELLED,
      })
    );
  }

  function setAcceptTeam(slotId, teamId) {
    setAcceptTeamBySlot((prev) => ({ ...prev, [slotId]: teamId }));
  }

  function applyQuickView(state) {
    if (!state) return;
    if (Object.prototype.hasOwnProperty.call(state, "dateFrom")) setDateFrom(state.dateFrom || "");
    if (Object.prototype.hasOwnProperty.call(state, "dateTo")) setDateTo(state.dateTo || "");
    if (Object.prototype.hasOwnProperty.call(state, "showSlots")) setShowSlots(!!state.showSlots);
    if (Object.prototype.hasOwnProperty.call(state, "showEvents")) setShowEvents(!!state.showEvents);
    if (Object.prototype.hasOwnProperty.call(state, "slotTypeFilter")) {
      setSlotTypeFilter(state.slotTypeFilter || "all");
    }
    if (Object.prototype.hasOwnProperty.call(state, "teamFilter")) setTeamFilter(state.teamFilter || "");
    if (Object.prototype.hasOwnProperty.call(state, "slotStatusFilter")) {
      const nextSlotStatus = state.slotStatusFilter || {};
      setSlotStatusFilter(
        createSlotStatusFilter({
          open: Object.prototype.hasOwnProperty.call(nextSlotStatus, SLOT_STATUS.OPEN)
            ? !!nextSlotStatus[SLOT_STATUS.OPEN]
            : !!nextSlotStatus.open,
          confirmed: Object.prototype.hasOwnProperty.call(nextSlotStatus, SLOT_STATUS.CONFIRMED)
            ? !!nextSlotStatus[SLOT_STATUS.CONFIRMED]
            : !!nextSlotStatus.confirmed,
          cancelled: Object.prototype.hasOwnProperty.call(nextSlotStatus, SLOT_STATUS.CANCELLED)
            ? !!nextSlotStatus[SLOT_STATUS.CANCELLED]
            : !!nextSlotStatus.cancelled,
        })
      );
    }
  }

  function renderSlotActions(slot) {
    if (!slot?.slotId) return null;
    const actionKey = slot.slotId;
    const canAdminAccept = canPickTeam && canAcceptSlot(slot);
    const canCoachAccept =
      !canPickTeam && role !== "Viewer" && canAcceptSlot(slot) && (slot?.offeringTeamId || "") !== myCoachTeamId;
    const canEdit = canEditSlot(slot);
    const canCancel = canCancelSlot(slot) && (slot?.status || "") !== "Cancelled";

    if (!canAdminAccept && !canCoachAccept && !canEdit && !canCancel) {
      return null;
    }

    const divisionKey = (slot?.division || "").trim().toUpperCase();
    const teamsForDivision = teamsByDivision.get(divisionKey) || [];
    const selectedTeamId = acceptTeamBySlot[actionKey] || "";

    return (
      <>
        {canAdminAccept ? (
          <>
            <select
              value={selectedTeamId}
              onChange={(e) => setAcceptTeam(actionKey, e.target.value)}
              title="Pick a team to accept this offer as."
            >
              <option value="">Select team</option>
              {teamsForDivision.map((team) => (
                <option key={team.teamId} value={team.teamId}>
                  {team.name || team.teamId}
                </option>
              ))}
            </select>
            <button
              className="btn btn--primary"
              type="button"
              onClick={() => requestSlot(slot, selectedTeamId)}
              disabled={!selectedTeamId}
              title="Accept this offer on behalf of the selected team."
            >
              Accept as
            </button>
          </>
        ) : null}
        {canCoachAccept ? (
          <button
            className="btn btn--primary"
            type="button"
            onClick={() => requestSlot(slot)}
            title="Accept this open slot and confirm the game."
          >
            Accept
          </button>
        ) : null}
        {canEdit ? (
          <button
            className="btn"
            type="button"
            onClick={() => openEditSlot(slot)}
            title="Edit date, time, or field for this game."
          >
            Edit
          </button>
        ) : null}
        {canCancel ? (
          <button
            className="btn"
            type="button"
            onClick={() => cancelSlot(slot)}
            title="Cancel this game/slot."
          >
            Cancel
          </button>
        ) : null}
      </>
    );
  }

  function renderEventActions(event) {
    if (!canDeleteAnyEvent || !event?.eventId) return null;
    return (
      <button
        className="btn"
        type="button"
        onClick={() => deleteEvent(event.eventId)}
        title="Delete this event from the calendar."
      >
        Delete
      </button>
    );
  }

  useEffect(() => {
    if (!editingSlot) return;
    const timer = setTimeout(() => {
      checkEditConflicts({
        gameDate: editGameDate,
        startTime: editStartTime,
        endTime: editEndTime,
        fieldKey: editFieldKey,
      });
    }, 250);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editingSlot, editGameDate, editStartTime, editEndTime, editFieldKey]);

  const activeSlotStatuses = useMemo(
    () => Object.entries(slotStatusFilter).filter(([, on]) => on).map(([k]) => k),
    [slotStatusFilter]
  );

  async function exportSchedule() {
    if (!division) {
      setToast({ tone: "error", message: "Please select a division to export." });
      return;
    }

    if (role !== "LeagueAdmin" && !isGlobalAdmin) {
      setToast({ tone: "error", message: "Only league admins can export schedules." });
      return;
    }

    setExporting(true);
    try {
      const params = new URLSearchParams();
      params.set("division", division);
      params.set("format", exportFormat);
      if (dateFrom) params.set("dateFrom", dateFrom);
      if (dateTo) params.set("dateTo", dateTo);

      const activeStatuses = Object.entries(slotStatusFilter)
        .filter(([, enabled]) => enabled)
        .map(([status]) => status);
      if (activeStatuses.length > 0 && activeStatuses.length < 3) {
        params.set("status", activeStatuses.join(","));
      }

      const url = `/api/schedule/export?${params.toString()}`;
      const headers = { "x-league-id": leagueId };

      const resp = await fetch(url, { headers });
      if (!resp.ok) {
        const error = await resp.json().catch(() => ({ error: { message: "Export failed" } }));
        throw new Error(error?.error?.message || "Export failed");
      }

      const blob = await resp.blob();
      const downloadUrl = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = downloadUrl;
      a.download = `schedule-${division}-${exportFormat}-${new Date().toISOString().slice(0, 10)}.csv`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(downloadUrl);

      setToast({ tone: "success", message: `Schedule exported as ${exportFormat}.` });
      trackEvent("ui_calendar_export", { leagueId, division, format: exportFormat });
    } catch (e) {
      setToast({ tone: "error", message: e.message || "Failed to export schedule." });
    } finally {
      setExporting(false);
    }
  }

  function statusClassForItem(item) {
    const raw = (item?.raw?.status || "").toLowerCase();
    if (item.kind === "event") return "timelineItem timelineItem--event";
    if (!raw) return "timelineItem timelineItem--slot";
    return `timelineItem timelineItem--slot status-${raw}`;
  }

  function statusLabelForItem(item) {
    if (item.kind === "event") return item.raw?.status || "Scheduled";
    return item.raw?.status || "Open";
  }

  if (loading) return <StatusCard title="Loading" message="Loading calendar..." />;

  return (
    <div className="stack">
      {err ? <StatusCard tone="error" title="Unable to load calendar" message={err} /> : null}
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />
      <ConfirmDialog
        open={!!confirmState}
        title={confirmState?.title}
        message={confirmState?.message}
        confirmLabel={confirmState?.confirmLabel}
        cancelLabel={confirmState?.cancelLabel}
        onConfirm={confirmYes}
        onCancel={confirmNo}
      />
      <PromptDialog
        open={!!promptState}
        title={promptState?.title}
        message={promptState?.message}
        placeholder={promptState?.placeholder}
        confirmLabel={promptState?.confirmLabel}
        cancelLabel={promptState?.cancelLabel}
        readOnly={!!promptState?.readOnly}
        value={promptValue}
        onChange={setPromptValue}
        onConfirm={handleConfirm}
        onCancel={handleCancel}
      />
      {editingSlot ? (
        <div className="modalOverlay" role="presentation" onClick={closeEditSlot}>
          <div
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-label="Edit scheduled game"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="modal__header">Edit scheduled game</div>
            <div className="modal__body">
              Update date, time, or field. Conflicts are shown before saving.
            </div>
            <div className="grid2">
              <label>
                GameDate (YYYY-MM-DD)
                <input value={editGameDate} onChange={(e) => setEditGameDate(e.target.value)} placeholder="YYYY-MM-DD" />
              </label>
              <label>
                Field
                <select value={editFieldKey} onChange={(e) => setEditFieldKey(e.target.value)}>
                  <option value="">Select field</option>
                  {fields.map((f) => (
                    <option key={f.fieldKey} value={f.fieldKey}>
                      {f.displayName || f.fieldKey}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                StartTime (HH:MM)
                <input value={editStartTime} onChange={(e) => setEditStartTime(e.target.value)} placeholder="HH:MM" />
              </label>
              <label>
                EndTime (HH:MM)
                <input value={editEndTime} onChange={(e) => setEditEndTime(e.target.value)} placeholder="HH:MM" />
              </label>
            </div>
            <div className="mt-2">
              {editCheckingConflicts ? <div className="subtle">Checking field conflicts...</div> : null}
              {!editCheckingConflicts && editConflicts.length > 0 ? (
                <div className="callout callout--error">
                  <div className="font-bold mb-1">Potential conflicts on this field</div>
                  <div className="stack gap-1">
                    {editConflicts.slice(0, 10).map((conflict, idx) => (
                      <div key={`${conflict.slotId || idx}-${idx}`} className="subtle">
                        {(conflict.startTime || "")}-{(conflict.endTime || "")} {conflict.division ? `(${conflict.division})` : ""}{" "}
                        {conflict.homeTeamId || conflict.offeringTeamId || "TBD"}{conflict.awayTeamId ? ` vs ${conflict.awayTeamId}` : ""}{" "}
                        [{conflict.status || "Open"}]
                      </div>
                    ))}
                    {editConflicts.length > 10 ? <div className="subtle">Showing first 10 conflicts.</div> : null}
                  </div>
                </div>
              ) : null}
            </div>
            <div className="modal__actions">
              <button className="btn btn--ghost" type="button" onClick={closeEditSlot} disabled={savingEdit}>
                Cancel
              </button>
              <button className="btn btn--primary" type="button" onClick={saveSlotEdit} disabled={savingEdit || editCheckingConflicts}>
                {savingEdit ? "Saving..." : "Save"}
              </button>
            </div>
          </div>
        </div>
      ) : null}

      <div className="calendarSplit">
        <div className="card">
        <div className="cardTitle">
          Calendar filters
          <span
            className="hint"
            title="Calendar results update automatically. Exports follow league, division, date, slots/events, and status filters. Slot type and team only affect the current page view."
          >
            ?
          </span>
        </div>
        {quickViews.length > 0 ? (
          <div className="row row--wrap mt-2">
            <div className="pill">Quick views</div>
            {quickViews.map((view) => (
              <button
                key={view.id}
                className="btn btn--ghost"
                type="button"
                onClick={() => applyQuickView(view.state)}
                title={view.title}
              >
                {view.label}
              </button>
            ))}
          </div>
        ) : null}
        <div className="row filterRow row--wrap">
          <LeaguePicker leagueId={leagueId} setLeagueId={setLeagueId} me={me} label="League" />
          <label title="Limit the calendar to one division, or show all.">
            Division
            <select value={division} onChange={(e) => setDivision(e.target.value)}>
              <option value="">All</option>
              {divisions.map((d) => (
                <option key={d.code} value={d.code}>
                  {d.name} ({d.code})
                </option>
              ))}
            </select>
          </label>
          <label title="Filter the current page view to one team." className={showSlots || showEvents ? "" : "opacity-50"}>
            Team
            <select
              value={teamFilter}
              onChange={(e) => setTeamFilter(e.target.value)}
              disabled={!showSlots && !showEvents}
            >
              <option value="">All</option>
              {availableTeamOptions.map((team) => (
                <option key={team.teamId} value={team.teamId}>
                  {team.label}
                </option>
              ))}
            </select>
          </label>
          <label title="Start date for the calendar view.">
            From
            <input value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label title="End date for the calendar view.">
            To
            <input value={dateTo} onChange={(e) => setDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label className="inlineCheck" title="Show or hide slot offers.">
            <input type="checkbox" checked={showSlots} onChange={(e) => setShowSlots(e.target.checked)} />
            Slots
          </label>
          <label className="inlineCheck" title="Show or hide league events.">
            <input type="checkbox" checked={showEvents} onChange={(e) => setShowEvents(e.target.checked)} />
            Events
          </label>
          <label title="Filter offers vs requests in the current page view." className={showSlots ? "" : "opacity-50"}>
            Slot type
            <select
              value={slotTypeFilter}
              onChange={(e) => setSlotTypeFilter(e.target.value)}
              disabled={!showSlots}
            >
              <option value="all">All</option>
              <option value="offer">Offers</option>
              <option value="request">Requests</option>
            </select>
          </label>
          <button
            className="btn"
            onClick={() => loadData(currentServerFilters)}
            title="Reload the calendar with the current filters."
          >
            Reload
          </button>
          {(role === "LeagueAdmin" || isGlobalAdmin) && (
            <>
              <label title="Choose export format for schedule CSV.">
                Export format
                <select value={exportFormat} onChange={(e) => setExportFormat(e.target.value)} disabled={!division}>
                  <option value="internal">Internal</option>
                  <option value="sportsengine">SportsEngine</option>
                  <option value="gamechanger">GameChanger</option>
                </select>
              </label>
              <button
                className="btn btn--primary"
                onClick={exportSchedule}
                disabled={!division || exporting}
                title="Export confirmed games for the selected division as CSV."
              >
                {exporting ? "Exporting..." : "Export Schedule"}
              </button>
            </>
          )}
        </div>
        <div className="row mt-3">
          <div className="pill">Slot status</div>
          {[SLOT_STATUS.OPEN, SLOT_STATUS.CONFIRMED, SLOT_STATUS.CANCELLED].map((status) => (
            <label key={status} className="pill cursor-pointer" title={`Show ${status.toLowerCase()} offers on the calendar.`}>
              <input
                type="checkbox"
                checked={!!slotStatusFilter[status]}
                onChange={() => toggleSlotStatus(status)}
                className="mr-2"
                disabled={!showSlots}
              />
              {status}
            </label>
          ))}
          {showSlots && activeSlotStatuses.length === 0 ? (
            <div className="muted">Select at least one status to show slots.</div>
          ) : null}
        </div>
        <div className="row row--between mt-3">
          <div className="muted">
            Showing calendar items for <b>{leagueId || "(no league)"}</b>.
          </div>
        </div>
        <div className="muted mt-2">
          Calendar updates automatically when filters change. Schedule exports follow league, division, date, slots/events, and status filters. Slot type and team only affect the current page view.
        </div>
        </div>

        <div className="card">
          <div className="row row--between items-center mb-2">
            <div className="cardTitle m-0">Calendar</div>
            <button
              className="btn btn--ghost"
              onClick={toggleCalendarView}
              title={useNewCalendarView ? "Switch to classic list view" : "Switch to compact week card view"}
            >
              {useNewCalendarView ? "Classic View" : "Week Cards"}
            </button>
          </div>
          {role === "Coach" && !myCoachTeamId ? (
            <div className="callout callout--error">
              Coach actions require a team assignment. Ask a LeagueAdmin to assign your team.
            </div>
          ) : null}
          {timeline.length === 0 ? <div className="muted">No items in this range.</div> : null}

          {useNewCalendarView ? (
            <CalendarView
              slots={visibleSlots}
              events={visibleEvents}
              defaultView="week-cards"
              onSlotClick={isGlobalAdmin || role === "LeagueAdmin" ? openEditSlot : undefined}
              renderSlotActions={renderSlotActions}
              renderEventActions={renderEventActions}
              showViewToggle={true}
            />
          ) : (
            <div className="stack">
            {timeline.map((it) => {
              if (it.kind === "slot") {
                const slot = it.raw;
                const fieldName = slot?.displayName || `${slot?.parkName || ""} ${slot?.fieldName || ""}`.trim();
                const matchup = slotMatchupLabel(slot);
                const division = slot?.division;

                return (
                  <div key={`${it.kind}:${it.id}`} className={statusClassForItem(it)}>
                    {/* Slot Header: Date, Time, Division, Status */}
                    <div className="slotHeader">
                      <div className="slotHeader__meta">
                        <span className="slotHeader__date">{it.date}</span>
                        {it.start && (
                          <>
                            <span>|</span>
                            <span className="slotHeader__time">
                              {it.start}{it.end ? `-${it.end}` : ""}
                            </span>
                          </>
                        )}
                        {division && (
                          <>
                            <span>|</span>
                            <span className="slotHeader__division">{division}</span>
                          </>
                        )}
                      </div>
                      <button
                        className={`statusBadge statusBadge--link status-${(statusLabelForItem(it) || "").toLowerCase()}`}
                        type="button"
                        onClick={() => activateSlotFilter(statusLabelForItem(it))}
                        title="Filter the calendar to this status"
                      >
                        {statusLabelForItem(it)}
                      </button>
                    </div>

                    {/* Slot Body: Field, Matchup, Actions */}
                    <div className="slotBody">
                      <div className="slotBody__primary">
                        <div className="slotField">
                          <span className="slotField__icon">@</span>
                          <span>{fieldName || "Field TBD"}</span>
                        </div>
                        {matchup && <div className="slotMatchup">{matchup}</div>}
                        {slot?.confirmedTeamId && (
                          <div className="slotDivision">Confirmed: {slot.confirmedTeamId}</div>
                        )}
                      </div>
                      <div className="slotBody__actions">
                        {renderSlotActions(slot)}
                      </div>
                    </div>
                  </div>
                );
              }

              // Event rendering (unchanged)
              return (
                <div key={`${it.kind}:${it.id}`} className={statusClassForItem(it)}>
                  <div className="row row--between">
                    <div>
                      <div className="font-bold">
                        {it.date} {it.start ? `${it.start}${it.end ? `-${it.end}` : ""}` : ""} - {it.title}
                      </div>
                      {it.subtitle ? <div className="muted">{it.subtitle}</div> : null}
                      {it.raw?.notes ? <div className="mt-2">{it.raw.notes}</div> : null}
                    </div>
                    <div className="row">
                      <span className={`statusBadge status-${(statusLabelForItem(it) || "").toLowerCase()}`}>
                        {statusLabelForItem(it)}
                      </span>
                      {renderEventActions(it.raw)}
                    </div>
                  </div>
                </div>
              );
            })}
            </div>
          )}
        </div>
      </div>

      {canCreateEvents ? (
        <div className="card">
          <details>
            <summary className="cursor-pointer font-bold">
              {role === "Coach" ? "Request a game" : "Add an event"}
            </summary>
            <div className="grid2 mt-3">
              {role === "LeagueAdmin" ? (
                <label>
                  Type
                  <select value={newType} onChange={(e) => setNewType(e.target.value)}>
                    <option value="Practice">Practice</option>
                    <option value="Meeting">Meeting</option>
                    <option value="Clinic">Clinic</option>
                    <option value="Other">Other</option>
                  </select>
                </label>
              ) : null}
              <label>
                Title
                <input value={newTitle} onChange={(e) => setNewTitle(e.target.value)} />
              </label>
              <label>
                Location
                <input value={newLocation} onChange={(e) => setNewLocation(e.target.value)} />
              </label>
              <label>
                EventDate (YYYY-MM-DD)
                <input value={newDate} onChange={(e) => setNewDate(e.target.value)} placeholder="2026-04-05" />
              </label>
              <label>
                StartTime (HH:MM)
                <input value={newStart} onChange={(e) => setNewStart(e.target.value)} placeholder="18:00" />
              </label>
              <label>
                EndTime (HH:MM)
                <input value={newEnd} onChange={(e) => setNewEnd(e.target.value)} placeholder="19:30" />
              </label>
              <label>
                Notes
                <input value={newNotes} onChange={(e) => setNewNotes(e.target.value)} />
              </label>
              {role === "LeagueAdmin" ? (
                <>
                  <label>
                    Division (optional)
                    <input value={newDivision} onChange={(e) => setNewDivision(e.target.value)} placeholder="10U" />
                  </label>
                  <label>
                    Team ID (optional)
                    <input value={newTeamId} onChange={(e) => setNewTeamId(e.target.value)} placeholder="TIGERS" />
                  </label>
                </>
              ) : null}
            </div>
            <div className="row mt-3">
              <button className="btn btn--primary" onClick={createEvent}>
                Create Event
              </button>
            </div>
          </details>
        </div>
      ) : null}
    </div>
  );
}
