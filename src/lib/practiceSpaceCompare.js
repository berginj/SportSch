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

function makeIdentity(fieldId, date, startTime, endTime) {
  return [String(fieldId || "").trim(), String(date || "").trim(), String(startTime || "").trim(), String(endTime || "").trim()].join("|");
}

function isImportedRequestable(row) {
  return row?.availabilityStatus === "available" && row?.utilizationStatus === "not_used";
}

function getImportedDivisionLabel(row) {
  return row?.canonicalDivisionCode || row?.rawAssignedDivision || "";
}

function buildIssueFlags(importedRow, manualSlot) {
  const issues = [];
  if (importedRow && manualSlot) {
    if (!isImportedRequestable(importedRow)) {
      issues.push("manual_overlap_nonrequestable");
    }
    const importedDivision = normalizeText(getImportedDivisionLabel(importedRow));
    const manualDivision = normalizeText(manualSlot?.division || "");
    if (importedDivision && manualDivision && importedDivision !== manualDivision) {
      issues.push("division_mismatch");
    }
    return issues;
  }

  if (importedRow && !manualSlot && isImportedRequestable(importedRow)) {
    issues.push("manual_missing");
  }

  if (!importedRow && manualSlot) {
    issues.push("import_missing");
  }

  return issues;
}

function buildCompareState(importedRow, manualSlot, issueFlags) {
  if (importedRow && manualSlot) {
    return issueFlags.length ? "conflict" : "aligned";
  }
  if (importedRow) return "imported_only";
  return "manual_only";
}

export function derivePracticeSpaceDateRange(rows = [], manualSlots = []) {
  const dates = [
    ...rows.map((row) => String(row?.date || "").trim()).filter(Boolean),
    ...manualSlots.map((slot) => String(slot?.gameDate || "").trim()).filter(Boolean),
  ].sort();
  if (!dates.length) {
    const today = new Date().toISOString().slice(0, 10);
    return { dateFrom: today, dateTo: today };
  }
  return { dateFrom: dates[0], dateTo: dates[dates.length - 1] };
}

export function buildPracticeSpaceComparison(rows = [], manualSlots = []) {
  const importedByKey = new Map();
  const manualByKey = new Map();

  rows.forEach((row) => {
    const key = makeIdentity(row?.fieldId, row?.date, row?.startTime, row?.endTime);
    if (!row?.fieldId || !row?.date || !row?.startTime || !row?.endTime) return;
    if (!importedByKey.has(key)) importedByKey.set(key, row);
  });

  manualSlots.forEach((slot) => {
    const key = makeIdentity(slot?.fieldKey, slot?.gameDate, slot?.startTime, slot?.endTime);
    if (!slot?.fieldKey || !slot?.gameDate || !slot?.startTime || !slot?.endTime) return;
    if (!manualByKey.has(key)) manualByKey.set(key, slot);
  });

  const keys = new Set([...importedByKey.keys(), ...manualByKey.keys()]);
  const items = Array.from(keys)
    .map((key) => {
      const importedRow = importedByKey.get(key) || null;
      const manualSlot = manualByKey.get(key) || null;
      const issueFlags = buildIssueFlags(importedRow, manualSlot);
      const compareState = buildCompareState(importedRow, manualSlot, issueFlags);
      const fieldId = importedRow?.fieldId || manualSlot?.fieldKey || "";
      const fieldName = importedRow?.fieldName || importedRow?.rawFieldName || manualSlot?.displayName || manualSlot?.fieldName || manualSlot?.fieldKey || "Field";
      const date = importedRow?.date || manualSlot?.gameDate || "";
      const startTime = importedRow?.startTime || manualSlot?.startTime || "";
      const endTime = importedRow?.endTime || manualSlot?.endTime || "";
      return {
        key,
        compareState,
        issueFlags,
        date,
        dayOfWeek: importedRow?.dayOfWeek || weekdayFromIso(date),
        startTime,
        endTime,
        fieldId,
        fieldName,
        importedRow,
        manualSlot,
      };
    })
    .sort((left, right) =>
      `${left.date}|${left.startTime}|${left.fieldName}`.localeCompare(`${right.date}|${right.startTime}|${right.fieldName}`)
    );

  return {
    items,
    summary: {
      importedCount: rows.length,
      manualCount: manualSlots.length,
      alignedCount: items.filter((item) => item.compareState === "aligned").length,
      importedOnlyCount: items.filter((item) => item.compareState === "imported_only").length,
      manualOnlyCount: items.filter((item) => item.compareState === "manual_only").length,
      conflictCount: items.filter((item) => item.compareState === "conflict").length,
      issueCount: items.filter((item) => item.issueFlags.length > 0).length,
    },
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
    if (!search) return true;
    const haystack = [
      item.fieldName,
      item.fieldId,
      item.date,
      item.startTime,
      item.endTime,
      item.importedRow?.assignedGroup,
      item.importedRow?.rawAssignedDivision,
      item.importedRow?.rawAssignedTeamOrEvent,
      item.manualSlot?.division,
      item.manualSlot?.displayName,
    ]
      .map((value) => String(value || "").toLowerCase())
      .join(" ");
    return haystack.includes(search);
  });
}

