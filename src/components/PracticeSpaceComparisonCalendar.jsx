import { useMemo, useState } from "react";
import { DayPilotMonth, DayPilotScheduler } from "@daypilot/daypilot-lite-react";
import "./CalendarView.css";

export default function PracticeSpaceComparisonCalendar({
  items = [],
  mode = "compare",
  title = "Practice Space Comparison Calendar",
}) {
  const [viewMode, setViewMode] = useState("timeline");
  const prepared = useMemo(() => prepareItems(items, mode), [items, mode]);
  const resources = useMemo(() => buildResources(prepared), [prepared]);
  const schedulerEvents = useMemo(() => prepared.map(buildSchedulerEvent), [prepared]);
  const monthEvents = useMemo(() => prepared.map(buildMonthEvent), [prepared]);
  const range = useMemo(() => getVisibleRange(prepared), [prepared]);
  const [selectedKey, setSelectedKey] = useState("");
  const selectedItem = useMemo(
    () => prepared.find((item) => item.id === selectedKey) || prepared[0] || null,
    [prepared, selectedKey]
  );

  const schedulerConfig = useMemo(() => ({
    startDate: range.startDate,
    days: range.days,
    scale: "CellDuration",
    cellDuration: 60,
    cellWidth: 46,
    rowHeaderWidth: 200,
    heightSpec: "Max",
    height: 580,
    businessBeginsHour: 6,
    businessEndsHour: 23,
    timeHeaders: [
      { groupBy: "Month" },
      { groupBy: "Day", format: "ddd M/d" },
      { groupBy: "Hour", format: "h tt" },
    ],
    eventMoveHandling: "Disabled",
    eventResizeHandling: "Disabled",
    resources,
    events: schedulerEvents,
    onEventClick: (args) => setSelectedKey(args.e?.data?.id || ""),
  }), [range.days, range.startDate, resources, schedulerEvents]);

  const monthConfig = useMemo(() => ({
    startDate: range.startDate,
    events: monthEvents,
    onEventClick: (args) => setSelectedKey(args.e?.data?.id || ""),
  }), [monthEvents, range.startDate]);

  if (!prepared.length) {
    return (
      <div className="calendar-view">
        <ViewToggle currentView={viewMode} onChange={setViewMode} />
        <div className="subtle">No calendar items match the current comparison filter.</div>
      </div>
    );
  }

  return (
    <div className="calendar-view">
      <div className="row row--wrap items-center gap-3">
        <div className="h2">{title}</div>
        <ViewToggle currentView={viewMode} onChange={setViewMode} />
      </div>

      <div className="calendar-view__summary">
        <span className="pill">Fields: {resources.length}</span>
        <span className="pill">Items: {prepared.length}</span>
        <span className="pill">Range: {range.startDate} to {range.endDate}</span>
      </div>

      <div className="calendar-daypilot">
        {viewMode === "month" ? <DayPilotMonth {...monthConfig} /> : <DayPilotScheduler {...schedulerConfig} />}
      </div>

      {selectedItem ? (
        <div className="calendar-selection card">
          <div className="calendar-selection__header">
            <div>
              <div className="cardTitle m-0">{selectedItem.fieldName}</div>
              <div className="subtle">
                {selectedItem.date} | {selectedItem.startTime}-{selectedItem.endTime}
              </div>
            </div>
            <span className={`calendar-selection__badge calendar-selection__badge--${selectedItem.tone}`}>
              {selectedItem.badge}
            </span>
          </div>
          <div className="calendar-selection__meta">
            <span>Field: {selectedItem.fieldId}</span>
            {selectedItem.importedRow ? <span>Imported: {formatImportedMeta(selectedItem.importedRow)}</span> : null}
            {selectedItem.manualSlot ? <span>Manual: {formatManualMeta(selectedItem.manualSlot)}</span> : null}
          </div>
          {selectedItem.issueFlags.length ? (
            <div className="row row--wrap gap-2">
              {selectedItem.issueFlags.map((issue) => (
                <span key={issue} className="pill">{humanizeIssue(issue)}</span>
              ))}
            </div>
          ) : (
            <div className="subtle">No comparison issues on this block.</div>
          )}
        </div>
      ) : null}
    </div>
  );
}

function ViewToggle({ currentView, onChange }) {
  return (
    <div className="calendar-view-toggle">
      <button className={`btn btn--ghost ${currentView === "timeline" ? "btn--active" : ""}`} type="button" onClick={() => onChange("timeline")}>
        Timeline
      </button>
      <button className={`btn btn--ghost ${currentView === "month" ? "btn--active" : ""}`} type="button" onClick={() => onChange("month")}>
        Month
      </button>
    </div>
  );
}

function prepareItems(items, mode) {
  return items
    .flatMap((item) => {
      if (mode === "imported") return item.importedRow ? [buildPreparedItem(item, "imported")] : [];
      if (mode === "manual") return item.manualSlot ? [buildPreparedItem(item, "manual")] : [];
      return [buildPreparedItem(item, "compare")];
    })
    .filter(Boolean);
}

