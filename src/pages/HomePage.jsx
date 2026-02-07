import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import LeaguePicker from "../components/LeaguePicker";
import StatusCard from "../components/StatusCard";
import CoachDashboard from "./CoachDashboard";
import { SLOT_STATUS } from "../lib/constants";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";

function toDateInputValue(d) {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function isPracticeSlot(slot) {
  return (slot?.gameType || "").trim().toLowerCase() === "practice";
}

function formatTimeRange(startTime, endTime) {
  const start = (startTime || "").trim();
  const end = (endTime || "").trim();
  if (!start && !end) return "";
  if (start && end) return `${start}-${end}`;
  return start || end;
}

function useIsMobile() {
  const [isMobile, setIsMobile] = useState(false);
  useEffect(() => {
    const mq = window.matchMedia("(max-width: 720px)");
    const update = () => setIsMobile(mq.matches);
    update();
    if (mq.addEventListener) mq.addEventListener("change", update);
    else mq.addListener(update);
    return () => {
      if (mq.removeEventListener) mq.removeEventListener("change", update);
      else mq.removeListener(update);
    };
  }, []);
  return isMobile;
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
      [SLOT_STATUS.PENDING]: true,
      [SLOT_STATUS.CONFIRMED]: true,
      [SLOT_STATUS.CANCELLED]: false,
    };
  }
  const set = new Set(raw.split(",").map((s) => s.trim()).filter(Boolean));
  return {
    [SLOT_STATUS.OPEN]: set.has(SLOT_STATUS.OPEN),
    [SLOT_STATUS.PENDING]: set.has(SLOT_STATUS.PENDING),
    [SLOT_STATUS.CONFIRMED]: set.has(SLOT_STATUS.CONFIRMED),
    [SLOT_STATUS.CANCELLED]: set.has(SLOT_STATUS.CANCELLED),
  };
}

