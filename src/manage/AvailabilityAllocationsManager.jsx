import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import { getDefaultRangeFallback, getSeasonRange } from "../lib/season";
import { trackEvent } from "../lib/telemetry";
import Toast from "../components/Toast";

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

function buildAllocationTemplateCsv(divisions, fields) {
  const header = [
    "division",
    "fieldKey",
    "dateFrom",
    "dateTo",
    "daysOfWeek",
    "startTime",
    "endTime",
    "slotType",
    "priorityRank",
    "notes",
    "isActive",
    "parkName",
    "fieldName",
    "displayName",
  ];
  const divs = (divisions || [])
    .filter((d) => d && d.isActive !== false)
    .map((d) => (typeof d === "string" ? d : d.code || d.division || ""))
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b));
  const scopes = ["LEAGUE", ...divs];
  const rows = [];
  for (const scope of scopes) {
    for (const f of fields || []) {
      if (!f?.fieldKey) continue;
      rows.push([
        scope,
        f.fieldKey,
        "",
        "",
        "",
        "",
        "",
        "practice",
        "",
        "",
        "true",
        f.parkName || "",
        f.fieldName || "",
        f.displayName || "",
      ]);
    }
  }
  return [header, ...rows].map((row) => row.map(csvEscape).join(",")).join("\n");
}

const DAY_OPTIONS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

