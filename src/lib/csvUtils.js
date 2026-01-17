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

/**
 * Parses CSV text into an array of objects.
 * Assumes first row is headers.
 * @param {string} csvText - The CSV content to parse
 * @returns {Array<Object>} Array of objects with keys from header row
 */
export function parseCsv(csvText) {
  if (!csvText || !csvText.trim()) return [];

  const lines = csvText.trim().split("\n");
  if (lines.length === 0) return [];

  const headers = lines[0].split(",").map(h => h.trim().replace(/^"|"$/g, ""));
  const rows = [];

  for (let i = 1; i < lines.length; i++) {
    const values = lines[i].split(",").map(v => v.trim().replace(/^"|"$/g, ""));
    if (values.length !== headers.length) continue; // Skip malformed rows

    const row = {};
    headers.forEach((header, idx) => {
      row[header] = values[idx] || "";
    });
    rows.push(row);
  }

  return rows;
}

/**
 * Validates a CSV row against required fields.
 * @param {Object} row - The row object to validate
 * @param {Array<string>} requiredFields - Array of required field names
 * @returns {Object} { valid: boolean, missing: Array<string> }
 */
export function validateCsvRow(row, requiredFields) {
  const missing = requiredFields.filter(field => !row[field] || !row[field].trim());
  return {
    valid: missing.length === 0,
    missing
  };
}
