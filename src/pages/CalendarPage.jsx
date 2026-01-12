import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";
import { SLOT_STATUS } from "../lib/constants";
import LeaguePicker from "../components/LeaguePicker";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";
import { ConfirmDialog, PromptDialog } from "../components/Dialogs";
import { useConfirmDialog, usePromptDialog } from "../lib/useDialogs";
import { trackEvent } from "../lib/telemetry";

function toDateInputValue(d) {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

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
    return {
      [SLOT_STATUS.OPEN]: true,
      [SLOT_STATUS.CONFIRMED]: true,
      [SLOT_STATUS.CANCELLED]: false,
    };
  }
  const set = new Set(raw.split(",").map((s) => s.trim()).filter(Boolean));
  return {
    [SLOT_STATUS.OPEN]: set.has(SLOT_STATUS.OPEN),
    [SLOT_STATUS.CONFIRMED]: set.has(SLOT_STATUS.CONFIRMED),
    [SLOT_STATUS.CANCELLED]: set.has(SLOT_STATUS.CANCELLED),
  };
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

function canAcceptSlot(slot) {
  if (!slot || (slot.status || "") !== "Open") return false;
  if (slot.isAvailability) return false;
  if ((slot.awayTeamId || "").trim() && !slot.isExternalOffer) return false;
  return true;
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

export default function CalendarPage({ me, leagueId, setLeagueId }) {
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
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
    return (coach?.teamId || coach?.team?.teamId || "").trim();
  }, [memberships, leagueId]);

  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");

  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [showSlots, setShowSlots] = useState(true);
  const [showEvents, setShowEvents] = useState(true);
  const [slotTypeFilter, setSlotTypeFilter] = useState("all");
  const [slotStatusFilter, setSlotStatusFilter] = useState({
    [SLOT_STATUS.OPEN]: true,
    [SLOT_STATUS.CONFIRMED]: true,
    [SLOT_STATUS.CANCELLED]: false,
  });

  const [events, setEvents] = useState([]);
  const [slots, setSlots] = useState([]);
  const [teams, setTeams] = useState([]);
  const [acceptTeamBySlot, setAcceptTeamBySlot] = useState({});
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const initializedRef = useRef(false);
  const defaultsRef = useRef(getDefaultRangeFallback());
  const [toast, setToast] = useState(null);
  const { confirmState, requestConfirm, handleConfirm: confirmYes, handleCancel: confirmNo } = useConfirmDialog();
  const { promptState, promptValue, setPromptValue, requestPrompt, handleConfirm, handleCancel } = usePromptDialog();

  const canPickTeam = isGlobalAdmin || role === "LeagueAdmin";

  async function loadMeta() {
    const divs = await apiFetch("/api/divisions");
    setDivisions(Array.isArray(divs) ? divs : []);

    if (canPickTeam) {
      const t = await apiFetch("/api/teams");
      setTeams(Array.isArray(t) ? t : []);
    } else {
      setTeams([]);
    }
  }

  const applyFiltersFromUrl = useCallback((defaults) => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    setDivision((params.get("division") || "").trim());
    setDateFrom((params.get("dateFrom") || "").trim() || defaults.from);
    setDateTo((params.get("dateTo") || "").trim() || defaults.to);
    setShowSlots(parseBoolParam(params, "showSlots", true));
    setShowEvents(parseBoolParam(params, "showEvents", true));
    setSlotTypeFilter(normalizeSlotTypeFilter(params.get("slotType")));
    setSlotStatusFilter(parseStatusFilter(params));
  }, []);

  async function loadData(overrides = null) {
    const current = overrides || {
      division,
      dateFrom,
      dateTo,
      showSlots,
      showEvents,
      slotStatusFilter,
    };
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

      const [ev, sl] = await Promise.all([
        current.showEvents ? apiFetch(`/api/events?${baseQuery.toString()}`) : Promise.resolve([]),
        shouldLoadSlots ? apiFetch(`/api/slots?${slotsQuery.toString()}`) : Promise.resolve([]),
      ]);
      setEvents(Array.isArray(ev) ? ev : []);
      setSlots(Array.isArray(sl) ? sl : []);
    } catch (e) {
      setErr(e?.message || String(e));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    (async () => {
      let defaults = getDefaultRangeFallback();
      try {
        const league = await apiFetch("/api/league");
        const seasonRange = getSeasonRange(league?.season, new Date());
        if (seasonRange) defaults = seasonRange;
      } catch {
        // ignore season config
      }
      defaultsRef.current = defaults;
      applyFiltersFromUrl(defaults);
      try {
        await loadMeta();
      } catch {
        // ignore
      }
      const params = new URLSearchParams(typeof window !== "undefined" ? window.location.search : "");
      const initialFilters = {
        division: (params.get("division") || "").trim(),
        dateFrom: (params.get("dateFrom") || "").trim() || defaults.from,
        dateTo: (params.get("dateTo") || "").trim() || defaults.to,
        showSlots: parseBoolParam(params, "showSlots", true),
        showEvents: parseBoolParam(params, "showEvents", true),
        slotStatusFilter: parseStatusFilter(params),
      };
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

    const activeStatuses = [
      SLOT_STATUS.OPEN,
      SLOT_STATUS.CONFIRMED,
      SLOT_STATUS.CANCELLED,
    ].filter((s) => slotStatusFilter[s]);
    if (activeStatuses.length) params.set("status", activeStatuses.join(","));
    else params.delete("status");

    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [division, dateFrom, dateTo, showSlots, showEvents, slotStatusFilter, slotTypeFilter]);

  const timeline = useMemo(() => {
    const items = [];

    for (const e of events || []) {
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

    for (const s of slots || []) {
      if (s.isAvailability) continue;
      if (!matchesSlotType(s.gameType, slotTypeFilter)) continue;
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
  }, [events, slots, slotTypeFilter]);

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

  function toggleSlotStatus(status) {
    setSlotStatusFilter((prev) => ({ ...prev, [status]: !prev[status] }));
  }

  function activateSlotFilter(status) {
    setShowSlots(true);
    setSlotStatusFilter({
      [SLOT_STATUS.OPEN]: status === SLOT_STATUS.OPEN,
      [SLOT_STATUS.CONFIRMED]: status === SLOT_STATUS.CONFIRMED,
      [SLOT_STATUS.CANCELLED]: status === SLOT_STATUS.CANCELLED,
    });
  }

  function setAcceptTeam(slotId, teamId) {
    setAcceptTeamBySlot((prev) => ({ ...prev, [slotId]: teamId }));
  }

  const activeSlotStatuses = useMemo(
    () => Object.entries(slotStatusFilter).filter(([, on]) => on).map(([k]) => k),
    [slotStatusFilter]
  );

  const subscribeInfo = useMemo(() => {
    if (!leagueId) return { url: "", webcal: "" };
    const params = new URLSearchParams();
    params.set("leagueId", leagueId);
    if (division) params.set("division", division);
    if (dateFrom) params.set("dateFrom", dateFrom);
    if (dateTo) params.set("dateTo", dateTo);
    const shouldIncludeSlots = showSlots && activeSlotStatuses.length > 0;
    params.set("includeSlots", String(shouldIncludeSlots));
    params.set("includeEvents", String(showEvents));
    if (shouldIncludeSlots && activeSlotStatuses.length) params.set("status", activeSlotStatuses.join(","));

    const origin = typeof window !== "undefined" ? window.location.origin : "";
    const url = origin ? `${origin}/api/calendar/ics?${params.toString()}` : "";
    const webcal = url ? url.replace(/^https?:\/\//i, "webcal://") : "";
    return { url, webcal };
  }, [leagueId, division, dateFrom, dateTo, showSlots, showEvents, activeSlotStatuses]);

  async function copySubscribeUrl() {
    if (!subscribeInfo.url) return;
    try {
      await navigator.clipboard.writeText(subscribeInfo.url);
      setToast({ tone: "success", message: "Subscribe link copied." });
    } catch {
      await requestPrompt({
        title: "Copy subscribe link",
        message: "Copy the link below.",
        defaultValue: subscribeInfo.url,
        readOnly: true,
        confirmLabel: "Close",
        cancelLabel: "Close",
      });
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

      <div className="calendarSplit">
        <div className="card">
        <div className="cardTitle">
          Calendar filters
          <span className="hint" title="Filter what appears on the calendar and subscription link.">?</span>
        </div>
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
          <label title="Filter offers vs requests." className={showSlots ? "" : "opacity-50"}>
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
          <button className="btn" onClick={loadData} title="Refresh the calendar list with the current filters.">
            Refresh
          </button>
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
          <div className="row">
            {subscribeInfo.webcal ? (
              <a className="btn" href={subscribeInfo.webcal} title="Open the subscription link in your calendar app.">
                Subscribe
              </a>
            ) : null}
            {subscribeInfo.url ? (
              <button className="btn btn--ghost" onClick={copySubscribeUrl} title="Copy the filtered calendar link.">
                Copy link
              </button>
            ) : null}
          </div>
        </div>
        <div className="muted mt-2">
          Subscribe link reflects the current filters and date range.
        </div>
        </div>

        <div className="card">
          <div className="cardTitle">Calendar</div>
          {role === "Coach" && !myCoachTeamId ? (
            <div className="callout callout--error">
              Coach actions require a team assignment. Ask a LeagueAdmin to assign your team.
            </div>
          ) : null}
          {timeline.length === 0 ? <div className="muted">No items in this range.</div> : null}
          <div className="stack">
            {timeline.map((it) => (
              <div key={`${it.kind}:${it.id}`} className={statusClassForItem(it)}>
                <div className="row row--between">
                  <div>
                    <div className="font-bold">
                      {it.date} {it.start ? `${it.start}${it.end ? `-${it.end}` : ""}` : ""} - {it.title}
                    </div>
                    {it.subtitle ? <div className="muted">{it.subtitle}</div> : null}
                    {it.kind === "event" && it.raw?.notes ? <div className="mt-2">{it.raw.notes}</div> : null}
                  </div>
                  <div className="row">
                    {it.kind === "slot" ? (
                      <button
                        className={`statusBadge statusBadge--link status-${(statusLabelForItem(it) || "").toLowerCase()}`}
                        type="button"
                        onClick={() => activateSlotFilter(statusLabelForItem(it))}
                        title="Filter the calendar to this status"
                      >
                        {statusLabelForItem(it)}
                      </button>
                    ) : (
                      <span className={`statusBadge status-${(statusLabelForItem(it) || "").toLowerCase()}`}>
                        {statusLabelForItem(it)}
                      </span>
                    )}
                    {it.kind === "slot" && canPickTeam && canAcceptSlot(it.raw) ? (
                      (() => {
                        const divisionKey = (it.raw?.division || "").trim().toUpperCase();
                        const teamsForDivision = teamsByDivision.get(divisionKey) || [];
                        const selectedTeamId = acceptTeamBySlot[it.id] || "";
                        return (
                          <div className="row">
                            <select
                              value={selectedTeamId}
                              onChange={(e) => setAcceptTeam(it.id, e.target.value)}
                              title="Pick a team to accept this offer as."
                            >
                              <option value="">Select team</option>
                              {teamsForDivision.map((t) => (
                                <option key={t.teamId} value={t.teamId}>
                                  {t.name || t.teamId}
                                </option>
                              ))}
                            </select>
                            <button
                              className="btn btn--primary"
                              onClick={() => requestSlot(it.raw, selectedTeamId)}
                              disabled={!selectedTeamId}
                              title="Accept this offer on behalf of the selected team."
                            >
                              Accept as
                            </button>
                          </div>
                        );
                      })()
                    ) : null}
                    {it.kind === "slot" && !canPickTeam && role !== "Viewer" && canAcceptSlot(it.raw) && (it.raw?.offeringTeamId || "") !== myCoachTeamId ? (
                      <button className="btn btn--primary" onClick={() => requestSlot(it.raw)} title="Accept this open slot and confirm the game.">
                        Accept
                      </button>
                    ) : null}
                    {it.kind === "slot" && canCancelSlot(it.raw) && (it.raw?.status || "") !== "Cancelled" ? (
                      <button className="btn" onClick={() => cancelSlot(it.raw)} title="Cancel this game/slot.">
                        Cancel
                      </button>
                    ) : null}
                    {it.kind === "event" && canDeleteAnyEvent ? (
                      <button className="btn" onClick={() => deleteEvent(it.id)} title="Delete this event from the calendar.">
                        Delete
                      </button>
                    ) : null}
                  </div>
                </div>
              </div>
            ))}
          </div>
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