function createManualDays() {
  const next = {};
  for (const day of DAY_OPTIONS) {
    next[day] = {
      enabled: false,
      startTime: "",
      endTime: "",
      slotType: "practice",
      priorityRank: "",
      notes: "",
    };
  }
  return next;
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

export default function AvailabilityAllocationsManager({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [fields, setFields] = useState([]);
  const [leagueSeason, setLeagueSeason] = useState(null);
  const [toast, setToast] = useState(null);

  const [allocFile, setAllocFile] = useState(null);
  const [allocBusy, setAllocBusy] = useState(false);
  const [allocErr, setAllocErr] = useState("");
  const [allocOk, setAllocOk] = useState("");
  const [allocErrors, setAllocErrors] = useState([]);
  const [allocWarnings, setAllocWarnings] = useState([]);

  const [allocations, setAllocations] = useState([]);
  const [allocScope, setAllocScope] = useState("");
  const [allocFieldKey, setAllocFieldKey] = useState("");
  const [allocDateFrom, setAllocDateFrom] = useState("");
  const [allocDateTo, setAllocDateTo] = useState("");
  const [allocListLoading, setAllocListLoading] = useState(false);

  const [manualScope, setManualScope] = useState("LEAGUE");
  const [manualFieldKey, setManualFieldKey] = useState("");
  const [manualDateFrom, setManualDateFrom] = useState("");
  const [manualDateTo, setManualDateTo] = useState("");
  const [manualIsActive, setManualIsActive] = useState(true);
  const [manualDays, setManualDays] = useState(createManualDays);
  const [manualBusy, setManualBusy] = useState(false);
  const [manualBlackouts, setManualBlackouts] = useState([]);
  const [manualBlackoutBusy, setManualBlackoutBusy] = useState(false);

  const [genDivision, setGenDivision] = useState("");
  const [genFieldKey, setGenFieldKey] = useState("");
  const [genDateFrom, setGenDateFrom] = useState("");
  const [genDateTo, setGenDateTo] = useState("");
  const [genPreview, setGenPreview] = useState(null);
  const [genLoading, setGenLoading] = useState(false);
  const [genStatus, setGenStatus] = useState("");

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      setAllocErr("");
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
          setGenDivision((prev) => prev || list[0].code || list[0].division || "");
        }
      } catch (e) {
        setAllocErr(formatApiError(e, "Failed to load divisions/fields."));
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

  useEffect(() => {
    if (!allocDateFrom) setAllocDateFrom(seasonRange.from);
    if (!allocDateTo) setAllocDateTo(seasonRange.to);
  }, [seasonRange, allocDateFrom, allocDateTo]);

  useEffect(() => {
    if (!genDateFrom) setGenDateFrom(seasonRange.from);
    if (!genDateTo) setGenDateTo(seasonRange.to);
  }, [seasonRange, genDateFrom, genDateTo]);

  useEffect(() => {
    if (!manualDateFrom) setManualDateFrom(seasonRange.from);
    if (!manualDateTo) setManualDateTo(seasonRange.to);
  }, [seasonRange, manualDateFrom, manualDateTo]);

  useEffect(() => {
    if (!genDivision && divisions.length) {
      setGenDivision(divisions[0].code || divisions[0].division || "");
    }
  }, [divisions, genDivision]);

  useEffect(() => {
    if (!manualFieldKey && fields.length) {
      setManualFieldKey(fields[0].fieldKey || "");
    }
  }, [fields, manualFieldKey]);

  useEffect(() => {
    if (!manualFieldKey) {
      setManualBlackouts([]);
      return;
    }
    const selected = fields.find((f) => f.fieldKey === manualFieldKey);
    setManualBlackouts(Array.isArray(selected?.blackouts) ? selected.blackouts : []);
  }, [manualFieldKey, fields]);

  function downloadTemplate() {
    const csv = buildAllocationTemplateCsv(divisions, fields);
    const safeLeague = (leagueId || "league").replace(/[^a-z0-9_-]+/gi, "_");
    downloadCsv(csv, `availability_allocations_${safeLeague}.csv`);
    trackEvent("ui_availability_allocations_template_download", { leagueId });
  }

  function updateManualDay(day, patch) {
    setManualDays((prev) => ({
      ...prev,
      [day]: {
        ...(prev[day] || {}),
        ...patch,
      },
    }));
  }

  function resetManualDays() {
    setManualDays(createManualDays());
  }

  function normalizeManualSlotType(raw) {
    const key = String(raw || "").trim().toLowerCase();
    if (key === "game" || key === "both") return key;
    return "practice";
  }

  async function addManualAllocations() {
    setAllocErr("");
    setAllocOk("");
    setAllocErrors([]);
    setAllocWarnings([]);

    if (!manualFieldKey) return setAllocErr("Select a field.");
    const dateError = validateIsoDates([
      { label: "Date from", value: manualDateFrom, required: true },
      { label: "Date to", value: manualDateTo, required: true },
    ]);
    if (dateError) return setAllocErr(dateError);
    if (manualDateTo < manualDateFrom) return setAllocErr("Date to must be on or after date from.");

    const selectedDays = selectedManualDays;
    if (selectedDays.length === 0) return setAllocErr("Enable at least one weekday section.");

    for (const day of selectedDays) {
      const row = manualDays[day] || {};
      if (!row.startTime || !row.endTime) {
        return setAllocErr(`${day}: start and end time are required.`);
      }
      const slotType = normalizeManualSlotType(row.slotType);
      if (slotType !== "practice") {
        const priorityRaw = String(row.priorityRank || "").trim();
        if (priorityRaw && (!/^\d+$/.test(priorityRaw) || Number(priorityRaw) <= 0)) {
          return setAllocErr(`${day}: priority rank must be a positive whole number.`);
        }
      }
    }

    setManualBusy(true);
    try {
      const selectedField = fields.find((f) => f.fieldKey === manualFieldKey);
      const header = [
        "division",
        "fieldKey",
        "dateFrom",
        "dateTo",
        "daysOfWeek",
        "startTime",
        "endTime",
        "slotType",
        "priorityRank",
        "notes",
        "isActive",
        "parkName",
        "fieldName",
        "displayName",
      ];
      const rows = selectedDays.map((day) => {
        const row = manualDays[day] || {};
        const slotType = normalizeManualSlotType(row.slotType);
        const priorityRank = slotType === "practice" ? "" : String(row.priorityRank || "").trim();
        return [
          manualScope || "LEAGUE",
          manualFieldKey,
          manualDateFrom,
          manualDateTo,
          day,
          String(row.startTime || "").trim(),
          String(row.endTime || "").trim(),
          slotType,
          priorityRank,
          String(row.notes || "").trim(),
          manualIsActive ? "true" : "false",
          selectedField?.parkName || "",
          selectedField?.fieldName || "",
          selectedField?.displayName || "",
        ];
      });
      const csv = [header, ...rows].map((row) => row.map(csvEscape).join(",")).join("\n");

      const res = await apiFetch("/api/import/availability-allocations", {
        method: "POST",
        headers: { "Content-Type": "text/csv" },
        body: csv,
      });
      setAllocOk(`Added allocations. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      setToast({ tone: "success", message: "Manual allocations saved." });
      trackEvent("ui_availability_allocations_manual_add", {
        leagueId,
        scope: manualScope || "LEAGUE",
        fieldKey: manualFieldKey,
        days: selectedDays.length,
        upserted: res?.upserted ?? 0,
        rejected: res?.rejected ?? 0,
        skipped: res?.skipped ?? 0,
      });
      if (Array.isArray(res?.errors) && res.errors.length) setAllocErrors(res.errors);
      if (Array.isArray(res?.warnings) && res.warnings.length) setAllocWarnings(res.warnings);
      await loadAllocations();
    } catch (e) {
      setAllocErr(formatApiError(e, "Manual allocation save failed."));
    } finally {
      setManualBusy(false);
    }
  }

  function updateManualBlackout(index, key, value) {
    setManualBlackouts((prev) => prev.map((item, i) => (i === index ? { ...item, [key]: value } : item)));
  }

  function addManualBlackout() {
    setManualBlackouts((prev) => [...prev, { startDate: "", endDate: "", label: "" }]);
  }

  function removeManualBlackout(index) {
    setManualBlackouts((prev) => prev.filter((_, i) => i !== index));
  }

  async function saveManualFieldBlackouts() {
    setAllocErr("");
    if (!manualFieldKey) return setAllocErr("Select a field to save blackouts.");
    const [parkCode, fieldCode] = manualFieldKey.split("/");
    if (!parkCode || !fieldCode) return setAllocErr("Invalid field key.");

    const dateFields = (manualBlackouts || []).flatMap((b, idx) => ([
      { label: `Blackout ${idx + 1} start`, value: b.startDate, required: !!b.endDate },
      { label: `Blackout ${idx + 1} end`, value: b.endDate, required: !!b.startDate },
    ]));
    const dateError = validateIsoDates(dateFields);
    if (dateError) return setAllocErr(dateError);
    for (let i = 0; i < (manualBlackouts || []).length; i += 1) {
      const b = manualBlackouts[i] || {};
      const from = String(b.startDate || "").trim();
      const to = String(b.endDate || "").trim();
      if (from && to && to < from) {
        return setAllocErr(`Blackout ${i + 1} end must be on or after start.`);
      }
    }

    setManualBlackoutBusy(true);
    try {
      const updated = await apiFetch(`/api/fields/${encodeURIComponent(parkCode)}/${encodeURIComponent(fieldCode)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ blackouts: manualBlackouts }),
      });
      const nextBlackouts = Array.isArray(updated?.blackouts) ? updated.blackouts : [];
      setManualBlackouts(nextBlackouts);
      setFields((prev) => prev.map((f) => (f.fieldKey === manualFieldKey ? { ...f, blackouts: nextBlackouts } : f)));
      setToast({ tone: "success", message: "Field blackouts saved." });
    } catch (e) {
      setAllocErr(formatApiError(e, "Failed to save field blackouts."));
    } finally {
      setManualBlackoutBusy(false);
    }
  }

  async function importAllocationsCsv() {
    setAllocErr("");
    setAllocOk("");
    setAllocErrors([]);
    setAllocWarnings([]);
    if (!allocFile) return setAllocErr("Choose a CSV file to upload.");

    setAllocBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", allocFile);
      const res = await apiFetch("/api/import/availability-allocations", { method: "POST", body: fd });
      setAllocOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      setToast({ tone: "success", message: "Availability allocations imported." });
      trackEvent("ui_availability_allocations_import_success", {
        leagueId,
        upserted: res?.upserted ?? 0,
        rejected: res?.rejected ?? 0,
        skipped: res?.skipped ?? 0,
      });
      if (Array.isArray(res?.errors) && res.errors.length) setAllocErrors(res.errors);
      if (Array.isArray(res?.warnings) && res.warnings.length) setAllocWarnings(res.warnings);
    } catch (e) {
      setAllocErr(formatApiError(e, "Import failed"));
    } finally {
      setAllocBusy(false);
    }
  }

  async function loadAllocations() {
    setAllocErr("");
    setAllocOk("");
    setAllocListLoading(true);
    const dateError = validateIsoDates([
      { label: "Date from", value: allocDateFrom, required: false },
      { label: "Date to", value: allocDateTo, required: false },
    ]);
    if (dateError) {
      setAllocListLoading(false);
      return setAllocErr(dateError);
    }
    try {
      const qs = new URLSearchParams();
      if (allocScope) qs.set("division", allocScope);
      if (allocFieldKey) qs.set("fieldKey", allocFieldKey);
      if (allocDateFrom) qs.set("dateFrom", allocDateFrom);
      if (allocDateTo) qs.set("dateTo", allocDateTo);
      const data = await apiFetch(`/api/availability/allocations?${qs.toString()}`);
      setAllocations(Array.isArray(data) ? data : []);
      if (!data || data.length === 0) setAllocOk("No allocations found for this filter.");
    } catch (e) {
      setAllocErr(formatApiError(e, "Failed to load allocations."));
      setAllocations([]);
    } finally {
      setAllocListLoading(false);
    }
  }

  async function clearAllocations() {
    const dateError = validateIsoDates([
      { label: "Date from", value: allocDateFrom, required: true },
      { label: "Date to", value: allocDateTo, required: true },
    ]);
    if (dateError) return setAllocErr(dateError);
    setAllocOk("");
    const targetLabel = allocScope
      ? (allocScope === "LEAGUE" ? "league-wide allocations" : `${allocScope} allocations`)
      : "allocations across all scopes";
    const expectedText = allocScope ? "DELETE ALLOCATIONS" : "DELETE ALL ALLOCATIONS";
    const confirmText = window.prompt(
      `Type ${expectedText} to remove ${targetLabel} for the selected date range.`
    );
    if (confirmText == null) return;
    const normalized = confirmText.trim().toUpperCase();
    const accepted = allocScope
      ? new Set(["DELETE ALLOCATIONS"])
      : new Set(["DELETE ALL ALLOCATIONS", "DELETE ALLOCATIONS"]);
    if (!accepted.has(normalized)) {
      setAllocErr(`Confirmation text mismatch. Type ${expectedText} exactly.`);
      return;
    }

    setAllocListLoading(true);
    setAllocErr("");
    try {
      const res = await apiFetch("/api/availability/allocations/clear", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          scope: allocScope || undefined,
          dateFrom: allocDateFrom,
          dateTo: allocDateTo,
          fieldKey: allocFieldKey || undefined,
        }),
      });
      setAllocOk(`Deleted ${res?.deleted ?? 0} allocations.`);
      setToast({ tone: "success", message: `Deleted ${res?.deleted ?? 0} allocations.` });
      await loadAllocations();
    } catch (e) {
      setAllocErr(formatApiError(e, "Delete failed."));
    } finally {
      setAllocListLoading(false);
    }
  }

  async function previewAllocations() {
    if (!genDivision) return;
    setGenLoading(true);
    setAllocErr("");
    setAllocOk("");
    setGenStatus("Running preview...");
    const dateError = validateIsoDates([
      { label: "Date from", value: genDateFrom, required: true },
      { label: "Date to", value: genDateTo, required: true },
    ]);
    if (dateError) {
      setGenLoading(false);
      setGenStatus(dateError);
      return setAllocErr(dateError);
    }
    try {
      const data = await apiFetch("/api/availability/allocations/slots/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: genDivision,
          dateFrom: genDateFrom,
          dateTo: genDateTo,
          fieldKey: genFieldKey || undefined,
        }),
      });
      const slots = Array.isArray(data?.slots) ? data.slots : [];
      const conflicts = Array.isArray(data?.conflicts) ? data.conflicts : [];
      const slotCount = Number.isFinite(Number(data?.slotCount)) ? Number(data.slotCount) : slots.length;
      const conflictCount = Number.isFinite(Number(data?.conflictCount)) ? Number(data.conflictCount) : conflicts.length;
      setGenPreview({ slots, conflicts, failed: [], slotCount, conflictCount, failedCount: 0 });
      setAllocOk(`Preview ready: ${slotCount} candidate slots, ${conflictCount} conflicts.`);
      if (slotCount === 0 && conflictCount === 0) {
        setGenStatus("Preview complete: no candidate slots or conflicts were found for this filter.");
      } else {
        setGenStatus(`Preview complete: ${slotCount} candidate slots, ${conflictCount} conflicts.`);
      }
    } catch (e) {
      const message = formatApiError(e, "Failed to preview allocation slots.");
      setAllocErr(message);
      setGenStatus(message);
      setGenPreview(null);
    } finally {
      setGenLoading(false);
    }
  }

  async function applyAllocations() {
    if (!genDivision) return;
    setGenLoading(true);
    setAllocErr("");
    setAllocOk("");
    setGenStatus("Generating slots...");
    const dateError = validateIsoDates([
      { label: "Date from", value: genDateFrom, required: true },
      { label: "Date to", value: genDateTo, required: true },
    ]);
    if (dateError) {
      setGenLoading(false);
      setGenStatus(dateError);
      return setAllocErr(dateError);
    }
    try {
      const data = await apiFetch("/api/availability/allocations/slots/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          division: genDivision,
          dateFrom: genDateFrom,
          dateTo: genDateTo,
          fieldKey: genFieldKey || undefined,
        }),
      });
      const created = Array.isArray(data?.created) ? data.created : [];
      const conflicts = Array.isArray(data?.conflicts) ? data.conflicts : [];
      const failed = Array.isArray(data?.failed) ? data.failed : [];
      const createdCount = Number.isFinite(Number(data?.createdCount)) ? Number(data.createdCount) : created.length;
      const conflictCount = Number.isFinite(Number(data?.conflictCount)) ? Number(data.conflictCount) : conflicts.length;
      const failedCount = Number.isFinite(Number(data?.failedCount)) ? Number(data.failedCount) : failed.length;

      // Keep result details on-screen so zero-create runs are diagnosable.
      setGenPreview({
        slots: created,
        conflicts,
        failed,
        slotCount: createdCount,
        conflictCount,
        failedCount,
      });
      setAllocOk(`Generate complete: created ${createdCount} slots, conflicts ${conflictCount}, failed writes ${failedCount}.`);
      if (createdCount === 0 && conflictCount === 0 && failedCount === 0) {
        setGenStatus("Generate complete: created 0 slots, and no conflicts or write failures were found for this filter.");
      } else {
        setGenStatus(`Generate complete: created ${createdCount} slots, conflicts ${conflictCount}, failed writes ${failedCount}.`);
      }
      setToast({
        tone: createdCount > 0 ? "success" : conflictCount > 0 || failedCount > 0 ? "warning" : "info",
        message:
          createdCount > 0
            ? `Created ${createdCount} availability slots.`
            : conflictCount > 0 || failedCount > 0
              ? `Created 0 slots. ${conflictCount} conflicts and ${failedCount} write failures blocked generation.`
              : "Created 0 slots.",
      });
    } catch (e) {
      const message = formatApiError(e, "Failed to apply allocation slots.");
      setAllocErr(message);
      setGenStatus(message);
    } finally {
      setGenLoading(false);
    }
  }

  const scopes = useMemo(() => {
    const divs = (divisions || [])
      .filter((d) => d && d.isActive !== false)
      .map((d) => (typeof d === "string" ? d : d.code || d.division || ""))
      .filter(Boolean)
      .sort((a, b) => a.localeCompare(b));
    return ["LEAGUE", ...divs];
  }, [divisions]);

  const fieldLabelMap = useMemo(() => {
    const map = new Map();
    for (const f of fields || []) {
      if (!f?.fieldKey) continue;
      map.set(f.fieldKey, f.displayName || f.fieldKey);
    }
    return map;
  }, [fields]);
  const selectedManualDays = useMemo(
    () => DAY_OPTIONS.filter((day) => manualDays[day]?.enabled),
    [manualDays]
  );

  return (
    <div className="stack">
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />
      {allocErr ? <div className="callout callout--error">{allocErr}</div> : null}
      {allocOk ? <div className="callout callout--ok">{allocOk}</div> : null}

      <div className="card">
        <div className="card__header">
          <div className="h2">Manual allocation builder (no CSV)</div>
          <div className="subtle">
            Set Active, pick weekdays, then fill details for only those selected days.
          </div>
        </div>
        <div className="card__body grid2">
          <label>
            Scope
            <select value={manualScope} onChange={(e) => setManualScope(e.target.value)}>
              {scopes.map((s) => (
                <option key={s} value={s}>
                  {s === "LEAGUE" ? "League-wide" : s}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field
            <select value={manualFieldKey} onChange={(e) => setManualFieldKey(e.target.value)}>
              <option value="">Select field</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName || f.fieldKey}
                </option>
              ))}
            </select>
          </label>
          <label>
            Date from
            <input value={manualDateFrom} onChange={(e) => setManualDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Date to
            <input value={manualDateTo} onChange={(e) => setManualDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label className="inlineCheck">
            <input type="checkbox" checked={manualIsActive} onChange={(e) => setManualIsActive(e.target.checked)} />
            Active
          </label>
        </div>
        <div className="card__body stack gap-2">
          {manualIsActive ? (
            <>
              <div className="row row--wrap gap-2">
                {DAY_OPTIONS.map((day) => (
                  <label key={day} className="inlineCheck">
                    <input
                      type="checkbox"
                      checked={!!manualDays[day]?.enabled}
                      onChange={(e) => updateManualDay(day, { enabled: e.target.checked })}
                    />
                    {day}
                  </label>
                ))}
              </div>
              {!selectedManualDays.length ? <div className="subtle">Select at least one weekday.</div> : null}
              {selectedManualDays.map((day) => {
                const row = manualDays[day] || {};
                const rowType = normalizeManualSlotType(row.slotType);
                return (
                  <div key={day} className="card">
                    <div className="card__body grid2">
                      <div className="font-bold col-span-2">{day}</div>
                      <label>
                        Slot type
                        <select
                          value={rowType}
                          onChange={(e) =>
                            updateManualDay(day, {
                              slotType: normalizeManualSlotType(e.target.value),
                              priorityRank: normalizeManualSlotType(e.target.value) === "practice" ? "" : row.priorityRank,
                            })}
                        >
                          <option value="practice">Practice</option>
                          <option value="game">Game</option>
                          <option value="both">Both</option>
                        </select>
                      </label>
                      <label>
                        Start time
                        <input
                          type="time"
                          value={row.startTime || ""}
                          onChange={(e) => updateManualDay(day, { startTime: e.target.value })}
                        />
                      </label>
                      <label>
                        End time
                        <input
                          type="time"
                          value={row.endTime || ""}
                          onChange={(e) => updateManualDay(day, { endTime: e.target.value })}
                        />
                      </label>
                      <label>
                        Priority rank
                        <input
                          type="number"
                          min="1"
                          value={row.priorityRank || ""}
                          onChange={(e) => updateManualDay(day, { priorityRank: e.target.value })}
                          disabled={rowType === "practice"}
                          placeholder={rowType === "practice" ? "-" : "1"}
                        />
                      </label>
                      <label className="col-span-2">
                        Notes (optional)
                        <input
                          value={row.notes || ""}
                          onChange={(e) => updateManualDay(day, { notes: e.target.value })}
                          placeholder={`${day} notes`}
                        />
                      </label>
                    </div>
                  </div>
                );
              })}
            </>
          ) : (
            <div className="subtle">Turn on Active to select weekdays and configure day details.</div>
          )}
        </div>
        <div className="card__body row gap-2">
          <button
            className="btn btn--primary"
            onClick={addManualAllocations}
            disabled={manualBusy || !manualFieldKey || selectedManualDays.length === 0}
          >
            {manualBusy ? "Saving..." : "Add selected day allocations"}
          </button>
          <button className="btn btn--ghost" onClick={resetManualDays} disabled={manualBusy}>
            Reset weekdays
          </button>
        </div>
        <div className="card__body">
          <div className="font-bold mb-2">Field blackouts for selected field</div>
          <div className="subtle mb-2">
            Optional: add blackout ranges before generating slots so blocked dates are automatically skipped.
          </div>
          {manualBlackouts.length === 0 ? <div className="muted mb-2">No blackouts yet.</div> : null}
          {manualBlackouts.map((b, idx) => (
            <div key={`${idx}-${b.startDate || ""}-${b.endDate || ""}`} className="row row--wrap gap-2 items-center mb-2">
              <input
                value={b.startDate || ""}
                onChange={(e) => updateManualBlackout(idx, "startDate", e.target.value)}
                placeholder="YYYY-MM-DD"
              />
              <span className="muted">to</span>
              <input
                value={b.endDate || ""}
                onChange={(e) => updateManualBlackout(idx, "endDate", e.target.value)}
                placeholder="YYYY-MM-DD"
              />
              <input
                value={b.label || ""}
                onChange={(e) => updateManualBlackout(idx, "label", e.target.value)}
                placeholder="Label"
              />
              <button className="btn btn--ghost" type="button" onClick={() => removeManualBlackout(idx)}>
                Remove
              </button>
            </div>
          ))}
          <div className="row gap-2">
            <button className="btn btn--ghost" type="button" onClick={addManualBlackout}>
              Add blackout
            </button>
            <button
              className="btn"
              type="button"
              onClick={saveManualFieldBlackouts}
              disabled={!manualFieldKey || manualBlackoutBusy}
            >
              {manualBlackoutBusy ? "Saving..." : "Save field blackouts"}
            </button>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Allocation CSV import</div>
          <div className="subtle">
            Upload per-division or league-wide field allocations (use <code>division</code> = <code>LEAGUE</code> or leave blank).
          </div>
        </div>
        <div className="card__body">
          <div className="subtle mb-2">
            Required columns: <code>fieldKey</code>, <code>dateFrom</code>, <code>dateTo</code>, <code>startTime</code>, <code>endTime</code>.
            Optional: <code>division</code>, <code>daysOfWeek</code>, <code>slotType</code> (<code>Practice</code>/<code>Game</code>/<code>Both</code>), <code>priorityRank</code>, <code>notes</code>, <code>isActive</code>.
          </div>
          <div className="row items-end gap-3">
            <label className="flex-1">
              CSV file
              <input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => setAllocFile(e.target.files?.[0] || null)}
                disabled={allocBusy}
              />
            </label>
            <button className="btn" onClick={importAllocationsCsv} disabled={allocBusy || !allocFile}>
              {allocBusy ? "Importing..." : "Upload & Import"}
            </button>
            <button className="btn btn--ghost" onClick={downloadTemplate} disabled={!leagueId}>
              Download CSV template
            </button>
          </div>
          {allocErrors.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Rejected rows ({allocErrors.length})</div>
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Error</th>
                  </tr>
                </thead>
                <tbody>
                  {allocErrors.slice(0, 50).map((x, idx) => (
                    <tr key={idx}>
                      <td>{x.row}</td>
                      <td>{x.error}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {allocErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
          {allocWarnings.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Warnings ({allocWarnings.length})</div>
              <div className="subtle mb-2">
                These rows were skipped because they already exist.
              </div>
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Field Key</th>
                    <th>Warning</th>
                  </tr>
                </thead>
                <tbody>
                  {allocWarnings.slice(0, 50).map((x, idx) => (
                    <tr key={idx}>
                      <td>{x.row}</td>
                      <td>{x.fieldKey || ""}</td>
                      <td>{x.warning}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {allocWarnings.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Allocation list</div>
          <div className="subtle">Review and bulk delete allocation rows.</div>
        </div>
        <div className="card__body grid2">
          <label>
            Scope
            <select value={allocScope} onChange={(e) => setAllocScope(e.target.value)}>
              <option value="">All scopes</option>
              {scopes.map((s) => (
                <option key={s} value={s}>
                  {s === "LEAGUE" ? "League-wide" : s}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field (optional)
            <select value={allocFieldKey} onChange={(e) => setAllocFieldKey(e.target.value)}>
              <option value="">All fields</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName || f.fieldKey}
                </option>
              ))}
            </select>
          </label>
          <label>
            Date from
            <input value={allocDateFrom} onChange={(e) => setAllocDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Date to
            <input value={allocDateTo} onChange={(e) => setAllocDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={loadAllocations} disabled={allocListLoading}>
            {allocListLoading ? "Loading..." : "Load allocations"}
          </button>
          <button className="btn btn--danger" onClick={clearAllocations} disabled={allocListLoading}>
            Delete filtered allocations
          </button>
        </div>
        {allocations.length ? (
          <div className="card__body tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Scope</th>
                  <th>Field</th>
                  <th>Dates</th>
                  <th>Days</th>
                  <th>Time</th>
                  <th>Type</th>
                  <th>Priority</th>
                  <th>Active</th>
                  <th>Notes</th>
                </tr>
              </thead>
              <tbody>
                {allocations.map((a) => (
                  <tr key={a.allocationId}>
                    <td>{a.scope === "LEAGUE" ? "League-wide" : a.scope}</td>
                    <td>{fieldLabelMap.get(a.fieldKey) || a.fieldKey}</td>
                    <td>{a.startsOn} - {a.endsOn}</td>
                    <td>{(a.daysOfWeek || []).join(", ") || "Any"}</td>
                    <td>{a.startTimeLocal} - {a.endTimeLocal}</td>
                    <td>{a.slotType || "practice"}</td>
                    <td>{a.priorityRank || "-"}</td>
                    <td>{a.isActive ? "Yes" : "No"}</td>
                    <td>{a.notes || ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Generate availability slots from allocations</div>
          <div className="subtle">Allocations turn into availability slots for scheduling.</div>
        </div>
        <div className="card__body grid2">
          <label>
            Division
            <select value={genDivision} onChange={(e) => setGenDivision(e.target.value)}>
              {divisions.map((d) => (
                <option key={d.code || d.division} value={d.code || d.division}>
                  {d.name ? `${d.name} (${d.code || d.division})` : d.code || d.division}
                </option>
              ))}
            </select>
          </label>
          <label>
            Field (optional)
            <select value={genFieldKey} onChange={(e) => setGenFieldKey(e.target.value)}>
              <option value="">All fields</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName || f.fieldKey}
                </option>
              ))}
            </select>
          </label>
          <label>
            Date from
            <input value={genDateFrom} onChange={(e) => setGenDateFrom(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
          <label>
            Date to
            <input value={genDateTo} onChange={(e) => setGenDateTo(e.target.value)} placeholder="YYYY-MM-DD" />
          </label>
        </div>
        <div className="card__body row gap-2">
          <button className="btn" onClick={previewAllocations} disabled={genLoading || !genDivision}>
            {genLoading ? "Loading..." : "Preview slots"}
          </button>
          <button className="btn btn--primary" onClick={applyAllocations} disabled={genLoading || !genDivision}>
            Generate slots
          </button>
        </div>
        <div className="card__body subtle">
          Tip: if generation creates 0 slots with conflicts, clear or move overlapping non-availability slots for that field/date window.
        </div>
        {genStatus ? <div className="card__body"><div className="callout">{genStatus}</div></div> : null}
        {genPreview ? (
          <div className="card__body">
            <div className="row row--wrap gap-4">
              <div className="layoutStat">
                <div className="layoutStat__value">{genPreview.slotCount ?? genPreview.slots?.length ?? 0}</div>
                <div className="layoutStat__label">Slots</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{genPreview.conflictCount ?? genPreview.conflicts?.length ?? 0}</div>
                <div className="layoutStat__label">Conflicts</div>
              </div>
              <div className="layoutStat">
                <div className="layoutStat__value">{genPreview.failedCount ?? genPreview.failed?.length ?? 0}</div>
                <div className="layoutStat__label">Failed Writes</div>
              </div>
            </div>
            {genPreview.conflicts?.length ? (
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
                    {genPreview.conflicts.slice(0, 20).map((c, idx) => (
                      <tr key={`${c.gameDate}-${c.startTime}-${idx}`}>
                        <td>{c.gameDate}</td>
                        <td>{c.startTime}-{c.endTime}</td>
                        <td>{fieldLabelMap.get(c.fieldKey) || c.fieldKey}</td>
                        <td>{c.division}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {(genPreview.conflictCount ?? genPreview.conflicts.length) > 20 ? (
                  <div className="subtle">Showing first 20 conflicts.</div>
                ) : null}
              </div>
            ) : null}
            {genPreview.failed?.length ? (
              <div className="mt-3 tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Time</th>
                      <th>Field</th>
                      <th>Status</th>
                      <th>Code</th>
                    </tr>
                  </thead>
                  <tbody>
                    {genPreview.failed.slice(0, 20).map((f, idx) => (
                      <tr key={`${f.gameDate}-${f.startTime}-${idx}`}>
                        <td>{f.gameDate}</td>
                        <td>{f.startTime}-{f.endTime}</td>
                        <td>{fieldLabelMap.get(f.fieldKey) || f.fieldKey}</td>
                        <td>{f.status ?? "-"}</td>
                        <td>{f.code || "-"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {(genPreview.failedCount ?? genPreview.failed.length) > 20 ? (
                  <div className="subtle">Showing first 20 failed writes.</div>
                ) : null}
              </div>
            ) : null}
          </div>
        ) : null}
      </div>
    </div>
  );
}