export default function HomePage({ me, leagueId, setLeagueId, setTab }) {
  const isMobile = useIsMobile();
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const role = useMemo(() => {
    const inLeague = memberships.filter((m) => (m?.leagueId || "").trim() === (leagueId || "").trim());
    const roles = inLeague.map((m) => normalizeRole(m?.role));
    if (roles.includes("LeagueAdmin")) return "LeagueAdmin";
    if (roles.includes("Coach")) return "Coach";
    return roles.includes("Viewer") ? "Viewer" : "";
  }, [memberships, leagueId]);
  const isAdmin = !!me?.isGlobalAdmin || role === "LeagueAdmin";

  const today = useMemo(() => new Date(), []);

  const [division, setDivision] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [showSlots, setShowSlots] = useState(true);
  const [showEvents, setShowEvents] = useState(true);
  const [slotStatusFilter, setSlotStatusFilter] = useState({
    [SLOT_STATUS.OPEN]: true,
    [SLOT_STATUS.PENDING]: true,
    [SLOT_STATUS.CONFIRMED]: true,
    [SLOT_STATUS.CANCELLED]: false,
  });

  const [divisions, setDivisions] = useState([]);
  const [slots, setSlots] = useState([]);
  const [events, setEvents] = useState([]);
  const [accessRequests, setAccessRequests] = useState([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const initializedRef = useRef(false);
  const defaultsRef = useRef(getDefaultRangeFallback());

  const applyFiltersFromUrl = useCallback((defaults) => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    setDivision((params.get("division") || "").trim());
    setDateFrom((params.get("dateFrom") || "").trim() || defaults.from);
    setDateTo((params.get("dateTo") || "").trim() || defaults.to);
    setShowSlots(parseBoolParam(params, "showSlots", true));
    setShowEvents(parseBoolParam(params, "showEvents", true));
    setSlotStatusFilter(parseStatusFilter(params));
  }, []);

  useEffect(() => {
    if (!leagueId) return;
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
      initializedRef.current = true;
    })();
  }, [leagueId, applyFiltersFromUrl]);

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

    const activeStatuses = [
      SLOT_STATUS.OPEN,
      SLOT_STATUS.PENDING,
      SLOT_STATUS.CONFIRMED,
      SLOT_STATUS.CANCELLED,
    ].filter((s) => slotStatusFilter[s]);
    if (activeStatuses.length) params.set("status", activeStatuses.join(","));
    else params.delete("status");

    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [division, dateFrom, dateTo, showSlots, showEvents, slotStatusFilter]);

  async function load() {
    if (!leagueId) return;
    setErr("");
    setLoading(true);
    try {
      const baseQuery = new URLSearchParams();
      if (division) baseQuery.set("division", division);
      if (dateFrom) baseQuery.set("dateFrom", dateFrom);
      if (dateTo) baseQuery.set("dateTo", dateTo);

      const activeStatuses = Object.entries(slotStatusFilter)
        .filter(([, on]) => on)
        .map(([k]) => k);
      const slotsQuery = new URLSearchParams(baseQuery);
      if (activeStatuses.length) slotsQuery.set("status", activeStatuses.join(","));

      const reqs = [];
      reqs.push(apiFetch("/api/divisions"));
      reqs.push(showSlots && activeStatuses.length ? apiFetch(`/api/slots?${slotsQuery.toString()}`) : Promise.resolve([]));
      reqs.push(showEvents ? apiFetch(`/api/events?${baseQuery.toString()}`) : Promise.resolve([]));
      if (isAdmin) reqs.push(apiFetch("/api/accessrequests?status=Pending"));
      const [divs, slotList, eventList, accessList] = await Promise.all(reqs);

      setDivisions(Array.isArray(divs) ? divs : []);
      setSlots(Array.isArray(slotList) ? slotList : []);
      setEvents(Array.isArray(eventList) ? eventList : []);
      setAccessRequests(Array.isArray(accessList) ? accessList : []);
    } catch (e) {
      setErr(e?.message || "Failed to load.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  const openSlots = useMemo(
    () => slots.filter((s) => s.status === SLOT_STATUS.OPEN && !s.isAvailability && !isPracticeSlot(s) && (!s.awayTeamId || s.isExternalOffer)),
    [slots]
  );
  const confirmedSlots = useMemo(
    () => slots.filter((s) => s.status === SLOT_STATUS.CONFIRMED && !s.isAvailability && !isPracticeSlot(s)),
    [slots]
  );
  const practiceSlots = useMemo(
    () => slots.filter((s) => s.status === SLOT_STATUS.CONFIRMED && !s.isAvailability && isPracticeSlot(s)),
    [slots]
  );

  function goToCalendarWithStatus(status) {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (division) params.set("division", division);
    else params.delete("division");
    if (dateFrom) params.set("dateFrom", dateFrom);
    else params.delete("dateFrom");
    if (dateTo) params.set("dateTo", dateTo);
    else params.delete("dateTo");
    params.set("showSlots", "1");
    if (showEvents) params.set("showEvents", "1");
    else params.delete("showEvents");
    params.set("status", status);
    const next = `${window.location.pathname}?${params.toString()}#calendar`;
    window.history.replaceState({}, "", next);
    setTab("calendar");
  }

  function openAccessRequests() {
    setTab("admin");
    if (typeof window !== "undefined") {
      window.location.hash = "#admin";
    }
  }

  const nextItems = useMemo(() => {
    const todayKey = toDateInputValue(today);
    const windowEnd = new Date(today);
    windowEnd.setDate(windowEnd.getDate() + 30);
    const windowEndKey = toDateInputValue(windowEnd);

    const items = [
      ...events.map((e) => ({
        kind: "event",
        date: e.eventDate,
        label: `${e.type ? `${e.type}: ` : ""}${e.title}`,
      })),
      ...confirmedSlots.map((s) => ({
        kind: "slot",
        date: s.gameDate,
        label: `${s.offeringTeamId || ""} @ ${s.displayName || s.fieldKey || ""}`,
      })),
      ...practiceSlots.map((s) => ({
        kind: "practice",
        date: s.gameDate,
        label: `Practice: ${(s.confirmedTeamId || s.offeringTeamId || "").trim()} @ ${s.displayName || s.fieldKey || ""}`,
      })),
    ];
    return items
      .filter((i) => i.date && i.date >= todayKey && i.date <= windowEndKey)
      .sort((a, b) => a.date.localeCompare(b.date))
      .slice(0, 5);
  }, [events, confirmedSlots, practiceSlots, today]);

  const nextAvailableOffers = useMemo(() => {
    const todayKey = toDateInputValue(today);
    return openSlots
      .filter((s) => s.gameDate && s.gameDate >= todayKey)
      .map((s) => ({
        date: s.gameDate,
        label: `${s.offeringTeamId || ""} @ ${s.displayName || s.fieldKey || ""}`,
        time: formatTimeRange(s.startTime, s.endTime),
      }))
      .sort((a, b) => a.date.localeCompare(b.date))
      .slice(0, 5);
  }, [openSlots, today]);

  const layoutKey = isMobile ? "mobile" : isAdmin ? "admin" : role === "Coach" ? "filters" : "coach";

  function renderFilters() {
    return (
      <div className="layoutPanel">
        <div className="layoutPanel__title">Filters</div>
        <div className="layoutForm">
          <label>
            League
            <LeaguePicker leagueId={leagueId} setLeagueId={setLeagueId} me={me} label="League" />
          </label>
          <label>
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
          <label>
            From
            <input value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} />
          </label>
          <label>
            To
            <input value={dateTo} onChange={(e) => setDateTo(e.target.value)} />
          </label>
          <label>
            Status
            <div className="layoutRow">
              {[SLOT_STATUS.OPEN, SLOT_STATUS.PENDING, SLOT_STATUS.CONFIRMED, SLOT_STATUS.CANCELLED].map((status) => (
                <button
                  key={status}
                  className={`btn btn--ghost ${slotStatusFilter[status] ? "is-active" : ""}`}
                  onClick={() =>
                    setSlotStatusFilter((prev) => ({ ...prev, [status]: !prev[status] }))
                  }
                >
                  {status}
                </button>
              ))}
            </div>
          </label>
          <div className="layoutRow">
            <label className="inlineCheck">
              <input type="checkbox" checked={showSlots} onChange={(e) => setShowSlots(e.target.checked)} />
              Slots
            </label>
            <label className="inlineCheck">
              <input type="checkbox" checked={showEvents} onChange={(e) => setShowEvents(e.target.checked)} />
              Events
            </label>
          </div>
          <button className="btn" onClick={load}>
            Apply
          </button>
        </div>
      </div>
    );
  }

  function renderFiltersResults() {
    return (
      <div className="layoutPanel">
        <div className="layoutPanel__title">Results</div>
        <div className="layoutList">
          {openSlots.slice(0, 6).map((s) => (
            <div className="layoutItem" key={s.slotId}>
              <div className="layoutRow layoutRow--space">
                <div>{s.gameDate} - {s.offeringTeamId}</div>
                <div className="layoutBadge">Open</div>
              </div>
              <div className="layoutMeta">{s.displayName || s.fieldKey}</div>
            </div>
          ))}
          {openSlots.length === 0 ? <div className="layoutMeta">No open offers.</div> : null}
        </div>
      </div>
    );
  }

  function renderAdmin() {
    return (
      <div className="layoutPreview layoutPreview--admin">
        <div className="layoutHeader">
          <div>
            <div className="layoutTitle">League admin desk</div>
            <div className="layoutMeta">Power tasks for {leagueId || "your league"}</div>
          </div>
          <div className="layoutRow">
            <button className="btn" onClick={() => setTab("manage")}>League Management</button>
            <button className="btn" onClick={() => setTab("offers")}>Create offer/request</button>
            <button className="btn" onClick={() => setTab("admin")}>Access requests</button>
          </div>
        </div>
        <div className="layoutGrid">
          <div className="layoutPanel">
            <div className="layoutPanel__title">Today</div>
            <div className="layoutStatRow">
              <button className="layoutStat layoutStat--link" type="button" onClick={() => goToCalendarWithStatus(SLOT_STATUS.OPEN)}>
                <div className="layoutStat__value">{openSlots.length}</div>
                <div className="layoutStat__label">Open offers</div>
              </button>
              <button className="layoutStat layoutStat--link" type="button" onClick={() => goToCalendarWithStatus(SLOT_STATUS.CONFIRMED)}>
                <div className="layoutStat__value">{confirmedSlots.length}</div>
                <div className="layoutStat__label">Confirmed</div>
              </button>
              <button className="layoutStat layoutStat--link" type="button" onClick={openAccessRequests}>
                <div className="layoutStat__value">{accessRequests.length}</div>
                <div className="layoutStat__label">Access requests</div>
              </button>
            </div>
          </div>
          <div className="layoutPanel">
            <div className="layoutPanel__title">Next 30 days</div>
            <div className="layoutList">
              {nextItems.map((i, idx) => (
                <div className="layoutItem" key={idx}>
                  {i.date} - {i.label}
                </div>
              ))}
              {nextItems.length === 0 && nextAvailableOffers.length > 0 ? (
                <>
                  <div className="layoutMeta">Next available offers</div>
                  {nextAvailableOffers.map((i, idx) => (
                    <div className="layoutItem" key={`offer-${idx}`}>
                      {i.date} - {i.label}{i.time ? ` (${i.time})` : ""}
                    </div>
                  ))}
                </>
              ) : null}
              {nextItems.length === 0 && nextAvailableOffers.length === 0 ? (
                <div className="layoutMeta">No upcoming items.</div>
              ) : null}
            </div>
          </div>
          <div className="layoutPanel">
            <div className="layoutPanel__title">Quick filters</div>
            <div className="layoutRow">
              {divisions.slice(0, 4).map((d) => (
                <div key={d.code} className="layoutPill">{d.code}</div>
              ))}
              <button className="layoutPill layoutPill--link" type="button" onClick={() => goToCalendarWithStatus(SLOT_STATUS.OPEN)}>Open</button>
              <button className="layoutPill layoutPill--link" type="button" onClick={() => goToCalendarWithStatus(SLOT_STATUS.PENDING)}>Pending</button>
              <button className="layoutPill layoutPill--link" type="button" onClick={() => goToCalendarWithStatus(SLOT_STATUS.CONFIRMED)}>Confirmed</button>
            </div>
          </div>
          <div className="layoutPanel">
            <div className="layoutPanel__title">Shortcuts</div>
            <div className="layoutList">
              <button className="layoutItem layoutItem--link" onClick={() => setTab("manage")} type="button">
                Teams and coaches
              </button>
              <button className="layoutItem layoutItem--link" onClick={() => setTab("manage")} type="button">
                Invites
              </button>
              <button className="layoutItem layoutItem--link" onClick={() => setTab("calendar")} type="button">
                Calendar view
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  function renderCoachHub() {
    return (
      <div className="layoutPreview layoutPreview--coach">
        <div className="layoutHero">
          <div>
            <div className="layoutTitle">Coach hub</div>
            <div className="layoutMeta">Find open offers, accept, and confirm quickly.</div>
          </div>
          <div className="layoutRow">
            <button className="btn" onClick={() => setTab("offers")}>Create offer/request</button>
            <button className="btn btn--ghost" onClick={() => setTab("calendar")}>Calendar</button>
          </div>
        </div>
        <div className="layoutGrid layoutGrid--two">
          <div className="layoutPanel">
            <div className="layoutPanel__title">Open offers</div>
            <div className="layoutList">
              {openSlots.slice(0, 4).map((s) => (
                <div className="layoutItem" key={s.slotId}>
                  <div className="layoutRow layoutRow--space">
                    <div>{s.gameDate} - {s.offeringTeamId}</div>
                    <div className="layoutBadge">Open</div>
                  </div>
                  <div className="layoutMeta">{s.displayName || s.fieldKey}</div>
                </div>
              ))}
              {openSlots.length === 0 ? <div className="layoutMeta">No open offers.</div> : null}
            </div>
          </div>
          <div className="layoutPanel">
            <div className="layoutPanel__title">My calendar</div>
            <div className="layoutList">
              {nextItems.map((i, idx) => (
                <div className="layoutItem" key={idx}>
                  {i.date} - {i.label}
                </div>
              ))}
              {nextItems.length === 0 ? <div className="layoutMeta">No upcoming items.</div> : null}
            </div>
          </div>
        </div>
      </div>
    );
  }

  function renderMobile() {
    return (
      <div className="layoutPreview layoutPreview--mobile">
        <div className="layoutPhone">
          <div className="layoutPhone__bar">Sports Scheduler</div>
          <div className="layoutPhone__section">
            <div className="layoutTitle">Today</div>
            <div className="layoutPill">Open offers</div>
            <div className="layoutList">
              {openSlots.slice(0, 2).map((s) => (
                <div className="layoutItem" key={s.slotId}>
                  <div className="layoutRow">
                    <div>{s.offeringTeamId} @ {s.displayName || s.fieldKey}</div>
                    <div className="layoutBadge">Open</div>
                  </div>
                  <div className="layoutMeta">{s.gameDate} {s.startTime}-{s.endTime}</div>
                  <button className="btn">Accept</button>
                </div>
              ))}
              {openSlots.length === 0 ? <div className="layoutMeta">No open offers.</div> : null}
            </div>
          </div>
          <div className="layoutPhone__nav">
            <button className="btn btn--ghost" onClick={() => setTab("calendar")}>Calendar</button>
            <button className="btn btn--ghost" onClick={() => setTab("offers")}>Offer/Request</button>
            <button className="btn btn--ghost" onClick={() => setTab("manage")}>Teams</button>
            <button className="btn btn--ghost" onClick={() => setTab("help")}>Help</button>
          </div>
        </div>
      </div>
    );
  }

  if (loading) {
    return <StatusCard title="Loading" message="Loading dashboard..." />;
  }

  if (err) {
    return <StatusCard tone="error" title="Unable to load dashboard" message={err} />;
  }

  // Show new CoachDashboard for coaches (desktop only, mobile keeps existing layout)
  if (role === "Coach" && !isMobile) {
    return <CoachDashboard me={me} leagueId={leagueId} setTab={setTab} />;
  }

  if (layoutKey === "mobile") return renderMobile();
  if (layoutKey === "admin") return renderAdmin();
  if (layoutKey === "filters") {
    return (
      <div className="layoutPreview layoutPreview--filters">
        <div className="layoutSplit">
          {renderFilters()}
          {renderFiltersResults()}
        </div>
      </div>
    );
  }
  return renderCoachHub();
}
