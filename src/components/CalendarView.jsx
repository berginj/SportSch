import { useEffect, useMemo, useState } from "react";
import { DayPilotMonth, DayPilotScheduler } from "@daypilot/daypilot-lite-react";
import "./CalendarView.css";

export default function CalendarView({
  slots = [],
  events = [],
  defaultView = "timeline",
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
      return normalizeViewMode(localStorage.getItem(storageKey) || defaultView);
    } catch {
      return normalizeViewMode(defaultView);
    }
  });

  const items = useMemo(() => buildCalendarItems(slots, events), [slots, events]);
  const resources = useMemo(() => buildResources(items), [items]);
  const schedulerEvents = useMemo(() => items.map(buildSchedulerEvent), [items]);
  const monthEvents = useMemo(() => items.map(buildMonthEvent), [items]);
  const range = useMemo(() => getVisibleRange(items), [items]);
  const [selectedItemKey, setSelectedItemKey] = useState("");

  useEffect(() => {
    if (!items.length) {
      setSelectedItemKey("");
      return;
    }

    if (!items.some((item) => item.key === selectedItemKey)) {
      setSelectedItemKey(items[0].key);
    }
  }, [items, selectedItemKey]);

  const selectedItem = useMemo(
    () => items.find((item) => item.key === selectedItemKey) || null,
    [items, selectedItemKey]
  );

  // Memoize the event click handler to prevent unnecessary re-renders
  const handleItemSelect = useMemo(() => {
    return (itemKey) => {
      if (!itemKey) return;
      setSelectedItemKey(itemKey);
      const item = items.find((entry) => entry.key === itemKey);
      if (!item) return;
      if (item.kind === "slot") onSlotClick?.(item.raw);
      else onEventClick?.(item.raw);
    };
  }, [items, onSlotClick, onEventClick]);

  // Base scheduler configuration (stable, non-dynamic)
  const baseSchedulerConfig = useMemo(() => ({
    scale: "CellDuration",
    cellDuration: 60,
    cellWidth: 46,
    rowHeaderWidth: 180,
    heightSpec: "Max",
    height: 560,
    businessBeginsHour: 8,
    businessEndsHour: 22,
    timeHeaders: [
      { groupBy: "Month" },
      { groupBy: "Day", format: "ddd M/d" },
      { groupBy: "Hour", format: "h tt" },
    ],
    eventMoveHandling: "Disabled",
    eventResizeHandling: "Disabled",
    timeRangeSelectedHandling: "Disabled",
    eventClickHandling: "Enabled",
    durationBarVisible: false,
  }), []);

  // Dynamic scheduler configuration (depends on data that changes)
  const schedulerConfig = useMemo(() => ({
    ...baseSchedulerConfig,
    startDate: range.startDate,
    days: range.days,
    resources,
    events: schedulerEvents,
    onBeforeEventRender: (args) => {
      const selected = args.data.id === selectedItemKey;
      args.data.borderColor = selected ? "#1d4ed8" : args.data.borderColor;
      args.data.fontColor = "#0f172a";
    },
    onEventClick: (args) => {
      handleItemSelect(args.e?.data?.key || "");
    },
  }), [baseSchedulerConfig, range.days, range.startDate, resources, schedulerEvents, selectedItemKey, handleItemSelect]);

  // Base month configuration (stable)
  const baseMonthConfig = useMemo(() => ({
    eventClickHandling: "Enabled",
  }), []);

  // Dynamic month configuration
  const monthConfig = useMemo(() => ({
    ...baseMonthConfig,
    startDate: range.startDate,
    events: monthEvents,
    onBeforeEventRender: (args) => {
      const selected = args.data.id === selectedItemKey;
      args.data.borderColor = selected ? "#1d4ed8" : args.data.borderColor;
      args.data.fontColor = "#0f172a";
    },
    onEventClick: (args) => {
      handleItemSelect(args.e?.data?.key || "");
    },
  }), [baseMonthConfig, monthEvents, range.startDate, selectedItemKey, handleItemSelect]);

  function handleViewChange(mode) {
    const nextMode = normalizeViewMode(mode);
    setViewMode(nextMode);
    try {
      localStorage.setItem(storageKey, nextMode);
    } catch {
      // Ignore localStorage errors
    }
  }

  if (!items.length) {
    return (
      <div className="calendar-view">
        {showViewToggle ? <ViewToggle currentView={viewMode} onChange={handleViewChange} /> : null}
        <div className="subtle">No items in this range.</div>
      </div>
    );
  }

  return (
    <div className="calendar-view">
      {showViewToggle ? <ViewToggle currentView={viewMode} onChange={handleViewChange} /> : null}

      <div className="calendar-view__summary">
        <span className="pill">Fields: {resources.length}</span>
        <span className="pill">Items: {items.length}</span>
        <span className="pill">Range: {range.startDate} to {range.endDate}</span>
      </div>

      <div className="calendar-daypilot">
        {viewMode === "month" ? (
          <DayPilotMonth {...monthConfig} />
        ) : (
          <DayPilotScheduler {...schedulerConfig} />
        )}
      </div>

      {selectedItem ? (
        <SelectionPanel
          item={selectedItem}
          renderSlotActions={renderSlotActions}
          renderEventActions={renderEventActions}
        />
      ) : null}
    </div>
  );
}

