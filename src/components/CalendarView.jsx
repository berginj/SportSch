import { useMemo, useState } from "react";
import "./CalendarView.css";

/**
 * Reusable calendar view component with two layout options:
 * - Option 1: Compact Week Cards (default for desktop)
 * - Option 4: Agenda List (mobile-friendly)
 */

export default function CalendarView({
  slots = [],
  events = [],
  defaultView = "week-cards",
  onSlotClick,
  onEventClick,
  renderSlotActions,
  renderEventActions,
  showViewToggle = true,
}) {
  const storageKey = "calendar-view-preference";
  const [viewMode, setViewMode] = useState(() => {
    if (!showViewToggle) return defaultView;
    try {
      return localStorage.getItem(storageKey) || defaultView;
    } catch {
      return defaultView;
    }
  });

  const handleViewChange = (mode) => {
    setViewMode(mode);
    try {
      localStorage.setItem(storageKey, mode);
    } catch {
      // Ignore localStorage errors
    }
  };

  const weekGroups = useMemo(() => {
    const groups = new Map();

    [...slots, ...events].forEach((item) => {
      const date = getItemDate(item);
      if (!date) return;

      const weekKey = getWeekKey(date);
      if (!groups.has(weekKey)) {
        groups.set(weekKey, {
          weekKey,
          weekStart: getWeekStart(date),
          weekEnd: getWeekEnd(date),
          days: new Map(),
          slots: [],
          events: [],
        });
      }

      const group = groups.get(weekKey);
      const dayKey = date;

      if (isSlotItem(item)) {
        group.slots.push(item);
        if (!group.days.has(dayKey)) {
          group.days.set(dayKey, { date: dayKey, slots: [], events: [] });
        }
        group.days.get(dayKey).slots.push(item);
        return;
      }

      group.events.push(item);
      if (!group.days.has(dayKey)) {
        group.days.set(dayKey, { date: dayKey, slots: [], events: [] });
      }
      group.days.get(dayKey).events.push(item);
    });

    return Array.from(groups.values()).sort((a, b) => a.weekKey.localeCompare(b.weekKey));
  }, [slots, events]);

  if (viewMode === "agenda") {
    return (
      <div className="calendar-view">
        {showViewToggle ? <ViewToggle currentView={viewMode} onChange={handleViewChange} /> : null}
        <AgendaView
          weekGroups={weekGroups}
          onSlotClick={onSlotClick}
          onEventClick={onEventClick}
          renderSlotActions={renderSlotActions}
          renderEventActions={renderEventActions}
        />
      </div>
    );
  }

  return (
    <div className="calendar-view">
      {showViewToggle ? <ViewToggle currentView={viewMode} onChange={handleViewChange} /> : null}
      <WeekCardsView
        weekGroups={weekGroups}
        onSlotClick={onSlotClick}
        onEventClick={onEventClick}
        renderSlotActions={renderSlotActions}
        renderEventActions={renderEventActions}
      />
    </div>
  );
}

function ViewToggle({ currentView, onChange }) {
  return (
    <div className="calendar-view-toggle">
      <button
        className={`btn btn--ghost ${currentView === "week-cards" ? "btn--active" : ""}`}
        type="button"
        onClick={() => onChange("week-cards")}
        title="Week card view (compact)"
      >
        Week Cards
      </button>
      <button
        className={`btn btn--ghost ${currentView === "agenda" ? "btn--active" : ""}`}
        type="button"
        onClick={() => onChange("agenda")}
        title="Agenda list view (chronological)"
      >
        Agenda
      </button>
    </div>
  );
}

