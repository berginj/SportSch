/**
 * Escapes a value for CSV format.
 * Wraps in quotes if it contains comma, quote, or newline.
 */
export function csvEscape(value) {
  const raw = String(value ?? "");
  if (!/[",\n]/.test(raw)) return raw;
  return `"${raw.replace(/"/g, '""')}"`;
}

/**
 * Builds a CSV template for teams import.
 * @param {Array} divisions - List of division objects or strings
 * @returns {string} CSV content
 */
export function buildTeamsTemplateCsv(divisions) {
  const header = ["division", "teamId", "name", "coachName", "coachEmail", "coachPhone"];
  const rows = (divisions || [])
    .map((d) => {
      if (!d) return "";
      if (typeof d === "string") return d;
      if (d.isActive === false) return "";
      return d.code || d.division || "";
    })
    .filter(Boolean)
    .map((code) => [code, "", "", "", "", ""]);

  return [header, ...rows].map((row) => row.map(csvEscape).join(",")).join("\n");
}

/**
 * Downloads a CSV string as a file.
 * @param {string} csv - CSV content
 * @param {string} filename - Desired filename
 */
export function downloadCsv(csv, filename) {
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.setAttribute("download", filename);
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
