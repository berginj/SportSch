function normalizeText(value) {
  return String(value || "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "");
}

function weekdayFromIso(date) {
  if (!date) return "";
  const value = new Date(`${date}T00:00:00`);
  if (Number.isNaN(value.getTime())) return "";
  return value.toLocaleDateString("en-US", { weekday: "long", timeZone: "UTC" });
}

function toCompareState(slot) {
  const state = String(slot?.normalizationState || "").trim().toLowerCase();
  if (state === "normalized") return "normalized";
  if (state === "ready") return "missing";
  if (state === "conflict") return "conflict";
  return "blocked";
}

function buildIssueFlags(importedRow, slot) {
  return Array.from(
    new Set([
      ...(Array.isArray(importedRow?.mappingIssues) ? importedRow.mappingIssues : []),
      ...(Array.isArray(slot?.normalizationIssues) ? slot.normalizationIssues : []),
    ].filter(Boolean))
  );
}

function buildSummary(items, rows, slots, normalization) {
  const issueCount = new Set(
    items
      .filter((item) => item.issueFlags.length > 0)
      .map((item) => item.importedRow?.recordId || item.slot?.liveRecordId || item.slot?.practiceSlotKey || item.slot?.slotId || item.key)
      .filter(Boolean)
  ).size;

  return {
    candidateCount: slots.length,
    importedCount: rows.length,
    blockCount: slots.length,
    normalizedCount: normalization?.normalizedBlocks ?? items.filter((item) => item.compareState === "normalized").length,
    missingCount: normalization?.missingBlocks ?? items.filter((item) => item.compareState === "missing").length,
    conflictCount: normalization?.conflictBlocks ?? items.filter((item) => item.compareState === "conflict").length,
    blockedCount: normalization?.blockedBlocks ?? items.filter((item) => item.compareState === "blocked").length,
    issueCount,
  };
}

export function derivePracticeSpaceDateRange(rows = [], slots = []) {
  const dates = [
    ...rows.map((row) => String(row?.date || "").trim()).filter(Boolean),
    ...slots.map((slot) => String(slot?.date || "").trim()).filter(Boolean),
  ].sort();

  if (!dates.length) {
    const today = new Date().toISOString().slice(0, 10);
    return { dateFrom: today, dateTo: today };
  }

  return { dateFrom: dates[0], dateTo: dates[dates.length - 1] };
}

export function buildPracticeSpaceComparison(rows = [], slots = [], normalization = null) {
  const rowsByRecordId = new Map();
  rows.forEach((row) => {
    if (row?.recordId) rowsByRecordId.set(row.recordId, row);
  });

  const items = (Array.isArray(slots) ? slots : [])
    .map((slot) => {
      const importedRow = rowsByRecordId.get(slot?.liveRecordId) || null;
      const compareState = toCompareState(slot);
      const issueFlags = buildIssueFlags(importedRow, slot);
      return {
        key: `${slot?.practiceSlotKey || slot?.slotId || slot?.liveRecordId || "slot"}|${compareState}`,
        compareState,
        issueFlags,
        date: slot?.date || importedRow?.date || "",
        dayOfWeek: slot?.dayOfWeek || importedRow?.dayOfWeek || weekdayFromIso(slot?.date || importedRow?.date || ""),
        startTime: slot?.startTime || importedRow?.startTime || "",
        endTime: slot?.endTime || importedRow?.endTime || "",
        fieldId: slot?.fieldId || importedRow?.fieldId || "",
        fieldName: slot?.fieldName || importedRow?.fieldName || importedRow?.rawFieldName || "Field",
        importedRow,
        slot,
      };
    })
    .sort((left, right) =>
      `${left.date || ""}|${left.startTime || ""}|${left.fieldName || ""}`.localeCompare(
        `${right.date || ""}|${right.startTime || ""}|${right.fieldName || ""}`
      )
    );

  return {
    items,
    summary: buildSummary(items, Array.isArray(rows) ? rows : [], Array.isArray(slots) ? slots : [], normalization),
  };
}

export function filterPracticeSpaceComparison(items = [], filters = {}) {
  const search = String(filters.search || "").trim().toLowerCase();
  return items.filter((item) => {
    if (filters.dateFrom && item.date < filters.dateFrom) return false;
    if (filters.dateTo && item.date > filters.dateTo) return false;
    if (filters.compareState && item.compareState !== filters.compareState) return false;
    if (filters.issue && !item.issueFlags.includes(filters.issue)) return false;
    if (filters.fieldId && item.fieldId !== filters.fieldId) return false;
    if (filters.division) {
      const division = String(item.slot?.division || item.importedRow?.canonicalDivisionCode || item.importedRow?.rawAssignedDivision || "").trim();
      if (division !== filters.division) return false;
    }
    if (!search) return true;

    const haystack = [
      item.fieldName,
      item.fieldId,
      item.date,
      item.startTime,
      item.endTime,
      item.compareState,
      item.slot?.division,
      item.slot?.slotId,
      item.slot?.bookingPolicyLabel,
      item.slot?.assignedGroup,
      item.slot?.assignedDivision,
      item.slot?.assignedTeamOrEvent,
      item.importedRow?.assignedGroup,
      item.importedRow?.rawAssignedDivision,
      item.importedRow?.rawAssignedTeamOrEvent,
      item.issueFlags.join(" "),
    ]
      .map((value) => String(value || "").toLowerCase())
      .join(" ");

    return haystack.includes(search) || normalizeText(haystack).includes(normalizeText(search));
  });
}
