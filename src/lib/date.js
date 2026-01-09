const ISO_DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

export function isStrictIsoDate(value) {
  const v = (value || "").trim();
  if (!ISO_DATE_RE.test(v)) return false;
  const parts = v.split("-");
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return false;
  const dt = new Date(Date.UTC(year, month - 1, day));
  return dt.getUTCFullYear() === year && dt.getUTCMonth() === month - 1 && dt.getUTCDate() === day;
}

export function validateIsoDates(fields) {
  for (const field of fields || []) {
    const raw = (field?.value || "").trim();
    if (!raw) {
      if (field?.required) return `${field?.label || "Date"} is required.`;
      continue;
    }
    if (!isStrictIsoDate(raw)) return `${field?.label || "Date"} must be YYYY-MM-DD.`;
  }
  return "";
}
