import { useState, useMemo } from "react";
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
  showViewToggle = true
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

  // Group slots and events by week
  const weekGroups = useMemo(() => {
    const groups = new Map();

    [...slots, ...events].forEach((item) => {
      const date = item.gameDate || item.date || "";
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

      if (item.slotId || item.fieldKey) {
        // It's a slot
        group.slots.push(item);
        if (!group.days.has(dayKey)) {
          group.days.set(dayKey, { date: dayKey, slots: [], events: [] });
        }
        group.days.get(dayKey).slots.push(item);
      } else {
        // It's an event
        group.events.push(item);
        if (!group.days.has(dayKey)) {
          group.days.set(dayKey, { date: dayKey, slots: [], events: [] });
        }
        group.days.get(dayKey).events.push(item);
      }
    });

    return Array.from(groups.values()).sort((a, b) => a.weekKey.localeCompare(b.weekKey));
  }, [slots, events]);

  if (viewMode === "agenda") {
    return (
      <div className="calendar-view">
        {showViewToggle && (
          <ViewToggle currentView={viewMode} onChange={handleViewChange} />
        )}
        <AgendaView
          weekGroups={weekGroups}
          onSlotClick={onSlotClick}
          onEventClick={onEventClick}
        />
      </div>
    );
  }

  return (
    <div className="calendar-view">
      {showViewToggle && (
        <ViewToggle currentView={viewMode} onChange={handleViewChange} />
      )}
      <WeekCardsView
        weekGroups={weekGroups}
        onSlotClick={onSlotClick}
        onEventClick={onEventClick}
      />
    </div>
  );
}

function ViewToggle({ currentView, onChange }) {
  return (
    <div className="calendar-view-toggle">
      <button
        className={`btn btn--ghost ${currentView === "week-cards" ? "btn--active" : ""}`}
        onClick={() => onChange("week-cards")}
        title="Week card view (compact)"
      >
        Week Cards
      </button>
      <button
        className={`btn btn--ghost ${currentView === "agenda" ? "btn--active" : ""}`}
        onClick={() => onChange("agenda")}
        title="Agenda list view (chronological)"
      >
        Agenda
      </button>
    </div>
  );
}

function WeekCardsView({ weekGroups, onSlotClick, onEventClick }) {
  const [expandedWeeks, setExpandedWeeks] = useState(new Set());

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
        const totalGames = week.slots.filter(s => !s.isAvailability).length;
        const totalOpen = week.slots.filter(s => s.status === "Open").length;
        const totalEvents = week.events.length;
        // Calculate day summaries
        const daySummaries = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map((dayName) => {
          const dayDates = Array.from(week.days.values()).filter((d) => {
            const dayOfWeek = getDayName(d.date);
            return dayOfWeek === dayName;
          });

          if (dayDates.length === 0) return { day: dayName, games: 0, open: 0 };

          const games = dayDates.reduce((sum, d) => sum + d.slots.filter(s => !s.isAvailability && s.status !== "Open").length, 0);
          const open = dayDates.reduce((sum, d) => sum + d.slots.filter(s => s.status === "Open").length, 0);

          return { day: dayName, games, open };
        });

        return (
          <div key={week.weekKey} className="week-card">
            <div className="week-card__header" onClick={() => toggleWeek(week.weekKey)}>
              <div className="week-card__title">
                <span className="week-card__dates">{week.weekStart} - {week.weekEnd}</span>
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
                    <div className="week-day-cell__count">
                      {day.games > 0 ? `${day.games}g` : "-"}
                    </div>
                    {day.open > 0 && (
                      <div className="week-day-cell__open">{day.open}o</div>
                    )}
                  </div>
                ))}
              </div>
            </div>

            {isExpanded && (
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
                            return (
                              <div
                                key={`slot-${idx}`}
                                className={`calendar-item calendar-item--slot calendar-item--${tone}`}
                                onClick={() => onSlotClick?.(slot)}
                              >
                                <div className="calendar-item__time">
                                  {slot.startTime}-{slot.endTime}
                                </div>
                                <div className="calendar-item__field">
                                  {slot.displayName || `${slot.parkName || ""} ${slot.fieldName || ""}`.trim()}
                                </div>
                                <div className="calendar-item__details">
                                  <div className="calendar-item__matchup">
                                    {getMatchupLabel(slot)}
                                  </div>
                                  <div className="calendar-item__meta">
                                    <span className={`calendar-item__status calendar-item__status--${tone}`}>
                                      {getSlotStatusLabel(slot)}
                                    </span>
                                    {slot.division ? (
                                      <span className="calendar-item__division">{slot.division}</span>
                                    ) : null}
                                  </div>
                                </div>
                              </div>
                            );
                          })}
                          {day.events.map((event, idx) => (
                            <div
                              key={`event-${idx}`}
                              className="calendar-item calendar-item--event"
                              onClick={() => onEventClick?.(event)}
                            >
                              <div className="calendar-item__title">Event: {event.title || "Event"}</div>
                              {event.description && (
                                <div className="calendar-item__description">{event.description}</div>
                              )}
                            </div>
                          ))}
                        </div>
                      </div>
                    );
                  })}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

