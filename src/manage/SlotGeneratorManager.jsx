import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import Toast from "../components/Toast";
import { getDefaultRangeFallback, getSeasonRange, getSlotsDefaultRange } from "../lib/season";
import { trackEvent } from "../lib/telemetry";

const ALL_FIELDS_VALUE = "__ALL_FIELDS__";

function csvEscape(value) {
  const raw = String(value ?? "");
  if (!/[",\n]/.test(raw)) return raw;
  return `"${raw.replace(/"/g, '""')}"`;
}

function downloadCsv(csv, filename) {
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

function buildAvailabilityTemplateCsv(divisions, fields) {
  const header = ["division", "gameDate", "startTime", "endTime", "fieldKey", "notes", "parkName", "fieldName", "displayName"];
  const divs = (divisions || [])
    .filter((d) => d && d.isActive !== false)
    .map((d) => (typeof d === "string" ? d : d.code || ""))
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b));
  const rows = [];
  for (const div of divs) {
    for (const f of fields || []) {
      if (!f?.fieldKey) continue;
      rows.push([
        div,
        "",
        "",
        "",
        f.fieldKey,
        "",
        f.parkName || "",
        f.fieldName || "",
        f.displayName || "",
      ]);
    }
  }
  return [header, ...rows].map((row) => row.map(csvEscape).join(",")).join("\n");
}

function parseCsvRows(text) {
  const rows = [];
  let row = [];
  let field = "";
  let inQuotes = false;

  for (let i = 0; i < text.length; i += 1) {
    const c = text[i];
    if (inQuotes) {
      if (c === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i += 1;
        } else {
          inQuotes = false;
        }
      } else {
        field += c;
      }
      continue;
    }

    if (c === '"') {
      inQuotes = true;
      continue;
    }
    if (c === ",") {
      row.push(field);
      field = "";
      continue;
    }
    if (c === "\n") {
      row.push(field);
      rows.push(row);
      row = [];
      field = "";
      continue;
    }
    if (c === "\r") continue;
    field += c;
  }

  row.push(field);
  rows.push(row);
  return rows.filter((r) => r.some((cell) => String(cell || "").trim() !== ""));
}

function buildHeaderIndex(header) {
  const index = {};
  (header || []).forEach((h, idx) => {
    const key = String(h || "").trim().toLowerCase();
    if (key) index[key] = idx;
  });
  return index;
}

function formatApiError(error, fallback) {
  const base = error?.originalMessage || error?.message || fallback;
  const requestId = error?.details?.requestId;
  const stage = error?.details?.stage;
  const exception = error?.details?.exception;
  const detailMessage = error?.details?.message;
  const parts = [];
  if (requestId) parts.push(`requestId: ${requestId}`);
  if (stage) parts.push(`stage: ${stage}`);
  if (exception) parts.push(`exception: ${exception}`);
  if (detailMessage) parts.push(`detail: ${detailMessage}`);
  if (!parts.length) return base;
  return `${base} (${parts.join(", ")})`;
}

function extractSlotItems(payload) {
  if (Array.isArray(payload)) return payload;
  if (!payload || typeof payload !== "object") return [];
  if (Array.isArray(payload.items)) return payload.items;
  if (payload.data && typeof payload.data === "object" && Array.isArray(payload.data.items)) {
    return payload.data.items;
  }
  if (Array.isArray(payload.data)) return payload.data;
  return [];
}

const DEFAULT_DAYS = {
  Mon: false,
  Tue: false,
  Wed: false,
  Thu: false,
  Fri: false,
  Sat: false,
  Sun: false,
};

const WEEKDAY_OPTIONS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

function isIsoDate(value) {
  return /^\d{4}-\d{2}-\d{2}$/.test(String(value || "").trim());
}

function isoToUtcDate(value) {
  if (!isIsoDate(value)) return null;
  const [year, month, day] = String(value).split("-").map((part) => Number(part));
  const dt = new Date(Date.UTC(year, month - 1, day));
  if (Number.isNaN(dt.getTime())) return null;
  return dt;
}

