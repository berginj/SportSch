function parseDate(value) {
  const v = (value || "").trim();
  if (!v) return null;
  const parts = v.split("-");
  if (parts.length !== 3) return null;
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return null;
  return new Date(year, month - 1, day);
}

function toIsoDate(d) {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

export function getSeasonRange(season, today = new Date()) {
  const springStart = parseDate(season?.springStart);
  const springEnd = parseDate(season?.springEnd);
  const fallStart = parseDate(season?.fallStart);
  const fallEnd = parseDate(season?.fallEnd);

  const inRange = (start, end) => start && end && today >= start && today <= end;
  const before = (start) => start && today < start;

  if (inRange(springStart, springEnd)) {
    return { from: toIsoDate(springStart), to: toIsoDate(springEnd) };
  }
  if (inRange(fallStart, fallEnd)) {
    return { from: toIsoDate(fallStart), to: toIsoDate(fallEnd) };
  }
  if (before(springStart)) {
    return { from: toIsoDate(springStart), to: toIsoDate(springEnd || springStart) };
  }
  if (before(fallStart)) {
    return { from: toIsoDate(fallStart), to: toIsoDate(fallEnd || fallStart) };
  }
  if (fallStart && fallEnd) {
    return { from: toIsoDate(fallStart), to: toIsoDate(fallEnd) };
  }
  if (springStart && springEnd) {
    return { from: toIsoDate(springStart), to: toIsoDate(springEnd) };
  }
  return null;
}

export function getDefaultRangeFallback(today = new Date(), days = 30) {
  const from = new Date(today);
  const to = new Date(today);
  to.setDate(to.getDate() + days);
  return { from: toIsoDate(from), to: toIsoDate(to) };
}