function AgendaView({ weekGroups, onSlotClick, onEventClick }) {
  if (weekGroups.length === 0) {
    return <div className="subtle">No items in this range.</div>;
  }

  // Flatten all days from all weeks into chronological list
  const allDays = weekGroups.flatMap((week) =>
    Array.from(week.days.values())
  ).sort((a, b) => a.date.localeCompare(b.date));

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
              {/* Group by field for better organization */}
              {groupByField(day.slots).map(({ fieldKey, fieldName, slots }) => (
                <div key={fieldKey} className="agenda-field-group">
                  <div className="agenda-field-group__name">
                    Field: {fieldName}
                  </div>
                  {slots.map((slot, idx) => {
                    const tone = getSlotToneKey(slot);
                    return (
                      <div
                        key={`slot-${idx}`}
                        className={`agenda-item agenda-item--${tone}`}
                        onClick={() => onSlotClick?.(slot)}
                      >
                        <div className="agenda-item__time">
                          {slot.startTime}-{slot.endTime}
                        </div>
                        <div className="agenda-item__matchup">
                          {getMatchupLabel(slot)}
                        </div>
                        <div className="agenda-item__meta">
                          <span className={`agenda-item__status agenda-item__status--${tone}`}>
                            {getSlotStatusLabel(slot)}
                          </span>
                          {slot.division ? (
                            <span className="agenda-item__division">{slot.division}</span>
                          ) : null}
                        </div>
                      </div>
                    );
                  })}
                </div>
              ))}

              {day.events.map((event, idx) => (
                <div
                  key={`event-${idx}`}
                  className="agenda-item agenda-item--event"
                  onClick={() => onEventClick?.(event)}
                >
                  <div className="agenda-item__title">Event: {event.title || "Event"}</div>
                  {event.description && (
                    <div className="agenda-item__description">{event.description}</div>
                  )}
                </div>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

// Helper functions
function getWeekKey(dateStr) {
  const date = new Date(dateStr);
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
  const date = new Date(dateStr);
  const day = date.getDay();
  const diff = date.getDate() - day + (day === 0 ? -6 : 1); // Adjust to Monday
  const monday = new Date(date.setDate(diff));
  return monday.toISOString().split("T")[0];
}

function getWeekEnd(dateStr) {
  const start = getWeekStart(dateStr);
  const date = new Date(start);
  date.setDate(date.getDate() + 6);
  return date.toISOString().split("T")[0];
}

function getDayName(dateStr) {
  const days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
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
    const fieldName = slot.displayName || `${slot.parkName || ""} ${slot.fieldName || ""}`.trim() || fieldKey;

    if (!grouped.has(fieldKey)) {
      grouped.set(fieldKey, { fieldKey, fieldName, slots: [] });
    }

    grouped.get(fieldKey).slots.push(slot);
  });

  return Array.from(grouped.values()).sort((a, b) => a.fieldName.localeCompare(b.fieldName));
}
