import { useMemo, useState } from "react";
import { apiFetch } from "../lib/api";

const PARSER_OPTIONS = [
  { value: "season_weekday_grid", label: "Season Weekday Grid" },
  { value: "weekend_grid", label: "Weekend Grid" },
  { value: "reference_grid", label: "Reference Grid" },
  { value: "ignore", label: "Ignore" },
];

const ACTION_OPTIONS = [
  { value: "ingest", label: "Ingest" },
  { value: "reference", label: "Reference" },
  { value: "ignore", label: "Ignore" },
];

export default function FieldInventoryImportManager({ leagueId }) {
  const [sourceWorkbookUrl, setSourceWorkbookUrl] = useState("");
  const [seasonLabel, setSeasonLabel] = useState("");
  const [workbook, setWorkbook] = useState(null);
  const [selectedTabs, setSelectedTabs] = useState([]);
  const [preview, setPreview] = useState(null);
  const [commitPreview, setCommitPreview] = useState(null);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [mappingDrafts, setMappingDrafts] = useState({});
  const [classificationDrafts, setClassificationDrafts] = useState({});

  const sortedFields = useMemo(
    () => [...(preview?.canonicalFields || [])].sort((a, b) => a.canonicalFieldName.localeCompare(b.canonicalFieldName)),
    [preview]
  );

  function resetMessages() {
    setError("");
    setMessage("");
  }

  function updateTab(tabName, patch) {
    setSelectedTabs((current) =>
      current.map((tab) => (tab.tabName === tabName ? { ...tab, ...patch } : tab))
    );
  }

  async function loadWorkbook() {
    resetMessages();
    setBusy("load");
    try {
      const result = await apiFetch("/api/field-inventory/workbook/inspect", {
        method: "POST",
        body: JSON.stringify({ sourceWorkbookUrl }),
      });
      setWorkbook(result);
      setSelectedTabs(
        (result?.tabs || []).map((tab) => ({
          tabName: tab.tabName,
          parserType: tab.inferredParserType,
          actionType: tab.inferredActionType,
          selected: tab.inferredActionType !== "ignore",
        }))
      );
      setPreview(null);
      setCommitPreview(null);
      setMessage("Workbook loaded. Select tabs and parse a preview.");
    } catch (e) {
      setError(formatImportError(e));
    } finally {
      setBusy("");
    }
  }

  async function parsePreview() {
    resetMessages();
    setBusy("preview");
    try {
      const result = await apiFetch("/api/field-inventory/preview", {
        method: "POST",
        body: JSON.stringify({
          sourceWorkbookUrl,
          seasonLabel: seasonLabel || null,
          selectedTabs,
        }),
      });
      setPreview(result);
      setCommitPreview(result?.commitPreview || null);
      setSeasonLabel(result?.run?.seasonLabel || seasonLabel);
      setMessage("Preview parsed and stored in staging.");
    } catch (e) {
      setError(formatImportError(e));
    } finally {
      setBusy("");
    }
  }

  async function stageResults() {
    if (!preview?.run?.id) return;
    resetMessages();
    setBusy("stage");
    try {
      const result = await apiFetch(`/api/field-inventory/runs/${preview.run.id}/stage`, {
        method: "PATCH",
      });
      setPreview(result);
      setMessage("Preview marked as staged. Live inventory is still unchanged.");
    } catch (e) {
      setError(formatImportError(e));
    } finally {
      setBusy("");
    }
  }

  async function saveFieldMapping(item) {
    const nextFieldId = mappingDrafts[item.id];
    const field = sortedFields.find((option) => option.fieldId === nextFieldId);
    if (!field || !preview?.run?.id) return;
    resetMessages();
    setBusy(`map:${item.id}`);
    try {
      const result = await apiFetch("/api/field-inventory/field-aliases", {
        method: "POST",
        body: JSON.stringify({
          rawFieldName: item.rawValue,
          canonicalFieldId: field.fieldId,
          canonicalFieldName: field.canonicalFieldName,
          runId: preview.run.id,
          saveForFuture: true,
        }),
      });
      setPreview(result);
      setMessage(`Saved mapping for ${item.rawValue}.`);
    } catch (e) {
      setError(formatImportError(e));
    } finally {
      setBusy("");
    }
  }

  async function saveTabClassification(item) {
    const draft = classificationDrafts[item.id] || {};
    if (!preview?.run?.id) return;
    resetMessages();
    setBusy(`classify:${item.id}`);
    try {
      const result = await apiFetch("/api/field-inventory/tab-classifications", {
        method: "POST",
        body: JSON.stringify({
          rawTabName: item.sourceTab,
          parserType: draft.parserType || item.suggestedResolution?.parserType || "ignore",
          actionType: draft.actionType || item.suggestedResolution?.actionType || "ignore",
          workbookTitlePattern: preview?.run?.sourceWorkbookTitle || "",
          runId: preview.run.id,
          saveForFuture: true,
        }),
      });
      setPreview(result);
      setMessage(`Saved tab classification for ${item.sourceTab}.`);
    } catch (e) {
      setError(formatImportError(e));
    } finally {
      setBusy("");
    }
  }

  async function updateReviewItem(item, status) {
    if (!preview?.run?.id) return;
    resetMessages();
    setBusy(`review:${item.id}`);
    try {
      const result = await apiFetch(`/api/field-inventory/runs/${preview.run.id}/review-items/${item.id}`, {
        method: "PATCH",
        body: JSON.stringify({
          status,
          chosenResolution: item.chosenResolution || {},
          saveDecisionForFuture: false,
        }),
      });
      setPreview(result);
      setMessage(`Review item marked ${status.replace("_", " ")}.`);
    } catch (e) {
      setError(formatImportError(e));
    } finally {
      setBusy("");
    }
  }

  async function runCommit(mode, dryRun) {
    if (!preview?.run?.id) return;
    resetMessages();
    if (!dryRun && !window.confirm(`Run ${mode} against staged field inventory? This writes to the dedicated live inventory store.`)) {
      return;
    }
    setBusy(`${mode}:${dryRun ? "dry" : "live"}`);
    try {
      const result = await apiFetch(`/api/field-inventory/runs/${preview.run.id}/commit`, {
        method: "POST",
        body: JSON.stringify({
          mode,
          dryRun,
          replaceExistingSeason: true,
        }),
      });
      setPreview(result);
      setCommitPreview(result?.commitPreview || null);
      setMessage(dryRun ? `${mode} dry run completed.` : `${mode} completed against live inventory storage.`);
    } catch (e) {
      setError(formatImportError(e));
    } finally {
      setBusy("");
    }
  }

  return (
    <div className="stack gap-4">
      <div className="callout">
        <div className="font-bold mb-2">Staging safety</div>
        <div className="subtle">
          This workflow always parses into separate staging tables first. Nothing is written into live field inventory records until you run an explicit import or upsert action.
        </div>
      </div>

      <div className="callout">
        <div className="font-bold mb-2">Google Sheets access</div>
        <div className="subtle">
          Use a public workbook link. If workbook load returns `401` or `403`, the sheet is usually not shared for anonymous view/download. Set it to &quot;Anyone with the link can view&quot; and retry.
        </div>
      </div>

      {error ? <div className="callout callout--error">{error}</div> : null}
      {message ? <div className="callout callout--ok">{message}</div> : null}

      <div className="grid2">
        <label>
          Google Sheets URL
          <input
            value={sourceWorkbookUrl}
            onChange={(e) => setSourceWorkbookUrl(e.target.value)}
            placeholder="https://docs.google.com/spreadsheets/d/..."
          />
        </label>
        <label>
          Season label
          <input
            value={seasonLabel}
            onChange={(e) => setSeasonLabel(e.target.value)}
            placeholder="Spring 2026"
          />
        </label>
      </div>

      <div className="row gap-2">
        <button className="btn btn--primary" type="button" onClick={loadWorkbook} disabled={!leagueId || busy === "load"}>
          {busy === "load" ? "Loading..." : "Load Workbook"}
        </button>
        <button className="btn btn--ghost" type="button" onClick={parsePreview} disabled={!selectedTabs.some((tab) => tab.selected) || busy === "preview"}>
          {busy === "preview" ? "Parsing..." : "Parse Preview"}
        </button>
      </div>

      {workbook ? (
        <div className="card">
          <div className="card__header">
            <div className="h2">Workbook Tabs</div>
            <div className="subtle">{workbook.sourceWorkbookTitle} - {workbook.tabs.length} tabs</div>
          </div>
          <div className="card__body overflow-x-auto">
            <table className="table" aria-label="Workbook tabs">
              <thead>
                <tr>
                  <th>Select</th>
                  <th>Tab</th>
                  <th>Parser</th>
                  <th>Action</th>
                  <th>Confidence</th>
                  <th>Cells</th>
                  <th>Merged</th>
                  <th>Reason</th>
                </tr>
              </thead>
              <tbody>
                {workbook.tabs.map((tab) => {
                  const current = selectedTabs.find((item) => item.tabName === tab.tabName) || {
                    tabName: tab.tabName,
                    parserType: tab.inferredParserType,
                    actionType: tab.inferredActionType,
                    selected: false,
                  };
                  return (
                    <tr key={tab.tabName}>
                      <td>
                        <input
                          type="checkbox"
                          checked={!!current.selected}
                          onChange={(e) => updateTab(tab.tabName, { selected: e.target.checked })}
                        />
                      </td>
                      <td>{tab.tabName}</td>
                      <td>
                        <select value={current.parserType} onChange={(e) => updateTab(tab.tabName, { parserType: e.target.value })}>
                          {PARSER_OPTIONS.map((option) => (
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <select value={current.actionType} onChange={(e) => updateTab(tab.tabName, { actionType: e.target.value })}>
                          {ACTION_OPTIONS.map((option) => (
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>{tab.confidence}</td>
                      <td>{tab.nonEmptyCellCount}</td>
                      <td>{tab.mergedRangeCount}</td>
                      <td className="subtle">{tab.reason}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      {preview ? (
        <>
          <div className="grid2 xl:grid-cols-4 gap-3">
            <SummaryCard label="Selected Tabs" value={preview.run.summaryCounts.selectedTabs} />
            <SummaryCard label="Parsed Records" value={preview.run.summaryCounts.parsedRecords} />
            <SummaryCard label="Warnings" value={preview.run.summaryCounts.warnings} />
            <SummaryCard label="Review Items" value={preview.run.summaryCounts.reviewItems} />
            <SummaryCard label="Unmapped Fields" value={preview.run.summaryCounts.unmappedFields} />
            <SummaryCard label="Run Status" value={preview.run.status} />
          </div>

          <div className="row gap-2">
            <button className="btn btn--primary" type="button" onClick={stageResults} disabled={busy === "stage"}>
              {busy === "stage" ? "Staging..." : "Stage Results"}
            </button>
            <button className="btn btn--ghost" type="button" onClick={() => runCommit("upsert", true)} disabled={!preview.records.length || busy === "upsert:dry"}>
              Dry Run Upsert
            </button>
            <button className="btn btn--ghost" type="button" onClick={() => runCommit("import", false)} disabled={!preview.records.length || busy === "import:live"}>
              Run Import
            </button>
            <button className="btn btn--ghost" type="button" onClick={() => runCommit("upsert", false)} disabled={!preview.records.length || busy === "upsert:live"}>
              Run Upsert
            </button>
          </div>

          {!preview.records.length ? (
            <div className="callout">
              <div className="font-bold mb-2">No records were parsed</div>
              <div className="subtle">
                SportsCH could inspect the workbook, but the selected tabs did not match the expected inventory grid layout closely enough to create staged records. Use the review queue to mark non-inventory tabs as ignore/reference, then retry the AGSA inventory tabs after checking date columns, time headers, and saved tab classifications.
              </div>
            </div>
          ) : null}

          {commitPreview ? (
            <div className="callout">
              <div className="font-bold mb-2">Commit Preview</div>
              <div className="grid2">
                <div>Create: {commitPreview.createCount}</div>
                <div>Update: {commitPreview.updateCount}</div>
                <div>Delete: {commitPreview.deleteCount}</div>
                <div>Unchanged: {commitPreview.unchangedCount}</div>
                <div>Skipped unmapped: {commitPreview.skippedUnmappedCount}</div>
                <div>Season: {commitPreview.seasonLabel}</div>
              </div>
            </div>
          ) : null}

          <div className="card">
            <div className="card__header">
              <div className="h2">Warnings</div>
              <div className="subtle">Reference-only conflicts and parser observations do not block staging.</div>
            </div>
            <div className="card__body">
              {preview.warnings.length ? (
                <ul className="stack gap-2">
                  {preview.warnings.map((warning) => (
                    <li key={warning.id} className="callout">
                      <div className="font-bold">{warning.code}</div>
                      <div>{warning.message}</div>
                      <div className="subtle">{warning.sourceTab}{warning.sourceCellRange ? ` - ${warning.sourceCellRange}` : ""}</div>
                    </li>
                  ))}
                </ul>
              ) : (
                <div className="subtle">No warnings.</div>
              )}
            </div>
          </div>

          <div className="card">
            <div className="card__header">
              <div className="h2">Review Queue</div>
              <div className="subtle">Save reusable field and tab decisions so future imports need less cleanup.</div>
            </div>
            <div className="card__body stack gap-3">
              {preview.reviewItems.length ? (
                preview.reviewItems.map((item) => (
                  <div key={item.id} className="callout">
                    <div className="row items-center justify-between gap-2">
                      <div>
                        <div className="font-bold">{item.title}</div>
                        <div className="subtle">{item.itemType} - {item.severity} - {item.status}</div>
                      </div>
                      <div className="subtle">{item.sourceTab}{item.sourceCellRange ? ` - ${item.sourceCellRange}` : ""}</div>
                    </div>
                    <div className="mt-2">{item.description}</div>
                    {item.rawValue ? <div className="subtle mt-2">Source value: {item.rawValue}</div> : null}
                    {item.itemType === "field_mapping" ? (
                      <div className="row gap-2 mt-3">
                        <select
                          value={mappingDrafts[item.id] || ""}
                          onChange={(e) => setMappingDrafts((current) => ({ ...current, [item.id]: e.target.value }))}
                        >
                          <option value="">Select canonical field</option>
                          {sortedFields.map((field) => (
                            <option key={field.fieldId} value={field.fieldId}>
                              {field.canonicalFieldName}
                            </option>
                          ))}
                        </select>
                        <button className="btn btn--ghost" type="button" onClick={() => saveFieldMapping(item)} disabled={!mappingDrafts[item.id]}>
                          Save Mapping
                        </button>
                      </div>
                    ) : null}
                    {item.itemType === "tab_classification" ? (
                      <div className="row gap-2 mt-3">
                        <select
                          value={classificationDrafts[item.id]?.parserType || item.suggestedResolution?.parserType || "ignore"}
                          onChange={(e) =>
                            setClassificationDrafts((current) => ({
                              ...current,
                              [item.id]: { ...current[item.id], parserType: e.target.value },
                            }))
                          }
                        >
                          {PARSER_OPTIONS.map((option) => (
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                        <select
                          value={classificationDrafts[item.id]?.actionType || item.suggestedResolution?.actionType || "ignore"}
                          onChange={(e) =>
                            setClassificationDrafts((current) => ({
                              ...current,
                              [item.id]: { ...current[item.id], actionType: e.target.value },
                            }))
                          }
                        >
                          {ACTION_OPTIONS.map((option) => (
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                        <button className="btn btn--ghost" type="button" onClick={() => saveTabClassification(item)}>
                          Save Classification
                        </button>
                      </div>
                    ) : null}
                    {item.itemType !== "field_mapping" && item.itemType !== "tab_classification" ? (
                      <div className="row gap-2 mt-3">
                        <button className="btn btn--ghost" type="button" onClick={() => updateReviewItem(item, "resolved")}>
                          Mark Resolved
                        </button>
                        <button className="btn btn--ghost" type="button" onClick={() => updateReviewItem(item, "ignored")}>
                          Ignore
                        </button>
                      </div>
                    ) : null}
                  </div>
                ))
              ) : (
                <div className="subtle">No review items.</div>
              )}
            </div>
          </div>

          <div className="card">
            <div className="card__header">
              <div className="h2">Staged Records</div>
              <div className="subtle">Preview of normalized availability and utilization stored separately from live scheduling data.</div>
            </div>
            <div className="card__body overflow-x-auto">
              <table className="table" aria-label="Staged field inventory records">
                <thead>
                  <tr>
                    <th>Date</th>
                    <th>Time</th>
                    <th>Field</th>
                    <th>Availability</th>
                    <th>Utilization</th>
                    <th>Used By</th>
                    <th>Assigned</th>
                    <th>Source</th>
                    <th>Confidence</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.records.length ? (
                    preview.records.slice(0, 250).map((record) => (
                      <tr key={record.id}>
                        <td>{record.date}</td>
                        <td>{record.startTime} - {record.endTime}</td>
                        <td>{record.fieldName || record.rawFieldName}</td>
                        <td>{record.availabilityStatus}</td>
                        <td>{record.utilizationStatus}</td>
                        <td>{record.usedBy || "-"}</td>
                        <td>{record.assignedTeamOrEvent || "-"}</td>
                        <td>{record.sourceTab} - {record.sourceCellRange}</td>
                        <td>{record.confidence}</td>
                      </tr>
                    ))
                  ) : (
                    <tr>
                      <td colSpan="9" className="subtle">No staged records. Fix blocking review items or adjust tab selection and parser choices, then parse again.</td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}

function SummaryCard({ label, value }) {
  return (
    <div className="card">
      <div className="card__body">
        <div className="subtle">{label}</div>
        <div className="h2">{value}</div>
      </div>
    </div>
  );
}

function formatImportError(error) {
  const message = error?.message || "Request failed.";
  const originalMessage = error?.originalMessage || "";
  const detailMessage = typeof error?.details?.exception === "string" ? error.details.exception : "";
  const combined = `${message} ${originalMessage} ${detailMessage}`.trim();
  if ((error?.status === 502 || error?.code === "WORKBOOK_LOAD_FAILED") && /\b401\b|\b403\b/.test(combined)) {
    return `${message} This usually means Google Sheets is not allowing anonymous view/download for the workbook. Set the workbook to "Anyone with the link can view" and try again.`;
  }

  return detailMessage && detailMessage !== message
    ? `${message} ${detailMessage}`
    : message;
}