function WeekCardsView({
  weekGroups,
  onSlotClick,
  onEventClick,
  renderSlotActions,
  renderEventActions,
}) {
  const [expandedWeeks, setExpandedWeeks] = useState(() => new Set());

  const toggleWeek = (weekKey) => {
    setExpandedWeeks((prev) => {
      const next = new Set(prev);
      if (next.has(weekKey)) {
        next.delete(weekKey);
      } else {
        next.add(weekKey);
      }
      return next;
    });
  };

  if (weekGroups.length === 0) {
    return <div className="subtle">No items in this range.</div>;
  }

  return (
    <div className="week-cards-view">
      {weekGroups.map((week) => {
        const isExpanded = expandedWeeks.has(week.weekKey);
        const totalGames = week.slots.filter((slot) => !slot.isAvailability).length;
        const totalOpen = week.slots.filter((slot) => slot.status === "Open").length;
        const totalEvents = week.events.length;
        const daySummaries = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map((dayName) => {
          const dayDates = Array.from(week.days.values()).filter((day) => getDayName(day.date) === dayName);

          if (dayDates.length === 0) {
            return { day: dayName, games: 0, open: 0 };
          }

          const games = dayDates.reduce(
            (sum, day) => sum + day.slots.filter((slot) => !slot.isAvailability && slot.status !== "Open").length,
            0
          );
          const open = dayDates.reduce(
            (sum, day) => sum + day.slots.filter((slot) => slot.status === "Open").length,
            0
          );

          return { day: dayName, games, open };
        });

        return (
          <div key={week.weekKey} className="week-card">
            <div className="week-card__header" onClick={() => toggleWeek(week.weekKey)}>
              <div className="week-card__title">
                <span className="week-card__dates">
                  {week.weekStart} - {week.weekEnd}
                </span>
                <span className="week-card__stats">
                  {totalGames} games | {totalOpen} open | {totalEvents} events
                </span>
              </div>
              <button className="week-card__toggle" type="button">
                {isExpanded ? "Hide" : "Details"}
              </button>
            </div>

            <div className="week-card__summary">
              <div className="week-day-grid">
                {daySummaries.map((day) => (
                  <div key={day.day} className="week-day-cell">
                    <div className="week-day-cell__label">{day.day}</div>
                    <div className="week-day-cell__count">{day.games > 0 ? `${day.games}g` : "-"}</div>
                    {day.open > 0 ? <div className="week-day-cell__open">{day.open}o</div> : null}
                  </div>
                ))}
              </div>
            </div>

            {isExpanded ? (
              <div className="week-card__details">
                {Array.from(week.days.values())
                  .sort((a, b) => a.date.localeCompare(b.date))
                  .map((day) => {
                    if (day.slots.length === 0 && day.events.length === 0) return null;

                    return (
                      <div key={day.date} className="day-detail">
                        <div className="day-detail__header">
                          {getDayName(day.date)}, {day.date}
                        </div>
                        <div className="day-detail__items">
                          {day.slots.map((slot, idx) => {
                            const tone = getSlotToneKey(slot);
                            const slotActions = renderSlotActions?.(slot);
                            const isInteractive = typeof onSlotClick === "function";
                            return (
                              <div
                                key={`slot-${slot.slotId || idx}`}
                                className={`calendar-item calendar-item--slot calendar-item--${tone}${isInteractive ? " calendar-item--interactive" : ""}`}
                                onClick={isInteractive ? () => onSlotClick(slot) : undefined}
                              >
                                <div className="calendar-item__time">{formatTimeRange(slot.startTime, slot.endTime)}</div>
                                <div className="calendar-item__field">
                                  <span>{getFieldLabel(slot) || "Field TBD"}</span>
                                  {slot.address ? (
                                    <a
                                      href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(slot.address)}`}
                                      target="_blank"
                                      rel="noopener noreferrer"
                                      className="field-directions-link"
                                      onClick={stopItemClickPropagation}
                                      title="Get directions to field"
                                    >
                                      Map
                                    </a>
                                  ) : null}
                                </div>
                                <div className="calendar-item__details">
                                  <div className="calendar-item__matchup">{getMatchupLabel(slot) || "Slot"}</div>
                                  <div className="calendar-item__meta">
                                    <span className={`calendar-item__status calendar-item__status--${tone}`}>
                                      {getSlotStatusLabel(slot)}
                                    </span>
                                    {slot.division ? (
                                      <span className="calendar-item__division">{slot.division}</span>
                                    ) : null}
                                  </div>
                                  {slot.confirmedTeamId ? (
                                    <div className="calendar-item__subtitle">Confirmed: {slot.confirmedTeamId}</div>
                                  ) : null}
                                  {slotActions ? (
                                    <div className="calendar-item__actions" onClick={stopItemClickPropagation}>
                                      {slotActions}
                                    </div>
                                  ) : null}
                                </div>
                              </div>
                            );
                          })}
                          {day.events.map((event, idx) => {
                            const eventActions = renderEventActions?.(event);
                            const subtitle = getEventSubtitle(event);
                            const notes = getEventNotes(event);
                            const isInteractive = typeof onEventClick === "function";
                            return (
                              <div
                                key={`event-${event.eventId || idx}`}
                                className={`calendar-item calendar-item--event${isInteractive ? " calendar-item--interactive" : ""}`}
                                onClick={isInteractive ? () => onEventClick(event) : undefined}
                              >
                                <div className="calendar-item__title">{getEventTitle(event)}</div>
                                {subtitle ? <div className="calendar-item__description">{subtitle}</div> : null}
                                {notes ? <div className="calendar-item__description">{notes}</div> : null}
                                {eventActions ? (
                                  <div className="calendar-item__actions" onClick={stopItemClickPropagation}>
                                    {eventActions}
                                  </div>
                                ) : null}
                              </div>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
              </div>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}

function AgendaView({
  weekGroups,
  onSlotClick,
  onEventClick,
  renderSlotActions,
  renderEventActions,
}) {
  if (weekGroups.length === 0) {
    return <div className="subtle">No items in this range.</div>;
  }

  const allDays = weekGroups
    .flatMap((week) => Array.from(week.days.values()))
    .sort((a, b) => a.date.localeCompare(b.date));

  return (
    <div className="agenda-view">
      {allDays.map((day) => {
        if (day.slots.length === 0 && day.events.length === 0) return null;

        return (
          <div key={day.date} className="agenda-day">
            <div className="agenda-day__header">
              <span className="agenda-day__name">{getDayName(day.date)}</span>
              <span className="agenda-day__date">{day.date}</span>
            </div>

            <div className="agenda-day__items">
              {groupByField(day.slots).map(({ fieldKey, fieldName, slots: fieldSlots }) => (
                <div key={fieldKey} className="agenda-field-group">
                  <div className="agenda-field-group__name">Field: {fieldName}</div>
                  {fieldSlots.map((slot, idx) => {
                    const tone = getSlotToneKey(slot);
                    const slotActions = renderSlotActions?.(slot);
                    const isInteractive = typeof onSlotClick === "function";
                    return (
                      <div
                        key={`slot-${slot.slotId || idx}`}
                        className={`agenda-item agenda-item--${tone}${isInteractive ? " agenda-item--interactive" : ""}`}
                        onClick={isInteractive ? () => onSlotClick(slot) : undefined}
                      >
                        <div className="agenda-item__time">{formatTimeRange(slot.startTime, slot.endTime)}</div>
                        <div className="agenda-item__body">
                          <div className="agenda-item__matchup">{getMatchupLabel(slot) || "Slot"}</div>
                          {slot.confirmedTeamId ? (
                            <div className="agenda-item__subtitle">Confirmed: {slot.confirmedTeamId}</div>
                          ) : null}
                          {slotActions ? (
                            <div className="agenda-item__actions" onClick={stopItemClickPropagation}>
                              {slotActions}
                            </div>
                          ) : null}
                        </div>
                        <div className="agenda-item__meta">
                          <span className={`agenda-item__status agenda-item__status--${tone}`}>
                            {getSlotStatusLabel(slot)}
                          </span>
                          {slot.division ? <span className="agenda-item__division">{slot.division}</span> : null}
                        </div>
                      </div>
                    );
                  })}
                </div>
              ))}

              {day.events.map((event, idx) => {
                const eventActions = renderEventActions?.(event);
                const subtitle = getEventSubtitle(event);
                const notes = getEventNotes(event);
                const isInteractive = typeof onEventClick === "function";
                return (
                  <div
                    key={`event-${event.eventId || idx}`}
                    className={`agenda-item agenda-item--event${isInteractive ? " agenda-item--interactive" : ""}`}
                    onClick={isInteractive ? () => onEventClick(event) : undefined}
                  >
                    <div className="agenda-item__title">{getEventTitle(event)}</div>
                    {subtitle ? <div className="agenda-item__description">{subtitle}</div> : null}
                    {notes ? <div className="agenda-item__description">{notes}</div> : null}
                    {eventActions ? (
                      <div className="agenda-item__actions" onClick={stopItemClickPropagation}>
                        {eventActions}
                      </div>
                    ) : null}
                  </div>
                );
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}

function stopItemClickPropagation(event) {
  event.stopPropagation();
}

function isSlotItem(item) {
  return !!(item?.slotId || item?.fieldKey);
}

function getItemDate(item) {
  return item?.gameDate || item?.eventDate || item?.date || "";
}

function getFieldLabel(slot) {
  return slot?.displayName || `${slot?.parkName || ""} ${slot?.fieldName || ""}`.trim() || slot?.fieldKey || "";
}

function formatTimeRange(startTime, endTime) {
  const start = String(startTime || "").trim();
  const end = String(endTime || "").trim();
  if (start && end) return `${start}-${end}`;
  return start || end || "Time TBD";
}

function formatOptionalTimeRange(startTime, endTime) {
  const start = String(startTime || "").trim();
  const end = String(endTime || "").trim();
  if (start && end) return `${start}-${end}`;
  return start || end || "";
}

function getEventTitle(event) {
  const type = String(event?.type || "").trim();
  const title = String(event?.title || "").trim();
  if (type && title && title.toLowerCase().startsWith(`${type.toLowerCase()}:`)) return title;
  if (type && title) return `${type}: ${title}`;
  return title || type || "Event";
}

function getEventSubtitle(event) {
  return [
    formatOptionalTimeRange(event?.startTime, event?.endTime),
    event?.status ? `Status: ${event.status}` : "",
    event?.opponentTeamId ? `Opponent: ${event.opponentTeamId}` : "",
    event?.location ? `Location: ${event.location}` : "",
    event?.division ? `Division: ${event.division}` : "",
    event?.teamId ? `Team: ${event.teamId}` : "",
  ]
    .filter(Boolean)
    .join(" | ");
}

function getEventNotes(event) {
  return String(event?.notes || event?.description || "").trim();
}

function parseLocalIsoDate(dateStr) {
  const parts = String(dateStr || "").split("-");
  if (parts.length !== 3) return null;
  const year = Number(parts[0]);
  const month = Number(parts[1]) - 1;
  const day = Number(parts[2]);
  if (!Number.isInteger(year) || !Number.isInteger(month) || !Number.isInteger(day)) return null;
  const date = new Date(year, month, day);
  return Number.isNaN(date.getTime()) ? null : date;
}

function formatLocalIsoDate(date) {
  if (!(date instanceof Date) || Number.isNaN(date.getTime())) return "";
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function getWeekKey(dateStr) {
  const date = parseLocalIsoDate(dateStr) || new Date(dateStr);
  const year = date.getFullYear();
  const week = getWeekNumber(date);
  return `${year}-W${String(week).padStart(2, "0")}`;
}

function getWeekNumber(date) {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
  const dayNum = d.getUTCDay() || 7;
  d.setUTCDate(d.getUTCDate() + 4 - dayNum);
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
  return Math.ceil((((d - yearStart) / 86400000) + 1) / 7);
}

function getWeekStart(dateStr) {
  const date = parseLocalIsoDate(dateStr);
  if (!date) return "";
  const day = date.getDay();
  const diff = date.getDate() - day + (day === 0 ? -6 : 1);
  const monday = new Date(date.setDate(diff));
  return formatLocalIsoDate(monday);
}

function getWeekEnd(dateStr) {
  const date = parseLocalIsoDate(getWeekStart(dateStr));
  if (!date) return "";
  date.setDate(date.getDate() + 6);
  return formatLocalIsoDate(date);
}

function getDayName(dateStr) {
  const days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
  const localDate = parseLocalIsoDate(dateStr);
  if (localDate) return days[localDate.getDay()];
  const date = new Date(dateStr);
  return days[date.getDay()];
}

function getMatchupLabel(slot) {
  if (slot.gameType === "practice" || slot.gameType === "Practice") {
    const team = slot.confirmedTeamId || slot.offeringTeamId || "";
    return team ? `Practice: ${team}` : "Practice";
  }
  const home = slot.homeTeamId || slot.offeringTeamId || "";
  const away = slot.awayTeamId || "";
  if (away) return `${home} vs ${away}`;
  if (home && slot.isExternalOffer) return `${home} vs TBD (external)`;
  if (home) return `${home} vs TBD`;
  if (slot.status === "Open") return "Open Slot";
  return "";
}

function getSlotToneKey(slot) {
  if (slot.status === "Cancelled") return "cancelled";
  if (slot.isAvailability) return "availability";
  if (slot.status === "Confirmed") return "confirmed";
  if (slot.status === "Pending") return "pending";
  if (slot.status === "Open") return "open";
  return "scheduled";
}

function getSlotStatusLabel(slot) {
  if (slot.isAvailability) return "Availability";
  return slot.status || "Scheduled";
}

function groupByField(slots) {
  const grouped = new Map();

  slots.forEach((slot) => {
    const fieldKey = slot.fieldKey || "unknown";
    const fieldName = getFieldLabel(slot) || fieldKey;

    if (!grouped.has(fieldKey)) {
      grouped.set(fieldKey, { fieldKey, fieldName, slots: [] });
    }

    grouped.get(fieldKey).slots.push(slot);
  });

  return Array.from(grouped.values()).sort((a, b) => a.fieldName.localeCompare(b.fieldName));
}
