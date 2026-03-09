import { useMemo } from "react";
import { DayPilotMonth } from "@daypilot/daypilot-lite-react";

export default function SeasonSummaryCalendar({
  assignments = [],
  openSlots = [],
  selectedAssignmentKey = "",
  onSelectAssignment,
  getAssignmentKey = defaultAssignmentKey,
}) {
  const items = useMemo(() => {
    const byDate = new Map();

    assignments.forEach((assignment) => {
      const gameDate = String(assignment?.gameDate || "").trim();
      if (!gameDate) return;
      if (!byDate.has(gameDate)) {
        byDate.set(gameDate, { date: gameDate, games: 0, open: 0, guest: 0, assignments: [] });
      }
      const entry = byDate.get(gameDate);
      entry.games += 1;
      if (assignment?.isExternalOffer) entry.guest += 1;
      entry.assignments.push(assignment);
    });

    openSlots.forEach((slot) => {
      const gameDate = String(slot?.gameDate || "").trim();
      if (!gameDate) return;
      if (!byDate.has(gameDate)) {
        byDate.set(gameDate, { date: gameDate, games: 0, open: 0, guest: 0, assignments: [] });
      }
      byDate.get(gameDate).open += 1;
    });

    return Array.from(byDate.values())
      .sort((a, b) => a.date.localeCompare(b.date))
      .map((entry) => {
        const selected = entry.assignments.some((assignment) => getAssignmentKey(assignment) === selectedAssignmentKey);
        const singleAssignment = entry.assignments.length === 1 ? entry.assignments[0] : null;
        const counts = [`G${entry.games}`];
        if (entry.open > 0) counts.push(`Open ${entry.open}`);
        if (entry.guest > 0) counts.push(`Guest ${entry.guest}`);
        return {
          id: entry.date,
          date: entry.date,
          assignmentKey: singleAssignment ? getAssignmentKey(singleAssignment) : "",
          text: counts.join(" | "),
          start: `${entry.date}T00:00:00`,
          end: addDays(entry.date, 1),
          backColor: selected ? "#bfdbfe" : (entry.open > 0 ? "#dbeafe" : "#dcfce7"),
          borderColor: selected ? "#1d4ed8" : (entry.open > 0 ? "#2563eb" : "#16a34a"),
          barColor: selected ? "#1d4ed8" : (entry.open > 0 ? "#2563eb" : "#15803d"),
          toolTip: `${entry.date}\nGames: ${entry.games}\nOpen slots: ${entry.open}\nGuest games: ${entry.guest}`,
        };
      });
  }, [assignments, getAssignmentKey, openSlots, selectedAssignmentKey]);

  const startDate = items[0]?.date || "";

  if (!items.length) {
    return <div className="subtle">No regular-season dates to display in the calendar.</div>;
  }

  return (
    <div className="calendar-daypilot">
      <DayPilotMonth
        startDate={startDate}
        eventClickHandling="Enabled"
        events={items}
        onEventClick={(args) => {
          const assignmentKey = args.e?.data?.assignmentKey || "";
          if (assignmentKey) onSelectAssignment?.(assignmentKey);
        }}
      />
    </div>
  );
}

function addDays(isoDate, daysToAdd) {
  const date = new Date(`${isoDate}T00:00:00`);
  if (Number.isNaN(date.getTime())) return `${isoDate}T00:00:00`;
  date.setDate(date.getDate() + daysToAdd);
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}T00:00:00`;
}

function defaultAssignmentKey(assignment) {
  return [
    assignment?.gameDate || "",
    assignment?.homeTeamId || assignment?.offeringTeamId || "",
    assignment?.awayTeamId || "",
    assignment?.fieldKey || "",
    assignment?.startTime || "",
  ].join("|");
}
