const easternParts = new Intl.DateTimeFormat("en-CA", {
  timeZone: "America/New_York", year: "numeric", month: "2-digit", day: "2-digit",
  hour: "2-digit", minute: "2-digit", hourCycle: "h23",
});

// Schedule dates are Eastern wall time, independent of the browser's timezone.
// Reject both missing spring-forward times and ambiguous fall-back times.
export function scheduleInstant(date, time) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date || "") || !/^\d{2}:\d{2}$/.test(time || "")) return null;
  const wall = Date.parse(`${date}T${time}:00Z`);
  if (!Number.isFinite(wall)) return null;
  const candidates = [4, 5].map((offset) => wall + offset * 3600000).filter((instant) => {
    const parts = Object.fromEntries(easternParts.formatToParts(instant).map(({ type, value }) => [type, value]));
    return `${parts.year}-${parts.month}-${parts.day}` === date && `${parts.hour}:${parts.minute}` === time;
  });
  return candidates.length === 1 ? candidates[0] : null;
}

export function bookingLeadTime(date, time, now = Date.now()) {
  const instant = scheduleInstant(date, time);
  const hoursUntil = instant === null ? null : (instant - now) / 3600000;
  return { withinLeadTime: hoursUntil === null || hoursUntil < 72, hoursUntil: hoursUntil === null ? null : Math.round(hoursUntil * 10) / 10, minimumHours: 72 };
}