function ViewToggle({ currentView, onChange }) {
  return (
    <div className="calendar-view-toggle">
      <button
        className={`btn btn--ghost ${currentView === "timeline" ? "btn--active" : ""}`}
        type="button"
        onClick={() => onChange("timeline")}
        title="DayPilot timeline view"
      >
        Timeline
      </button>
      <button
        className={`btn btn--ghost ${currentView === "month" ? "btn--active" : ""}`}
        type="button"
        onClick={() => onChange("month")}
        title="DayPilot month view"
      >
        Month
      </button>
    </div>
  );
}

function SelectionPanel({ item, renderSlotActions, renderEventActions }) {
  if (item.kind === "slot") {
    const slot = item.raw;
    const actions = renderSlotActions?.(slot);
    return (
      <div className="calendar-selection card">
        <div className="calendar-selection__header">
          <div>
            <div className="cardTitle m-0">{getMatchupLabel(slot) || "Slot"}</div>
            <div className="subtle">
              {slot.gameDate} | {formatTimeRange(slot.startTime, slot.endTime)} | {getFieldLabel(slot) || "Field TBD"}
            </div>
          </div>
          <span className={`calendar-selection__badge calendar-selection__badge--${getSlotToneKey(slot)}`}>
            {getSlotStatusLabel(slot)}
          </span>
        </div>
        <div className="calendar-selection__meta">
          {slot.division ? <span>Division: {slot.division}</span> : null}
          {slot.confirmedTeamId ? <span>Confirmed: {slot.confirmedTeamId}</span> : null}
          {slot.address ? (
            <a
              href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(slot.address)}`}
              target="_blank"
              rel="noopener noreferrer"
            >
              Open map
            </a>
          ) : null}
        </div>
        {actions ? <div className="calendar-selection__actions">{actions}</div> : null}
      </div>
    );
  }

  const event = item.raw;
  const actions = renderEventActions?.(event);
  return (
    <div className="calendar-selection card">
      <div className="calendar-selection__header">
        <div>
          <div className="cardTitle m-0">{getEventTitle(event)}</div>
          <div className="subtle">
            {item.date} | {formatOptionalTimeRange(event.startTime, event.endTime) || "All day"}
          </div>
        </div>
        <span className="calendar-selection__badge calendar-selection__badge--event">Event</span>
      </div>
      <div className="calendar-selection__meta">
        {event.location ? <span>Location: {event.location}</span> : null}
        {event.division ? <span>Division: {event.division}</span> : null}
        {event.teamId ? <span>Team: {event.teamId}</span> : null}
        {event.notes ? <span>{event.notes}</span> : null}
      </div>
      {actions ? <div className="calendar-selection__actions">{actions}</div> : null}
    </div>
  );
}

function buildCalendarItems(slots, events) {
  return [
    ...slots
      .map((slot, index) => buildItem("slot", slot, `slot-${slot?.slotId || index}`))
      .filter(Boolean),
    ...events
      .map((event, index) => buildItem("event", event, `event-${event?.eventId || index}`))
      .filter(Boolean),
  ].sort((left, right) => {
    const leftKey = `${left.date} ${left.startTime || ""} ${left.resourceName}`;
    const rightKey = `${right.date} ${right.startTime || ""} ${right.resourceName}`;
    return leftKey.localeCompare(rightKey);
  });
}

function buildItem(kind, raw, fallbackKey) {
  const date = getItemDate(raw);
  if (!date) return null;

  const resourceName = kind === "slot"
    ? getFieldLabel(raw) || "Field TBD"
    : (String(raw?.location || "").trim() || "League Events");
  const resourceId = slugify(`${kind}:${resourceName}`);
  const startIso = combineIsoDateTime(date, kind === "slot" ? raw?.startTime : raw?.startTime);
  const endIso = combineIsoDateTime(date, kind === "slot" ? raw?.endTime : raw?.endTime);
  const normalizedEndIso = ensureEndAfterStart(startIso, endIso);
  const tone = kind === "slot" ? getSlotToneKey(raw) : "event";

  return {
    key: fallbackKey,
    kind,
    raw,
    date,
    startIso,
    endIso: normalizedEndIso,
    startTime: raw?.startTime || "",
    endTime: raw?.endTime || "",
    resourceId,
    resourceName,
    text: kind === "slot" ? (getMatchupLabel(raw) || "Slot") : getEventTitle(raw),
    subtitle: kind === "slot"
      ? [raw?.division, getSlotStatusLabel(raw)].filter(Boolean).join(" | ")
      : getEventSubtitle(raw),
    toolTip: kind === "slot"
      ? `${getMatchupLabel(raw) || "Slot"}\n${date} ${formatTimeRange(raw?.startTime, raw?.endTime)}\n${resourceName}`
      : `${getEventTitle(raw)}\n${date} ${formatOptionalTimeRange(raw?.startTime, raw?.endTime)}`.trim(),
    colors: getItemColors(tone),
  };
}

function buildResources(items) {
  const resources = new Map();
  items.forEach((item) => {
    if (!resources.has(item.resourceId)) {
      resources.set(item.resourceId, { id: item.resourceId, name: item.resourceName });
    }
  });
  return Array.from(resources.values()).sort((a, b) => a.name.localeCompare(b.name));
}

function buildSchedulerEvent(item) {
  return {
    id: item.key,
    key: item.key,
    kind: item.kind,
    resource: item.resourceId,
    text: item.subtitle ? `${item.text} • ${item.subtitle}` : item.text,
    start: item.startIso,
    end: item.endIso,
    toolTip: item.toolTip,
    backColor: item.colors.backColor,
    borderColor: item.colors.borderColor,
    barColor: item.colors.barColor,
  };
}

function buildMonthEvent(item) {
  return {
    id: item.key,
    key: item.key,
    kind: item.kind,
    text: item.text,
    start: item.startIso,
    end: item.endIso,
    toolTip: item.toolTip,
    backColor: item.colors.backColor,
    borderColor: item.colors.borderColor,
    barColor: item.colors.barColor,
  };
}

function getVisibleRange(items) {
  if (!items.length) {
    const today = new Date();
    return {
      startDate: formatLocalIsoDate(today),
      endDate: formatLocalIsoDate(today),
      days: 1,
    };
  }

  const dates = items.map((item) => parseLocalIsoDate(item.date)).filter(Boolean);
  const start = new Date(Math.min(...dates.map((date) => date.getTime())));
  const end = new Date(Math.max(...dates.map((date) => date.getTime())));
  const days = Math.max(1, Math.round((end - start) / 86400000) + 1);
  return {
    startDate: formatLocalIsoDate(start),
    endDate: formatLocalIsoDate(end),
    days,
  };
}

function combineIsoDateTime(date, timeValue) {
  const time = normalizeTimeValue(timeValue);
  return `${date}T${time}:00`;
}

function ensureEndAfterStart(startIso, endIso) {
  const start = new Date(startIso);
  const end = new Date(endIso);
  if (Number.isNaN(start.getTime())) return endIso;
  if (!Number.isNaN(end.getTime()) && end > start) return endIso;
  const fallback = new Date(start.getTime() + 60 * 60 * 1000);
  return `${formatLocalIsoDate(fallback)}T${String(fallback.getHours()).padStart(2, "0")}:${String(fallback.getMinutes()).padStart(2, "0")}:00`;
}

function normalizeTimeValue(timeValue) {
  const trimmed = String(timeValue || "").trim();
  if (!trimmed) return "12:00";
  if (/^\d{2}:\d{2}$/.test(trimmed)) return trimmed;
  const parts = trimmed.split(":");
  if (parts.length === 2) {
    return `${String(Number(parts[0]) || 0).padStart(2, "0")}:${String(Number(parts[1]) || 0).padStart(2, "0")}`;
  }
  return "12:00";
}

function getItemColors(tone) {
  switch (tone) {
    case "open":
      return { backColor: "#fef3c7", borderColor: "#f59e0b", barColor: "#d97706" };
    case "confirmed":
      return { backColor: "#dcfce7", borderColor: "#16a34a", barColor: "#15803d" };
    case "cancelled":
      return { backColor: "#fee2e2", borderColor: "#dc2626", barColor: "#b91c1c" };
    case "availability":
      return { backColor: "#dbeafe", borderColor: "#2563eb", barColor: "#1d4ed8" };
    case "pending":
      return { backColor: "#fef9c3", borderColor: "#ca8a04", barColor: "#a16207" };
    case "event":
      return { backColor: "#ede9fe", borderColor: "#7c3aed", barColor: "#6d28d9" };
    default:
      return { backColor: "#e2e8f0", borderColor: "#64748b", barColor: "#475569" };
  }
}

function stopItemClickPropagation(event) {
  event.stopPropagation();
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
    event?.location ? `Location: ${event.location}` : "",
    event?.division ? `Division: ${event.division}` : "",
  ]
    .filter(Boolean)
    .join(" | ");
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

function slugify(value) {
  return String(value || "")
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function normalizeViewMode(value) {
  const normalized = String(value || "").trim().toLowerCase();
  if (normalized === "month" || normalized === "agenda") return "month";
  return "timeline";
}

export { stopItemClickPropagation };