function buildPreparedItem(item, mode) {
  const resourceName = item.fieldName || item.fieldId || "Field";
  const date = item.date;
  const start = combineIso(date, item.startTime);
  const end = combineIso(date, item.endTime);
  if (!date || !start || !end) return null;

  if (mode === "imported") {
    const tone = item.importedRow?.availabilityStatus === "available" && item.importedRow?.utilizationStatus === "not_used" ? "availability" : "scheduled";
    return {
      ...item,
      id: `imported:${item.key}`,
      resourceId: item.fieldId,
      resourceName,
      text: item.importedRow?.assignedGroup || item.importedRow?.rawAssignedDivision || item.importedRow?.rawAssignedTeamOrEvent || "Imported inventory",
      badge: "Imported",
      tone,
      start,
      end,
      backColor: tone === "availability" ? "#dbeafe" : "#e2e8f0",
      borderColor: tone === "availability" ? "#2563eb" : "#64748b",
      barColor: tone === "availability" ? "#2563eb" : "#64748b",
    };
  }

  if (mode === "manual") {
    return {
      ...item,
      id: `manual:${item.key}`,
      resourceId: item.fieldId,
      resourceName,
      text: item.manualSlot?.division || "Manual availability",
      badge: "Manual",
      tone: "open",
      start,
      end,
      backColor: "#dcfce7",
      borderColor: "#16a34a",
      barColor: "#16a34a",
    };
  }

  const config = getCompareTone(item.compareState, item.issueFlags);
  return {
    ...item,
    id: `compare:${item.key}`,
    resourceId: item.fieldId,
    resourceName,
    text: config.text,
    badge: config.badge,
    tone: config.tone,
    start,
    end,
    backColor: config.backColor,
    borderColor: config.borderColor,
    barColor: config.borderColor,
  };
}

function getCompareTone(compareState, issueFlags) {
  if (compareState === "aligned") {
    return { tone: "confirmed", badge: "Aligned", text: "Imported + Manual", backColor: "#dcfce7", borderColor: "#16a34a" };
  }
  if (compareState === "manual_only") {
    return { tone: "pending", badge: "Manual only", text: "Manual only", backColor: "#fef3c7", borderColor: "#ca8a04" };
  }
  if (compareState === "imported_only" && issueFlags.includes("manual_missing")) {
    return { tone: "cancelled", badge: "Gap", text: "Imported only", backColor: "#fee2e2", borderColor: "#dc2626" };
  }
  if (compareState === "conflict") {
    return { tone: "cancelled", badge: "Conflict", text: "Needs review", backColor: "#fee2e2", borderColor: "#dc2626" };
  }
  return { tone: "scheduled", badge: "Imported only", text: "Imported only", backColor: "#e2e8f0", borderColor: "#64748b" };
}

function buildResources(items) {
  const map = new Map();
  items.forEach((item) => {
    if (!map.has(item.resourceId)) {
      map.set(item.resourceId, { id: item.resourceId, name: item.resourceName });
    }
  });
  return Array.from(map.values()).sort((a, b) => a.name.localeCompare(b.name));
}

function buildSchedulerEvent(item) {
  return {
    id: item.id,
    resource: item.resourceId,
    start: item.start,
    end: item.end,
    text: item.text,
    backColor: item.backColor,
    borderColor: item.borderColor,
    barColor: item.barColor,
  };
}

function buildMonthEvent(item) {
  return {
    id: item.id,
    start: item.start,
    end: item.end,
    text: item.text,
    backColor: item.backColor,
    borderColor: item.borderColor,
    barColor: item.barColor,
  };
}

function getVisibleRange(items) {
  const dates = items.map((item) => item.date).filter(Boolean).sort();
  const startDate = dates[0];
  const endDate = dates[dates.length - 1];
  const start = new Date(`${startDate}T00:00:00`);
  const end = new Date(`${endDate}T00:00:00`);
  const days = Math.max(1, Math.round((end - start) / 86400000) + 1);
  return { startDate, endDate, days };
}

function combineIso(date, time) {
  if (!date) return "";
  const safeTime = String(time || "").trim() || "00:00";
  return `${date}T${safeTime}:00`;
}

function formatImportedMeta(row) {
  return [
    row?.availabilityStatus,
    row?.utilizationStatus,
    row?.assignedGroup,
    row?.rawAssignedDivision,
    row?.rawAssignedTeamOrEvent,
  ].filter(Boolean).join(" | ");
}

function formatManualMeta(slot) {
  return [slot?.division, slot?.displayName || slot?.fieldName].filter(Boolean).join(" | ");
}

function humanizeIssue(issue) {
  if (issue === "manual_missing") return "Imported block missing from manual availability";
  if (issue === "import_missing") return "Manual availability missing from import";
  if (issue === "division_mismatch") return "Division mismatch";
  if (issue === "manual_overlap_nonrequestable") return "Manual availability overlaps used or blocked import";
  return issue;
}

