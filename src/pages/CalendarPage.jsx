import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { SLOT_STATUS } from "../lib/constants";
import LeaguePicker from "../components/LeaguePicker";

function toDateInputValue(d) {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function normalizeRole(role) {
  return (role || "").trim();
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

  const today = useMemo(() => new Date(), []);
  const seasonEnd = useMemo(() => new Date(today.getFullYear(), 6, 30), [today]);
  const [dateFrom, setDateFrom] = useState(toDateInputValue(today));
  const [dateTo, setDateTo] = useState(toDateInputValue(seasonEnd));
  const [showSlots, setShowSlots] = useState(true);
  const [showEvents, setShowEvents] = useState(true);
  const [slotStatusFilter, setSlotStatusFilter] = useState({
    [SLOT_STATUS.OPEN]: true,
    [SLOT_STATUS.CONFIRMED]: true,
    [SLOT_STATUS.CANCELLED]: false,
  });

  const [events, setEvents] = useState([]);
  const [slots, setSlots] = useState([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");

  async function loadMeta() {
    const divs = await apiFetch("/api/divisions");
    setDivisions(Array.isArray(divs) ? divs : []);
  }

  async function loadData() {
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
      const shouldLoadSlots = showSlots && activeStatuses.length > 0;

      const slotsQuery = new URLSearchParams(baseQuery);
      if (activeStatuses.length) slotsQuery.set("status", activeStatuses.join(","));

      const [ev, sl] = await Promise.all([
        showEvents ? apiFetch(`/api/events?${baseQuery.toString()}`) : Promise.resolve([]),
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
      try {
        await loadMeta();
      } catch {
        // ignore
      }
      await loadData();
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

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
      const label = `${s.offeringTeamId || ""} @ ${s.displayName || `${s.parkName || ""} ${s.fieldName || ""}`}`.trim();
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
  }, [events, slots]);

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
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  async function deleteEvent(eventId) {
    if (!eventId) return;
    const ok = confirm("Delete this event?");
    if (!ok) return;
    setErr("");
    try {
      await apiFetch(`/api/events/${encodeURIComponent(eventId)}`, { method: "DELETE" });
      await loadData();
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  async function requestSlot(slot) {
    if (!slot?.slotId || !slot?.division) return;
    const notes = prompt("Optional notes for the offering coach:") || "";
    setErr("");
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(slot.division)}/${encodeURIComponent(slot.slotId)}/requests`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ notes: String(notes || "").trim() }),
      });
      await loadData();
      alert("Accepted. The game is now scheduled on the calendar.");
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  async function cancelSlot(slot) {
    if (!slot?.slotId || !slot?.division) return;
    const ok = confirm("Cancel this game/slot?");
    if (!ok) return;
    setErr("");
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(slot.division)}/${encodeURIComponent(slot.slotId)}/cancel`, {
        method: "PATCH",
      });
      await loadData();
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
      alert("Subscribe link copied.");
    } catch {
      prompt("Copy the subscribe link:", subscribeInfo.url);
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

  if (loading) return <div className="card">Loading...</div>;

  return (
    <div className="stack">
      {err ? <div className="card error">{err}</div> : null}

      <div className="card">
        <div className="cardTitle">
          Calendar filters
          <span className="hint" title="Filter what appears on the calendar and subscription link.">?</span>
        </div>
        <div className="row" style={{ flexWrap: "wrap", alignItems: "center" }}>
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
          <label style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 18 }} title="Show or hide slot offers.">
            <input type="checkbox" checked={showSlots} onChange={(e) => setShowSlots(e.target.checked)} />
            Slots
          </label>
          <label style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 18 }} title="Show or hide league events.">
            <input type="checkbox" checked={showEvents} onChange={(e) => setShowEvents(e.target.checked)} />
            Events
          </label>
          <button className="btn" onClick={loadData} title="Refresh the calendar list with the current filters.">
            Refresh
          </button>
        </div>
        <div className="row" style={{ marginTop: 10 }}>
          <div className="pill">Slot status</div>
          {[SLOT_STATUS.OPEN, SLOT_STATUS.CONFIRMED, SLOT_STATUS.CANCELLED].map((status) => (
            <label key={status} className="pill" style={{ cursor: "pointer" }} title={`Show ${status.toLowerCase()} offers on the calendar.`}>
              <input
                type="checkbox"
                checked={!!slotStatusFilter[status]}
                onChange={() => toggleSlotStatus(status)}
                style={{ marginRight: 6 }}
                disabled={!showSlots}
              />
              {status}
            </label>
          ))}
          {showSlots && activeSlotStatuses.length === 0 ? (
            <div className="muted">Select at least one status to show slots.</div>
          ) : null}
        </div>
        <div className="row" style={{ marginTop: 10, justifyContent: "space-between" }}>
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
        <div className="muted" style={{ marginTop: 6 }}>
          Subscribe link reflects the current filters and date range.
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">Calendar</div>
        {timeline.length === 0 ? <div className="muted">No items in this range.</div> : null}
        <div className="stack">
          {timeline.map((it) => (
            <div key={`${it.kind}:${it.id}`} className={statusClassForItem(it)}>
              <div className="row" style={{ justifyContent: "space-between" }}>
                <div>
                  <div style={{ fontWeight: 700 }}>
                    {it.date} {it.start ? `${it.start}${it.end ? `-${it.end}` : ""}` : ""} - {it.title}
                  </div>
                  {it.subtitle ? <div className="muted">{it.subtitle}</div> : null}
                  {it.kind === "event" && it.raw?.notes ? <div style={{ marginTop: 6 }}>{it.raw.notes}</div> : null}
                </div>
                <div className="row">
                  <span className={`statusBadge status-${(statusLabelForItem(it) || "").toLowerCase()}`}>
                    {statusLabelForItem(it)}
                  </span>
                  {it.kind === "slot" && role !== "Viewer" && (it.raw?.status || "") === "Open" && (it.raw?.offeringTeamId || "") !== myCoachTeamId ? (
                    <button className="btn primary" onClick={() => requestSlot(it.raw)} title="Accept this open offer and confirm the game.">
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

      {canCreateEvents ? (
        <div className="card">
          <details>
            <summary style={{ cursor: "pointer", fontWeight: 700 }}>
              {role === "Coach" ? "Request a game" : "Add an event"}
            </summary>
            <div className="grid2" style={{ marginTop: 12 }}>
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
            <div className="row" style={{ marginTop: 10 }}>
              <button className="btn primary" onClick={createEvent}>
                Create Event
              </button>
            </div>
          </details>
        </div>
      ) : null}
    </div>
  );
}
