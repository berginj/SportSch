const DAY_LABELS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

function parseTimeMinutes(raw) {
  const parts = (raw || "").split(":");
  if (parts.length < 2) return null;
  const h = Number(parts[0]);
  const m = Number(parts[1]);
  if (!Number.isFinite(h) || !Number.isFinite(m)) return null;
  return h * 60 + m;
}

function parseIsoDate(value) {
  const parts = (value || "").split("-");
  if (parts.length !== 3) return null;
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return null;
  return new Date(year, month - 1, day);
}

export function buildAvailabilityInsights(slots) {
  const dayStats = DAY_LABELS.map((day) => ({ day, slots: 0, minutes: 0 }));
  for (const s of slots || []) {
    const dt = parseIsoDate(s.gameDate);
    if (!dt) continue;
    const idx = dt.getDay();
    const bucket = dayStats[idx];
    if (!bucket) continue;
    bucket.slots += 1;
    const start = parseTimeMinutes(s.startTime);
    const end = parseTimeMinutes(s.endTime);
    if (start != null && end != null && end > start) bucket.minutes += end - start;
  }
  const totalSlots = dayStats.reduce((sum, d) => sum + d.slots, 0);
  const totalMinutes = dayStats.reduce((sum, d) => sum + d.minutes, 0);
  const ranked = [...dayStats]
    .filter((d) => d.slots > 0)
    .sort((a, b) => (b.slots - a.slots) || (b.minutes - a.minutes));
  const suggested = ranked.slice(0, 2).map((d) => d.day);
  return { dayStats, totalSlots, totalMinutes, suggested };
}