function addIsoDays(value, days) {
  const dt = isoToUtcDate(value);
  if (!dt) return "";
  dt.setUTCDate(dt.getUTCDate() + Number(days || 0));
  const year = dt.getUTCFullYear();
  const month = String(dt.getUTCMonth() + 1).padStart(2, "0");
  const day = String(dt.getUTCDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function weekdayFromIso(value) {
  const dt = isoToUtcDate(value);
  if (!dt) return "Other";
  return WEEKDAY_OPTIONS[(dt.getUTCDay() + 6) % 7] || "Other";
}

function parseMinutes(value) {
  const match = /^(\d{1,2}):(\d{2})$/.exec(String(value || "").trim());
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  if (!Number.isInteger(hours) || hours < 0 || hours > 23) return null;
  if (!Number.isInteger(minutes) || minutes < 0 || minutes > 59) return null;
  return hours * 60 + minutes;
}

function formatDurationMinutes(startTime, endTime) {
  const start = parseMinutes(startTime);
  const end = parseMinutes(endTime);
  if (start == null || end == null || end <= start) return "";
  return end - start;
}

function collectInterruptionDates(firstDate, lastDate, weekday, existingDates) {
  if (!isIsoDate(firstDate) || !isIsoDate(lastDate) || firstDate > lastDate) return [];
  if (!WEEKDAY_OPTIONS.includes(weekday)) return [];
  const missing = [];
  let cursor = firstDate;
  while (cursor && cursor <= lastDate) {
    if (weekdayFromIso(cursor) === weekday && !existingDates.has(cursor)) {
      missing.push(cursor);
    }
    cursor = addIsoDays(cursor, 1);
  }
  return missing;
}

export default function SlotGeneratorManager({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [fields, setFields] = useState([]);
  const [leagueSeason, setLeagueSeason] = useState(null);
  const [slotGenDivision, setSlotGenDivision] = useState("");
  const [slotGenFieldKey, setSlotGenFieldKey] = useState(ALL_FIELDS_VALUE);
  const [slotGenStartTime, setSlotGenStartTime] = useState("");
  const [slotGenEndTime, setSlotGenEndTime] = useState("");
  const [slotGenDays, setSlotGenDays] = useState(DEFAULT_DAYS);
  const [slotGenPreview, setSlotGenPreview] = useState(null);
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [availFile, setAvailFile] = useState(null);
  const [availBusy, setAvailBusy] = useState(false);
  const [availErr, setAvailErr] = useState("");
  const [availOk, setAvailOk] = useState("");
  const [availListErr, setAvailListErr] = useState("");
  const [availListMsg, setAvailListMsg] = useState("");
  const [availErrors, setAvailErrors] = useState([]);
  const [availWarnings, setAvailWarnings] = useState([]);
  const [availDivision, setAvailDivision] = useState("");
  const [availFieldKey, setAvailFieldKey] = useState("");
  const [availDateFrom, setAvailDateFrom] = useState("");
  const [availDateTo, setAvailDateTo] = useState("");
  const [availSlots, setAvailSlots] = useState([]);
  const [availListLoading, setAvailListLoading] = useState(false);
  const [availPatternDrafts, setAvailPatternDrafts] = useState({});
  const [availExpandedPatternKey, setAvailExpandedPatternKey] = useState("");
  const [availPatternSavingKey, setAvailPatternSavingKey] = useState("");
  const [availPatternRemovingKey, setAvailPatternRemovingKey] = useState("");
  const [availOccurrenceSavingKey, setAvailOccurrenceSavingKey] = useState("");
  const [availImportUnknowns, setAvailImportUnknowns] = useState([]);
  const [availImportRows, setAvailImportRows] = useState([]);
  const [availImportFixes, setAvailImportFixes] = useState({});
  const [availImportDefault, setAvailImportDefault] = useState("");
  const [toast, setToast] = useState(null);

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      try {
        const [divs, flds, league] = await Promise.all([
          apiFetch("/api/divisions"),
          apiFetch("/api/fields"),
          apiFetch("/api/league"),
        ]);
        const list = Array.isArray(divs) ? divs : [];
        setDivisions(list);
        setFields(Array.isArray(flds) ? flds : []);
        setLeagueSeason(league?.season || null);
        if (list.length) {
          setSlotGenDivision((prev) => prev || list[0].code || "");
        }
        if (Array.isArray(flds) && flds.length) {
          setSlotGenFieldKey((prev) => prev || ALL_FIELDS_VALUE);
        } else {
          setSlotGenFieldKey("");
        }
      } catch (e) {
        setErr(e?.message || "Failed to load divisions/fields.");
        setDivisions([]);
        setFields([]);
        setLeagueSeason(null);
      }
    })();
  }, [leagueId]);

  const seasonRange = useMemo(() => {
    const fallback = getDefaultRangeFallback();
    const range = getSeasonRange(leagueSeason, new Date());
    return range || fallback;
  }, [leagueSeason]);

  const slotsRange = useMemo(
    () => getSlotsDefaultRange(leagueSeason, new Date()),
    [leagueSeason]
  );

  useEffect(() => {
    if (!dateFrom) setDateFrom(seasonRange.from);
    if (!dateTo) setDateTo(seasonRange.to);
  }, [seasonRange, dateFrom, dateTo]);

  useEffect(() => {
    if (!availDateFrom) setAvailDateFrom(slotsRange.from);
    if (!availDateTo) setAvailDateTo(slotsRange.to);
  }, [slotsRange, availDateFrom, availDateTo]);

  useEffect(() => {
    if (!slotGenDivision && divisions.length) {
      setSlotGenDivision(divisions[0].code || "");
    }
  }, [divisions, slotGenDivision]);

  useEffect(() => {
    if (!slotGenFieldKey && fields.length) {
      setSlotGenFieldKey(ALL_FIELDS_VALUE);
    }
  }, [fields, slotGenFieldKey]);

  useEffect(() => {
    if (!availFieldKey && fields.length) {
      setAvailFieldKey("");
    }
  }, [fields, availFieldKey]);

  const selectedSlotGenFieldKeys = useMemo(() => {
    if (slotGenFieldKey === ALL_FIELDS_VALUE) {
      return (fields || [])
        .map((f) => f?.fieldKey || "")
        .filter(Boolean);
    }
    return slotGenFieldKey ? [slotGenFieldKey] : [];
  }, [fields, slotGenFieldKey]);

  const fieldLabelMap = useMemo(() => {
    const map = new Map();
    (fields || []).forEach((field) => {
      const fieldKey = String(field?.fieldKey || "").trim();
      if (!fieldKey) return;
      map.set(fieldKey, String(field?.displayName || field?.fieldKey || "").trim() || fieldKey);
    });
    return map;
  }, [fields]);

  const availSlotPatterns = useMemo(() => {
    const patterns = new Map();
    (availSlots || []).forEach((slot) => {
      const division = String(slot?.division || "").trim();
      const fieldKey = String(slot?.fieldKey || "").trim();
      const startTime = String(slot?.startTime || "").trim();
      const endTime = String(slot?.endTime || "").trim();
      const gameDate = String(slot?.gameDate || "").trim();
      const weekday = weekdayFromIso(gameDate);
      const key = [division, weekday, startTime, endTime, fieldKey].join("|");
      if (!patterns.has(key)) {
        patterns.set(key, {
          key,
          division,
          weekday,
          fieldKey,
          startTime,
          endTime,
          fieldLabel: String(slot?.displayName || "").trim() || fieldLabelMap.get(fieldKey) || fieldKey,
          count: 0,
          firstDate: gameDate,
          lastDate: gameDate,
          slots: [],
        });
      }
      const pattern = patterns.get(key);
      pattern.count += 1;
      pattern.slots.push(slot);
      if (!pattern.firstDate || (gameDate && gameDate < pattern.firstDate)) pattern.firstDate = gameDate;
      if (!pattern.lastDate || (gameDate && gameDate > pattern.lastDate)) pattern.lastDate = gameDate;
      if (!pattern.fieldLabel) {
        pattern.fieldLabel = String(slot?.displayName || "").trim() || fieldLabelMap.get(fieldKey) || fieldKey;
      }
    });

    return Array.from(patterns.values())
      .map((pattern) => {
        const slots = [...pattern.slots].sort((a, b) => {
          const dateCompare = String(a?.gameDate || "").localeCompare(String(b?.gameDate || ""));
          if (dateCompare !== 0) return dateCompare;
          return String(a?.slotId || "").localeCompare(String(b?.slotId || ""));
        });
        const existingDates = new Set(
          slots
            .map((slot) => String(slot?.gameDate || "").trim())
            .filter((value) => isIsoDate(value))
        );
        const interruptionDates = collectInterruptionDates(pattern.firstDate, pattern.lastDate, pattern.weekday, existingDates);
        return {
          ...pattern,
          slots,
          interruptionDates,
          interruptionCount: interruptionDates.length,
          durationMinutes: formatDurationMinutes(pattern.startTime, pattern.endTime),
        };
      })
      .sort((left, right) => {
        const leftDay = WEEKDAY_OPTIONS.indexOf(left.weekday);
        const rightDay = WEEKDAY_OPTIONS.indexOf(right.weekday);
        const weekdayIndex = (leftDay === -1 ? WEEKDAY_OPTIONS.length : leftDay) - (rightDay === -1 ? WEEKDAY_OPTIONS.length : rightDay);
        if (weekdayIndex !== 0) return weekdayIndex;
        const startCompare = left.startTime.localeCompare(right.startTime);
        if (startCompare !== 0) return startCompare;
        const fieldCompare = left.fieldLabel.localeCompare(right.fieldLabel);
        if (fieldCompare !== 0) return fieldCompare;
        return left.division.localeCompare(right.division);
      });
  }, [availSlots, fieldLabelMap]);

  const availPatternSummary = useMemo(() => ({
    slotCount: availSlots.length,
    patternCount: availSlotPatterns.length,
    interruptionCount: availSlotPatterns.reduce((total, pattern) => total + pattern.interruptionCount, 0),
  }), [availSlotPatterns, availSlots]);

  const availWeekdayColumns = useMemo(() => {
    const hasOther = availSlotPatterns.some((pattern) => pattern.weekday === "Other");
    return hasOther ? [...WEEKDAY_OPTIONS, "Other"] : WEEKDAY_OPTIONS;
  }, [availSlotPatterns]);

  const loadAvailabilitySlots = useCallback(async () => {
    setAvailListLoading(true);
    setAvailListErr("");
    setAvailListMsg("");
    const dateError = validateIsoDates([
      { label: "Date from", value: availDateFrom, required: false },
      { label: "Date to", value: availDateTo, required: false },
    ]);
    if (dateError) {
      setAvailListLoading(false);
      return setAvailListErr(dateError);
    }
    try {
      const qs = new URLSearchParams();
      if (availDivision) qs.set("division", availDivision);
      if (availDateFrom) qs.set("dateFrom", availDateFrom);
      if (availDateTo) qs.set("dateTo", availDateTo);
      if (availFieldKey) qs.set("fieldKey", availFieldKey);
      const data = await apiFetch(`/api/availability-slots?${qs.toString()}`);
      const filtered = extractSlotItems(data).filter((s) => s?.isAvailability);
      setAvailSlots(filtered);
      setAvailListMsg(filtered.length === 0
        ? "No availability slots found for this filter."
        : `Loaded ${filtered.length} availability slots.`);
    } catch (e) {
      setAvailListErr(formatApiError(e, "Failed to load availability slots."));
      setAvailSlots([]);
    } finally {
      setAvailListLoading(false);
    }
  }, [availDateFrom, availDateTo, availDivision, availFieldKey]);

  useEffect(() => {
    if (!leagueId || !availDateFrom || !availDateTo) return;
    loadAvailabilitySlots().catch(() => {});
  }, [leagueId, availDateFrom, availDateTo, loadAvailabilitySlots]);

  useEffect(() => {
    setAvailPatternDrafts((prev) => {
      const next = {};
      availSlotPatterns.forEach((pattern) => {
        const current = prev[pattern.key];
        next[pattern.key] = {
          startTime: current?.startTime ?? pattern.startTime,
          endTime: current?.endTime ?? pattern.endTime,
          fieldKey: current?.fieldKey ?? pattern.fieldKey,
        };
      });
      return next;
    });
    if (availExpandedPatternKey && !availSlotPatterns.some((pattern) => pattern.key === availExpandedPatternKey)) {
      setAvailExpandedPatternKey("");
    }
  }, [availSlotPatterns, availExpandedPatternKey]);

  function toggleDay(day) {
    setSlotGenDays((prev) => ({ ...prev, [day]: !prev[day] }));
  }

  async function previewSlotGeneration() {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Season start", value: dateFrom, required: true },
      { label: "Season end", value: dateTo, required: true },
    ]);
    if (dateError) return setErr(dateError);
    if (!selectedSlotGenFieldKeys.length) {
      return setErr("No fields available. Add fields before generating slots.");
    }
    setLoading(true);
    try {
      const days = Object.entries(slotGenDays)
        .filter(([, on]) => on)
        .map(([k]) => k);
      const combined = { slots: [], conflicts: [] };
      const failures = [];
      for (const fieldKey of selectedSlotGenFieldKeys) {
        try {
          const data = await apiFetch("/api/schedule/slots/preview", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              division: slotGenDivision,
              fieldKey,
              dateFrom,
              dateTo,
              daysOfWeek: days,
              startTime: slotGenStartTime,
              endTime: slotGenEndTime,
            }),
          });
          combined.slots.push(...(Array.isArray(data?.slots) ? data.slots : []));
          combined.conflicts.push(...(Array.isArray(data?.conflicts) ? data.conflicts : []));
        } catch (e) {
          failures.push({ fieldKey, message: e?.message || "Preview failed." });
        }
      }
      setSlotGenPreview(combined);
      if (failures.length) {
        const first = failures[0];
        setErr(`Preview completed with ${failures.length} field error(s). First: ${first.fieldKey} (${first.message})`);
      }
      trackEvent("ui_availability_slots_preview", {
        leagueId,
        division: slotGenDivision,
        fieldScope: slotGenFieldKey === ALL_FIELDS_VALUE ? "all" : "single",
        fieldCount: selectedSlotGenFieldKeys.length,
      });
    } catch (e) {
      setErr(e?.message || "Failed to preview slots");
      setSlotGenPreview(null);
    } finally {
      setLoading(false);
    }
  }

  async function applySlotGeneration(mode) {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Season start", value: dateFrom, required: true },
      { label: "Season end", value: dateTo, required: true },
    ]);
    if (dateError) return setErr(dateError);
    if (!selectedSlotGenFieldKeys.length) {
      return setErr("No fields available. Add fields before generating slots.");
    }
    setLoading(true);
    try {
      const days = Object.entries(slotGenDays)
        .filter(([, on]) => on)
        .map(([k]) => k);
      const summary = {
        created: [],
        overwritten: [],
        skipped: [],
        cleared: 0,
      };
      const failures = [];
      for (const fieldKey of selectedSlotGenFieldKeys) {
        try {
          const data = await apiFetch(`/api/schedule/slots/apply?mode=${encodeURIComponent(mode)}`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              division: slotGenDivision,
              fieldKey,
              dateFrom,
              dateTo,
              daysOfWeek: days,
              startTime: slotGenStartTime,
              endTime: slotGenEndTime,
            }),
          });
          summary.created.push(...(Array.isArray(data?.created) ? data.created : []));
          summary.overwritten.push(...(Array.isArray(data?.overwritten) ? data.overwritten : []));
          summary.skipped.push(...(Array.isArray(data?.skipped) ? data.skipped : []));
          summary.cleared += Number(data?.cleared || 0);
        } catch (e) {
          failures.push({ fieldKey, message: e?.message || "Generate failed." });
        }
      }
      setSlotGenPreview(null);
      const createdCount = summary.created.length;
      const overwrittenCount = summary.overwritten.length;
      const skippedCount = summary.skipped.length;
      const clearedCount = summary.cleared;
      const baseMessage = `Generated slots (${createdCount} created, ${overwrittenCount} overwritten, ${skippedCount} skipped, ${clearedCount} cleared).`;
      if (failures.length) {
        const first = failures[0];
        setErr(`Generation completed with ${failures.length} field error(s). First: ${first.fieldKey} (${first.message})`);
        setToast({ tone: "warning", message: baseMessage });
      } else {
        setToast({ tone: "success", message: baseMessage });
      }
      trackEvent("ui_availability_slots_generate", {
        leagueId,
        division: slotGenDivision,
        mode,
        created: createdCount,
        overwritten: overwrittenCount,
        skipped: skippedCount,
        cleared: clearedCount,
        fieldScope: slotGenFieldKey === ALL_FIELDS_VALUE ? "all" : "single",
        fieldCount: selectedSlotGenFieldKeys.length,
      });
    } catch (e) {
      setErr(e?.message || "Failed to generate slots");
    } finally {
      setLoading(false);
    }
  }

  async function importAvailabilityCsv() {
    setAvailErr("");
    setAvailOk("");
    setAvailErrors([]);
    setAvailWarnings([]);
    setAvailImportUnknowns([]);
    setAvailImportRows([]);
    setAvailImportFixes({});
    setAvailImportDefault("");
    if (!availFile) return setAvailErr("Choose a CSV file to upload.");

    setAvailBusy(true);
    try {
      const text = await availFile.text();
      const rows = parseCsvRows(text);
      if (rows.length < 2) {
        setAvailErr("No CSV rows found.");
        return;
      }
      const header = rows[0];
      const idx = buildHeaderIndex(header);
      if (!("division" in idx)) {
        setAvailErr("Missing required column: division.");
        return;
      }

      const knownDivisions = new Set(
        (divisions || [])
          .map((d) => (d?.code || "").trim())
          .filter(Boolean)
      );
      if (knownDivisions.size === 0) {
        setAvailErr("No divisions loaded. Add divisions before importing availability.");
        return;
      }
      const unknowns = new Set();
      for (let i = 1; i < rows.length; i += 1) {
        const raw = String(rows[i][idx.division] || "").trim();
        if (!raw) unknowns.add("(blank)");
        else if (!knownDivisions.has(raw)) unknowns.add(raw);
      }

      if (unknowns.size > 0) {
        const list = Array.from(unknowns);
        const defaultDivision = knownDivisions.size === 1 ? Array.from(knownDivisions)[0] : "";
        const fixes = {};
        list.forEach((key) => { fixes[key] = defaultDivision; });
        setAvailImportUnknowns(list);
        setAvailImportRows(rows);
        setAvailImportFixes(fixes);
        setAvailImportDefault(defaultDivision);
        setAvailErr("Division cleanup required before import.");
        return;
      }

      const fd = new FormData();
      fd.append("file", availFile);
      const res = await apiFetch("/api/import/availability-slots", { method: "POST", body: fd });
      setAvailOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      trackEvent("ui_availability_slots_import_success", {
        leagueId,
        upserted: res?.upserted ?? 0,
        rejected: res?.rejected ?? 0,
        skipped: res?.skipped ?? 0,
      });
      if (Array.isArray(res?.errors) && res.errors.length) setAvailErrors(res.errors);
      if (Array.isArray(res?.warnings) && res.warnings.length) setAvailWarnings(res.warnings);
    } catch (e) {
      setAvailErr(e?.message || "Import failed");
    } finally {
      setAvailBusy(false);
    }
  }

  async function applyAvailabilityDivisionFixes() {
    setAvailErr("");
    setAvailOk("");
    if (!availImportRows.length) return;
    const missing = availImportUnknowns.filter((u) => !availImportFixes[u]);
    if (missing.length > 0) {
      return setAvailErr("Choose a replacement division for all unknown values.");
    }

    setAvailBusy(true);
    try {
      const header = availImportRows[0];
      const idx = buildHeaderIndex(header);
      const rows = availImportRows.map((r, i) => {
        if (i === 0) return r;
        const next = [...r];
        const raw = String(next[idx.division] || "").trim();
        const key = raw || "(blank)";
        if (availImportFixes[key]) next[idx.division] = availImportFixes[key];
        return next;
      });
      const csv = rows.map((row) => row.map(csvEscape).join(",")).join("\n");

      const fd = new FormData();
      const name = availFile?.name || "availability.csv";
      fd.append("file", new Blob([csv], { type: "text/csv" }), name);
      const res = await apiFetch("/api/import/availability-slots", { method: "POST", body: fd });
      setAvailOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      trackEvent("ui_availability_slots_import_success", {
        leagueId,
        upserted: res?.upserted ?? 0,
        rejected: res?.rejected ?? 0,
        skipped: res?.skipped ?? 0,
        divisionFixes: true,
      });
      if (Array.isArray(res?.errors) && res.errors.length) setAvailErrors(res.errors);
      if (Array.isArray(res?.warnings) && res.warnings.length) setAvailWarnings(res.warnings);
      setAvailImportUnknowns([]);
      setAvailImportRows([]);
      setAvailImportFixes({});
    } catch (e) {
      setAvailErr(e?.message || "Import failed");
    } finally {
      setAvailBusy(false);
    }
  }

  function downloadAvailabilityTemplate() {
    const csv = buildAvailabilityTemplateCsv(divisions, fields);
    const safeLeague = (leagueId || "league").replace(/[^a-z0-9_-]+/gi, "_");
    downloadCsv(csv, `availability_template_${safeLeague}.csv`);
    trackEvent("ui_availability_slots_template_download", { leagueId });
  }

  async function deleteAvailabilitySlots() {
    const dateError = validateIsoDates([
      { label: "Date from", value: availDateFrom, required: true },
      { label: "Date to", value: availDateTo, required: true },
    ]);
    if (dateError) return setAvailListErr(dateError);
    const confirmPhrase = availDivision ? "DELETE AVAILABILITY" : "DELETE ALL AVAILABILITY";
    const confirmText = window.prompt(
      `Type ${confirmPhrase} to remove availability slots for the selected filters.`
    );
    if (confirmText !== confirmPhrase) return;

    setAvailListLoading(true);
    setAvailListErr("");
    try {
      const res = await apiFetch("/api/availability-slots/clear", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: availDivision,
          dateFrom: availDateFrom,
          dateTo: availDateTo,
          fieldKey: availFieldKey || undefined,
        }),
      });
      setAvailListMsg(`Deleted ${res?.deleted ?? 0} availability slots.`);
      await loadAvailabilitySlots();
    } catch (e) {
      setAvailListErr(e?.message || "Delete failed.");
    } finally {
      setAvailListLoading(false);
    }
  }

  async function deleteConflictingAvailabilitySlots() {
    const dateError = validateIsoDates([
      { label: "Date from", value: availDateFrom, required: true },
      { label: "Date to", value: availDateTo, required: true },
    ]);
    if (dateError) return setAvailListErr(dateError);

    const confirmText = window.prompt(
      "Type DELETE CONFLICTING AVAILABILITY to remove only availability slots that conflict with assigned slots or overlapping availability."
    );
    if (confirmText !== "DELETE CONFLICTING AVAILABILITY") return;

    setAvailListLoading(true);
    setAvailListErr("");
    setAvailListMsg("");
    try {
      const res = await apiFetch("/api/availability-slots/clear", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: availDivision || undefined,
          dateFrom: availDateFrom,
          dateTo: availDateTo,
          fieldKey: availFieldKey || undefined,
          conflictsOnly: true,
        }),
      });

      setAvailListMsg(
        `Deleted ${res?.deleted ?? 0} conflicting availability slot(s)` +
        ` (conflicts found: ${res?.conflictsFound ?? 0}, vs assigned: ${res?.conflictWithAssigned ?? 0}, vs availability: ${res?.conflictWithAvailability ?? 0}).`
      );
      await loadAvailabilitySlots();
    } catch (e) {
      setAvailListErr(formatApiError(e, "Conflict cleanup failed."));
    } finally {
      setAvailListLoading(false);
    }
  }

  function updateAvailPatternDraft(patternKey, patch) {
    setAvailPatternDrafts((prev) => ({
      ...prev,
      [patternKey]: {
        ...(prev[patternKey] || {}),
        ...patch,
      },
    }));
  }

  async function saveAvailabilityPattern(pattern) {
    const draft = availPatternDrafts[pattern.key] || {
      startTime: pattern.startTime,
      endTime: pattern.endTime,
      fieldKey: pattern.fieldKey,
    };
    const startMinutes = parseMinutes(draft.startTime);
    const endMinutes = parseMinutes(draft.endTime);
    if (startMinutes == null || endMinutes == null) {
      return setAvailListErr("Recurring availability changes require valid HH:MM start and end times.");
    }
    if (endMinutes <= startMinutes) {
      return setAvailListErr("Recurring availability end time must be later than the start time.");
    }
    if (!draft.fieldKey) {
      return setAvailListErr("Select a field before saving recurring availability changes.");
    }

    const isUnchanged =
      draft.startTime === pattern.startTime &&
      draft.endTime === pattern.endTime &&
      draft.fieldKey === pattern.fieldKey;
    if (isUnchanged) {
      return setAvailListMsg("No recurring changes to save for this pattern.");
    }

    setAvailPatternSavingKey(pattern.key);
    setAvailListErr("");
    setAvailListMsg("");
    let updatedCount = 0;
    try {
      for (const slot of pattern.slots) {
        await apiFetch(`/api/slots/${encodeURIComponent(slot.division)}/${encodeURIComponent(slot.slotId)}`, {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            gameDate: slot.gameDate,
            startTime: draft.startTime,
            endTime: draft.endTime,
            fieldKey: draft.fieldKey,
          }),
        });
        updatedCount += 1;
      }
      trackEvent("ui_availability_slots_pattern_update", {
        leagueId,
        division: pattern.division,
        fieldKey: pattern.fieldKey,
        slotCount: updatedCount,
      });
      await loadAvailabilitySlots();
      setAvailListMsg(`Updated ${updatedCount} recurring availability slot(s).`);
      setToast({ tone: "success", message: `Updated ${updatedCount} recurring availability slot(s).` });
    } catch (e) {
      const prefix = updatedCount > 0 ? `Updated ${updatedCount} slot(s) before the save stopped. ` : "";
      await loadAvailabilitySlots();
      setAvailListErr(`${prefix}${formatApiError(e, "Failed to update recurring availability.")}`);
    } finally {
      setAvailPatternSavingKey("");
    }
  }

  async function removeAvailabilityOccurrence(slot) {
    if (!slot?.slotId || !slot?.division) return;
    const confirmMessage = `Remove availability on ${slot.gameDate} at ${slot.startTime}-${slot.endTime}?`;
    if (!window.confirm(confirmMessage)) return;

    setAvailOccurrenceSavingKey(slot.slotId);
    setAvailListErr("");
    setAvailListMsg("");
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(slot.division)}/${encodeURIComponent(slot.slotId)}/cancel`, {
        method: "PATCH",
      });
      trackEvent("ui_availability_slots_occurrence_remove", {
        leagueId,
        division: slot.division,
        slotId: slot.slotId,
      });
      await loadAvailabilitySlots();
      setAvailListMsg(`Removed availability on ${slot.gameDate}.`);
      setToast({ tone: "success", message: `Removed availability on ${slot.gameDate}.` });
    } catch (e) {
      setAvailListErr(formatApiError(e, "Failed to remove availability occurrence."));
    } finally {
      setAvailOccurrenceSavingKey("");
    }
  }

  async function removeAvailabilityPattern(pattern) {
    if (!pattern?.slots?.length) return;
    const confirmMessage =
      `Remove this recurring pattern for ${pattern.weekday} ${pattern.startTime}-${pattern.endTime} ` +
      `on ${pattern.fieldLabel} (${pattern.count} occurrence${pattern.count === 1 ? "" : "s"})?`;
    if (!window.confirm(confirmMessage)) return;

    setAvailPatternRemovingKey(pattern.key);
    setAvailListErr("");
    setAvailListMsg("");
    let removedCount = 0;
    try {
      for (const slot of pattern.slots) {
        await apiFetch(`/api/slots/${encodeURIComponent(slot.division)}/${encodeURIComponent(slot.slotId)}/cancel`, {
          method: "PATCH",
        });
        removedCount += 1;
      }
      trackEvent("ui_availability_slots_pattern_remove", {
        leagueId,
        division: pattern.division,
        fieldKey: pattern.fieldKey,
        slotCount: removedCount,
      });
      await loadAvailabilitySlots();
      setAvailListMsg(`Removed ${removedCount} recurring availability slot(s).`);
      setToast({ tone: "success", message: `Removed ${removedCount} recurring availability slot(s).` });
    } catch (e) {
      const prefix = removedCount > 0 ? `Removed ${removedCount} slot(s) before the delete stopped. ` : "";
      await loadAvailabilitySlots();
      setAvailListErr(`${prefix}${formatApiError(e, "Failed to remove recurring availability.")}`);
    } finally {
      setAvailPatternRemovingKey("");
    }
  }

  return (
    <div className="stack">
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />
      {err ? <div className="callout callout--error">{err}</div> : null}

      <div className="card">
        <div className="card__header">
          <div className="h2">Availability CSV import</div>
          <div className="subtle">
            Upload bulk availability slots. Required columns: <code>division</code>, <code>gameDate</code>, <code>startTime</code>, <code>endTime</code>, <code>fieldKey</code>. GameDate should be <code>YYYY-MM-DD</code> (M/D/YYYY is accepted and normalized).
          </div>
        </div>
        <div className="card__body">
          {availErr ? <div className="callout callout--error">{availErr}</div> : null}
          {availOk ? <div className="callout callout--ok">{availOk}</div> : null}
          <div className="row items-end gap-3">
            <label className="flex-1">
              CSV file
              <input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => {
                  setAvailFile(e.target.files?.[0] || null);
                  setAvailImportUnknowns([]);
                  setAvailImportRows([]);
                  setAvailImportFixes({});
                  setAvailImportDefault("");
                }}
                disabled={availBusy}
              />
            </label>
            <button className="btn" onClick={importAvailabilityCsv} disabled={availBusy || !availFile}>
              {availBusy ? "Importing..." : "Upload & Import"}
            </button>
            <button className="btn btn--ghost" onClick={downloadAvailabilityTemplate}>
              Download CSV template
            </button>
          </div>
          {availImportUnknowns.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Division cleanup</div>
              <div className="subtle mb-2">
                Map missing/unknown divisions to a valid league division before importing.
              </div>
              <div className="row row--wrap gap-2 items-center mb-2">
                <span className="muted">Apply to all</span>
                <select
                  value={availImportDefault}
                  onChange={(e) => {
                    const next = e.target.value;
                    setAvailImportDefault(next);
                    setAvailImportFixes((prev) => {
                      const updated = { ...prev };
                      availImportUnknowns.forEach((key) => { updated[key] = next; });
                      return updated;
                    });
                  }}
                >
                  <option value="">Select division</option>
                  {divisions.map((d) => {
                    const code = d.code || "";
                    if (!code) return null;
                    return (
                      <option key={code} value={code}>
                        {d.name ? `${d.name} (${code})` : code}
                      </option>
                    );
                  })}
                </select>
              </div>
              <div className="stack gap-2">
                {availImportUnknowns.map((value) => (
                  <label key={value} className="row row--wrap gap-2 items-center">
                    <span className="muted">{value === "(blank)" ? "Blank division" : value}</span>
                    <select
                      value={availImportFixes[value] || ""}
                      onChange={(e) => setAvailImportFixes((prev) => ({ ...prev, [value]: e.target.value }))}
                    >
                      <option value="">Select division</option>
                      {divisions.map((d) => {
                        const code = d.code || "";
                        if (!code) return null;
                        return (
                          <option key={code} value={code}>
                            {d.name ? `${d.name} (${code})` : code}
                          </option>
                        );
                      })}
                    </select>
                  </label>
                ))}
              </div>
              <div className="row gap-2 mt-2">
                <button className="btn btn--primary" onClick={applyAvailabilityDivisionFixes} disabled={availBusy}>
                  Apply mapping & import
                </button>
                <button
                  className="btn btn--ghost"
                  onClick={() => {
                    setAvailImportUnknowns([]);
                    setAvailImportRows([]);
                    setAvailImportFixes({});
                  }}
                >
                  Cancel cleanup
                </button>
              </div>
            </div>
          ) : null}
          {availErrors.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Rejected rows ({availErrors.length})</div>
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Error</th>
                  </tr>
                </thead>
                <tbody>
                  {availErrors.slice(0, 50).map((x, idx) => (
                    <tr key={idx}>
                      <td>{x.row}</td>
                      <td>{x.error}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {availErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
          {availWarnings.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Warnings ({availWarnings.length})</div>
              <div className="subtle mb-2">
                These rows were skipped because they overlapped existing slots or duplicates in the CSV.
              </div>
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Warning</th>
                  </tr>
                </thead>
                <tbody>
                  {availWarnings.slice(0, 50).map((x, idx) => (
                    <tr key={idx}>
                      <td>{x.row}</td>
                      <td>{x.warning}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {availWarnings.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Availability slots</div>
          <div className="subtle">Review recurring patterns, make bulk edits, and remove one-off interruptions.</div>
        </div>
        <div className="card__body">
          {availListErr ? <div className="callout callout--error">{availListErr}</div> : null}
          {availListMsg ? <div className="callout callout--ok">{availListMsg}</div> : null}
        </div>
        <div className="card__body grid2">
          <label>
            Division
            <select value={availDivision} onChange={(e) => setAvailDivision(e.target.value)}>
              <option value="">All divisions</option>
              {divisions.map((d) => (
                <option key={d.code || ""} value={d.code || ""}>
                  {d.name ? `${d.name} (${d.code || ""})` : d.code || ""}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field (optional)
            <select value={availFieldKey} onChange={(e) => setAvailFieldKey(e.target.value)}>
              <option value="">All fields</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName}
                </option>
              ))}
            </select>
          </label>
          <label>
            Date from
            <input type="date" value={availDateFrom} onChange={(e) => setAvailDateFrom(e.target.value)} />
          </label>
          <label>
            Date to
            <input type="date" value={availDateTo} onChange={(e) => setAvailDateTo(e.target.value)} />
          </label>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={loadAvailabilitySlots} disabled={availListLoading}>
            {availListLoading ? "Loading..." : "Load availability slots"}
          </button>
          <button className="btn" onClick={deleteConflictingAvailabilitySlots} disabled={availListLoading}>
            Delete conflicting availability
          </button>
          <button className="btn btn--danger" onClick={deleteAvailabilitySlots} disabled={availListLoading}>
            Delete filtered availability
          </button>
        </div>
        {availSlots.length ? (
          <>
            <div className="card__body">
              <div className="callout">
                <div className="font-bold mb-1">Recurring availability view</div>
                <div className="subtle">
                  Slots are grouped by division, weekday, time, and field. Missing dates between the first and last occurrence are treated as
                  interruptions, so recurring patterns stay easy to adjust while one-off gaps stay visible.
                </div>
              </div>
            </div>
            <div className="card__body row row--wrap gap-3">
              <div className="layoutStat">
                <div className="layoutStat__value">{availPatternSummary.slotCount}</div>
                <div className="layoutStat__label">Slots</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{availPatternSummary.patternCount}</div>
                <div className="layoutStat__label">Recurring patterns</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{availPatternSummary.interruptionCount}</div>
                <div className="layoutStat__label">Interruptions</div>
              </div>
            </div>
            <div className="card__body seasonWeekGrid">
              {availWeekdayColumns.map((day) => {
                const dayPatterns = availSlotPatterns.filter((pattern) => pattern.weekday === day);
                return (
                  <div key={day} className="card seasonWeekColumn">
                    <div className="card__header seasonWeekColumn__header">
                      <div className="h4">{day}</div>
                    </div>
                    <div className="card__body stack gap-2 seasonWeekColumn__body">
                      {!dayPatterns.length ? (
                        <div className="subtle">No recurring patterns</div>
                      ) : (
                        dayPatterns.map((pattern) => {
                          const draft = availPatternDrafts[pattern.key] || {
                            startTime: pattern.startTime,
                            endTime: pattern.endTime,
                            fieldKey: pattern.fieldKey,
                          };
                          const isExpanded = availExpandedPatternKey === pattern.key;
                          const isPatternBusy =
                            availPatternSavingKey === pattern.key || availPatternRemovingKey === pattern.key;
                          const patternToneClass = pattern.interruptionCount > 0
                            ? "availabilityPatternCard--warning"
                            : "availabilityPatternCard--steady";
                          return (
                            <div
                              key={pattern.key}
                              className={`callout availabilityPatternCard ${patternToneClass}`}
                            >
                              <div className="row row--between gap-2 availabilityPatternCard__top">
                                <div className="availabilityPatternCard__time">
                                  <b>{pattern.startTime}-{pattern.endTime}</b>
                                </div>
                                <div className="row row--wrap gap-1">
                                  <span className="pill">Count: {pattern.count}</span>
                                  <span className="pill">Interruptions: {pattern.interruptionCount}</span>
                                  {pattern.durationMinutes ? <span className="pill">{pattern.durationMinutes} min</span> : null}
                                </div>
                              </div>
                              <div className="availabilityPatternCard__meta">
                                <div className="subtle">{pattern.fieldLabel}</div>
                                <div className="subtle">Division: {pattern.division || "-"}</div>
                                <div className="subtle">
                                  Span: {pattern.firstDate || "-"} to {pattern.lastDate || "-"}
                                </div>
                              </div>
                              <div className="grid2 mt-2">
                                <label>
                                  Start time
                                  <input
                                    type="time"
                                    aria-label={`Start time for ${pattern.weekday} ${pattern.fieldKey} ${pattern.division}`}
                                    value={draft.startTime}
                                    onChange={(e) => updateAvailPatternDraft(pattern.key, { startTime: e.target.value })}
                                    disabled={isPatternBusy}
                                  />
                                </label>
                                <label>
                                  End time
                                  <input
                                    type="time"
                                    aria-label={`End time for ${pattern.weekday} ${pattern.fieldKey} ${pattern.division}`}
                                    value={draft.endTime}
                                    onChange={(e) => updateAvailPatternDraft(pattern.key, { endTime: e.target.value })}
                                    disabled={isPatternBusy}
                                  />
                                </label>
                                <label className="col-span-2">
                                  Field
                                  <select
                                    aria-label={`Field for ${pattern.weekday} ${pattern.fieldKey} ${pattern.division}`}
                                    value={draft.fieldKey}
                                    onChange={(e) => updateAvailPatternDraft(pattern.key, { fieldKey: e.target.value })}
                                    disabled={isPatternBusy}
                                  >
                                    {fields.map((field) => (
                                      <option key={field.fieldKey} value={field.fieldKey}>
                                        {field.displayName || field.fieldKey}
                                      </option>
                                    ))}
                                  </select>
                                </label>
                              </div>
                              <div className="row row--wrap gap-2 mt-2">
                                <button
                                  className="btn btn--primary btn--sm"
                                  type="button"
                                  onClick={() => saveAvailabilityPattern(pattern)}
                                  disabled={isPatternBusy}
                                >
                                  {availPatternSavingKey === pattern.key ? "Saving..." : "Save recurring changes"}
                                </button>
                                <button
                                  className="btn btn--danger btn--sm"
                                  type="button"
                                  onClick={() => removeAvailabilityPattern(pattern)}
                                  disabled={isPatternBusy}
                                >
                                  {availPatternRemovingKey === pattern.key ? "Removing..." : "Remove recurring pattern"}
                                </button>
                                <button
                                  className="btn btn--ghost btn--sm"
                                  type="button"
                                  onClick={() => setAvailExpandedPatternKey(isExpanded ? "" : pattern.key)}
                                  disabled={availPatternRemovingKey === pattern.key}
                                >
                                  {isExpanded ? "Hide details" : "Occurrences & interruptions"}
                                </button>
                              </div>
                              {isExpanded ? (
                                <div className="mt-2 stack gap-2">
                                  <div>
                                    <div className="font-bold mb-1">Interruption dates</div>
                                    {pattern.interruptionDates.length ? (
                                      <div className="row row--wrap gap-1">
                                        {pattern.interruptionDates.map((date) => (
                                          <span key={date} className="pill">{date}</span>
                                        ))}
                                      </div>
                                    ) : (
                                      <div className="subtle">No interruptions between {pattern.firstDate} and {pattern.lastDate}.</div>
                                    )}
                                  </div>
                                  <div>
                                    <div className="font-bold mb-1">Occurrences</div>
                                    <div className="tableWrap">
                                      <table className="table">
                                        <thead>
                                          <tr>
                                            <th>Date</th>
                                            <th>Time</th>
                                            <th>Actions</th>
                                          </tr>
                                        </thead>
                                        <tbody>
                                          {pattern.slots.slice(0, 12).map((slot) => (
                                            <tr key={slot.slotId}>
                                              <td>{slot.gameDate}</td>
                                              <td>{slot.startTime}-{slot.endTime}</td>
                                              <td>
                                                <button
                                                  className="btn btn--ghost"
                                                  type="button"
                                                  onClick={() => removeAvailabilityOccurrence(slot)}
                                                  disabled={availOccurrenceSavingKey === slot.slotId}
                                                >
                                                  {availOccurrenceSavingKey === slot.slotId ? "Removing..." : `Remove ${slot.gameDate}`}
                                                </button>
                                              </td>
                                            </tr>
                                          ))}
                                        </tbody>
                                      </table>
                                      {pattern.slots.length > 12 ? (
                                        <div className="subtle">Showing first 12 occurrences. Adjust the filter range to work in smaller windows.</div>
                                      ) : null}
                                    </div>
                                  </div>
                                </div>
                              ) : null}
                            </div>
                          );
                        })
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </>
        ) : !availListLoading ? (
          <div className="card__body muted">No availability slots loaded for this filter.</div>
        ) : null}
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Field slot generator</div>
          <div className="subtle">
            Generate open availability slots for a division across one field or all fields. Uses season game length (no buffers).
          </div>
        </div>
        {leagueSeason && !leagueSeason.gameLengthMinutes ? (
          <div className="card__body">
            <div className="callout callout--error">
              Season game length is not set. Update league or division season settings.
            </div>
          </div>
        ) : null}
        <div className="card__body grid2">
          <label>
            Division
            <select value={slotGenDivision} onChange={(e) => setSlotGenDivision(e.target.value)}>
              {divisions.map((d) => (
                <option key={d.code || ""} value={d.code || ""}>
                  {d.name ? `${d.name} (${d.code || ""})` : d.code || ""}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field
            <select value={slotGenFieldKey} onChange={(e) => setSlotGenFieldKey(e.target.value)}>
              <option value={ALL_FIELDS_VALUE}>All fields</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName}
                </option>
              ))}
            </select>
          </label>
          <label>
            Start time
            <input value={slotGenStartTime} onChange={(e) => setSlotGenStartTime(e.target.value)} placeholder="17:00" />
          </label>
          <label>
            End time
            <input value={slotGenEndTime} onChange={(e) => setSlotGenEndTime(e.target.value)} placeholder="22:00" />
          </label>
          <label>
            Season range
            <div className="row gap-2 row--wrap">
              <input value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
              <input value={dateTo} onChange={(e) => setDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
            </div>
          </label>
          <label>
            Game length (minutes)
            <input value={leagueSeason?.gameLengthMinutes || 0} readOnly />
          </label>
          <div>
            <div className="mb-2">Days of week</div>
            <div className="row row--wrap gap-2">
              {Object.keys(slotGenDays).map((day) => (
                <label key={day} className="inlineCheck">
                  <input type="checkbox" checked={slotGenDays[day]} onChange={() => toggleDay(day)} />
                  {day}
                </label>
              ))}
            </div>
          </div>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={previewSlotGeneration} disabled={loading || !slotGenDivision || selectedSlotGenFieldKeys.length === 0}>
            Preview slots
          </button>
          <button className="btn btn--primary" onClick={() => applySlotGeneration("skip")} disabled={loading || !slotGenDivision || selectedSlotGenFieldKeys.length === 0}>
            Generate (skip conflicts)
          </button>
          <button className="btn" onClick={() => applySlotGeneration("overwrite")} disabled={loading || !slotGenDivision || selectedSlotGenFieldKeys.length === 0}>
            Generate (overwrite availability)
          </button>
          <button className="btn" onClick={() => applySlotGeneration("regenerate")} disabled={loading || !slotGenDivision || selectedSlotGenFieldKeys.length === 0}>
            Regenerate
          </button>
        </div>
        {slotGenPreview ? (
          <div className="card__body">
            <div className="h2">Preview</div>
            <div className="row row--wrap gap-4">
              <div className="layoutStat">
                <div className="layoutStat__value">{slotGenPreview.slots?.length || 0}</div>
                <div className="layoutStat__label">Slots</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{slotGenPreview.conflicts?.length || 0}</div>
                <div className="layoutStat__label">Conflicts</div>
              </div>
            </div>
            {slotGenPreview.conflicts?.length ? (
              <div className="mt-3 tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Time</th>
                      <th>Field</th>
                      <th>Division</th>
                    </tr>
                  </thead>
                  <tbody>
                    {slotGenPreview.conflicts.slice(0, 20).map((c, idx) => (
                      <tr key={`${c.gameDate}-${c.startTime}-${idx}`}>
                        <td>{c.gameDate}</td>
                        <td>{c.startTime}-{c.endTime}</td>
                        <td>{c.fieldKey}</td>
                        <td>{c.division}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {slotGenPreview.conflicts.length > 20 ? (
                  <div className="subtle">Showing first 20 conflicts.</div>
                ) : null}
              </div>
            ) : null}
          </div>
        ) : null}
      </div>
    </div>
  );
}
